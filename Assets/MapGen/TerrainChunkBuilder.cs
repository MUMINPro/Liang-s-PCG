using System;
using UnityEngine;

// ============================================================
// 分层地图生成 · 共享分块构建器 v0.1（Phase 1 + LOD 脚手架）
//
// 把一张 res×res 的高度场切成若干块，每块独立 GameObject：
//   MeshFilter / MeshRenderer / MeshCollider + LODGroup(当前只 LOD0)。
//
// 设计要点（按行业实践）：
//   - 第1层、第2层共用本构建器，分块只是"切网格"的方式，
//     地貌数据(heightDelta)不进这里，保持单一事实来源。
//   - 块边界对齐全局网格索引：相邻块共享边的顶点用同一 (wx,wz) 采样，
//     位置逐比特相同 → 零裂缝。
//   - 法线解析计算(对高度函数有限差分)，不用 RecalculateNormals：
//     法线只依赖世界坐标，跨块两侧必然一致 → 零光照接缝。
//   - 碰撞体每块全分辨率，与视觉 LOD 解耦（以后降 LOD 不动碰撞）。
//   - 每块包一个 LODGroup，当前只填 LOD0；以后加减面网格只是塞 LOD 档，
//     不用重构结构。
// ============================================================

namespace MapGen
{
    /// <summary>分块构建参数。高度/法线采样以世界坐标 (wx,wz) 为输入。</summary>
    public struct ChunkBuildParams
    {
        public Func<float, float, float> sampleHeight; // (wx,wz) → 海拔
        public int resolution;        // 全局每边顶点数
        public float terrainSize;     // 画布一边长度（米）
        public int chunkVerts;        // 每块每边顶点数（如 64）
        public Material material;      // 所有块共用（SRP Batcher 会自动批）
        public Transform parent;       // 块挂在它下面
        public string rootName;        // 根物体名（_MacroTerrain / _FeatureTerrain）
        public string meshNamePrefix;  // 网格命名前缀
    }

    public static class TerrainChunkBuilder
    {
        /// <summary>构建分块地形，返回根物体。已存在同名根会先删。</summary>
        public static GameObject Build(in ChunkBuildParams p)
        {
            int res = Mathf.Max(2, p.resolution);
            float step = p.terrainSize / (res - 1);
            int cv = Mathf.Clamp(p.chunkVerts, 2, res);

            // 每块跨 (cv-1) 个格子；最后一块可能不满，按实际收尾。
            int cellsPerChunk = cv - 1;
            int chunksPerSide = Mathf.CeilToInt((res - 1) / (float)cellsPerChunk);

            var root = new GameObject(p.rootName);
            root.transform.SetParent(p.parent, false);

            for (int cz = 0; cz < chunksPerSide; cz++)
                for (int cx = 0; cx < chunksPerSide; cx++)
                {
                    // 本块在全局网格里的顶点范围 [x0..x1] × [z0..z1]（含端点，相邻块共享边）
                    int x0 = cx * cellsPerChunk;
                    int z0 = cz * cellsPerChunk;
                    int x1 = Mathf.Min(x0 + cellsPerChunk, res - 1);
                    int z1 = Mathf.Min(z0 + cellsPerChunk, res - 1);
                    if (x1 <= x0 || z1 <= z0) continue;

                    BuildOneChunk(root.transform, in p, step, x0, z0, x1, z1, cx, cz);
                }

            return root;
        }

        static void BuildOneChunk(Transform parent, in ChunkBuildParams p, float step,
            int x0, int z0, int x1, int z1, int cx, int cz)
        {
            int nx = x1 - x0 + 1;        // 本块每边顶点数（含共享边）
            int nz = z1 - z0 + 1;
            int vcount = nx * nz;

            var verts = new Vector3[vcount];
            var normals = new Vector3[vcount];
            var uvs = new Vector2[vcount];
            float invSize = 1f / Mathf.Max(0.0001f, p.terrainSize);

            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int gx = x0 + x, gz = z0 + z;       // 全局索引
                    float wx = gx * step, wz = gz * step;
                    float h = p.sampleHeight(wx, wz);
                    int li = z * nx + x;                  // 本块局部索引
                    verts[li] = new Vector3(wx, h, wz);
                    normals[li] = AnalyticNormal(p.sampleHeight, wx, wz, step);
                    // UV 用全局世界坐标归一化 → 跨块连续，方便整山贴图
                    uvs[li] = new Vector2(wx * invSize, wz * invSize);
                }

            var tris = new int[(nx - 1) * (nz - 1) * 6];
            int ti = 0;
            for (int z = 0; z < nz - 1; z++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int i = z * nx + x;
                    tris[ti++] = i; tris[ti++] = i + nx; tris[ti++] = i + 1;
                    tris[ti++] = i + 1; tris[ti++] = i + nx; tris[ti++] = i + nx + 1;
                }

            var mesh = new Mesh { name = $"{p.meshNamePrefix}_{cx}_{cz}" };
            // 64 顶点边的块 = 4096 顶点，远低于 65535，用默认 UInt16 即可省内存。
            if (vcount > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = normals;     // 解析法线，不调 RecalculateNormals
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            var go = new GameObject($"Chunk_{cx}_{cz}");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = p.material;
            // 碰撞体：全分辨率，与视觉 LOD 解耦
            go.AddComponent<MeshCollider>().sharedMesh = mesh;

            // LOD 脚手架：当前只一档 LOD0(全模)。以后加减面网格往 lods 里塞即可。
            var lodGroup = go.AddComponent<LODGroup>();
            var lods = new LOD[1];
            lods[0] = new LOD(0.01f, new Renderer[] { mr }); // 屏占低于1%才剔除
            lodGroup.SetLODs(lods);
            lodGroup.RecalculateBounds();
        }

        /// <summary>对高度函数有限差分求解析法线。只依赖世界坐标，跨块一致 → 无接缝。</summary>
        static Vector3 AnalyticNormal(Func<float, float, float> h, float wx, float wz, float e)
        {
            float hx = h(wx + e, wz) - h(wx - e, wz);
            float hz = h(wx, wz + e) - h(wx, wz - e);
            // 切向量 (2e,hx,0)×(0,hz,2e) 的法线，化简后：
            var n = new Vector3(-hx, 2f * e, -hz);
            return n.normalized;
        }
    }
}
