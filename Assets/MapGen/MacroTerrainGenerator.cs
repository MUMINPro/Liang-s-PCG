using UnityEngine;

// ============================================================
// 分层地图生成 · 第 1 层：宏观山体 v0.1
// 生成一座低模山体网格（毛坯），作为后续所有层的画布。
// 你在它上面手工雕刻精修，第 2 层小峰群、第 3 层路网都叠在它上面。
//
// 用法：
//   1. 场景建空物体，挂本组件
//   2. Inspector 调参数，点「生成地形」
//   3. 不满意就改参数重新生成；满意后这就是你的雕刻毛坯
//
// 设计要点：
//   - 山形 = 径向轮廓曲线（falloff，你可在 Inspector 拖曲线）× 山高
//   - 叠加 3 层 Perlin 噪声做自然起伏
//   - 同种子永远生成同一座山（确定性）
// ============================================================

namespace MapGen
{
    public class MacroTerrainGenerator : MonoBehaviour
    {
        [Header("种子")]
        public int seed = 12345;

        [Header("画布尺寸")]
        [Tooltip("地形一边的长度（米）")]
        public float terrainSize = 600f;
        [Tooltip("网格分辨率（每边顶点数）。已用分块网格，可远超旧的 256 限制。\n" +
                 "600m 画布下：256≈2.3m/顶点  512≈1.2m/顶点  1024≈0.6m/顶点(细节丰富)。\n" +
                 "越高越精细；分块后视锥剔除+局部碰撞分摊，高分辨率更可用。")]
        [Range(64, 2048)] public int resolution = 512;
        [Tooltip("每块每边顶点数。64 是剔除粒度与批次开销的甜区。\n" +
                 "分块只切网格，山形/高度函数不受影响。")]
        [Range(16, 128)] public int chunkVerts = 64;

        [Header("主峰")]
        [Tooltip("峰顶高度（米）。建议与 MapDesignConfig 的 totalAltitude 一致")]
        public float mountainHeight = 180f;
        [Tooltip("山体半径（米），从峰顶到山脚的水平距离")]
        public float mountainRadius = 240f;
        [Tooltip("峰顶在画布上的位置（0~1，0.5 = 中心）")]
        public Vector2 peakPosition = new Vector2(0.5f, 0.5f);
        [Tooltip("山形轮廓：横轴 = 离峰顶的距离(0~1)，纵轴 = 高度比例。" +
                 "拖这条曲线就是在捏山的剪影")]
        public AnimationCurve falloff = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, -0.5f),
            new Keyframe(0.45f, 0.55f, -1.2f, -1.2f),
            new Keyframe(1f, 0f, -0.3f, 0f));

        [Header("自然起伏（噪声）")]
        [Tooltip("大尺度起伏强度（米）")]
        public float noiseAmplitude = 22f;
        [Tooltip("噪声尺度。越小起伏越大块")]
        public float noiseScale = 0.008f;
        [Tooltip("细节层数（octaves）")]
        [Range(1, 5)] public int octaves = 3;

        [Header("外观")]
        public Color terrainColor = new Color(0.55f, 0.6f, 0.45f);

        const string MeshChildName = "_MacroTerrain";

        // 噪声偏移缓存：由种子决定，只算一次。
        // SampleHeight 会被生成/叠地貌/贴路线调用几百万次，
        // 绝不能每次都 new System.Random（高分辨率下会 GC 卡死）。
        int cachedSeed = int.MinValue;
        float noiseOffsetX, noiseOffsetZ;

        void EnsureNoiseOffsets()
        {
            if (cachedSeed == seed) return;
            var rng = new System.Random(seed);
            noiseOffsetX = (float)rng.NextDouble() * 1000f;
            noiseOffsetZ = (float)rng.NextDouble() * 1000f;
            cachedSeed = seed;
        }

        public void Generate()
        {
            Clear();
            var p = new ChunkBuildParams
            {
                sampleHeight = SampleHeight,
                resolution = resolution,
                terrainSize = terrainSize,
                chunkVerts = chunkVerts,
                material = MakeMaterial(terrainColor),
                parent = transform,
                rootName = MeshChildName,
                meshNamePrefix = "Macro",
            };
            var root = TerrainChunkBuilder.Build(in p);

            int chunks = root.transform.childCount;
            Debug.Log($"[第1层·宏观山体] 已生成 {resolution}×{resolution} 分块地形" +
                      $"（{chunks} 块，每块 {chunkVerts} 顶点边），峰高 {mountainHeight}m。" +
                      $"这是毛坯，可在其上手工精修。");
        }

        public void Clear()
        {
            var t = transform.Find(MeshChildName);
            if (t != null)
            {
                if (Application.isPlaying) Destroy(t.gameObject);
                else DestroyImmediate(t.gameObject);
            }
        }

        // ---------------- 高度函数：整座山的灵魂 ----------------
        /// <summary>世界平面坐标 → 海拔。后续层（路网/洞口）也会调用它来贴地。</summary>
        public float SampleHeight(float wx, float wz)
        {
            // 噪声随机偏移（由种子决定，缓存一次，不在热路径里 new Random）
            EnsureNoiseOffsets();
            float ox = noiseOffsetX;
            float oz = noiseOffsetZ;

            // 1) 主峰径向轮廓
            float px = peakPosition.x * terrainSize;
            float pz = peakPosition.y * terrainSize;
            float d = Mathf.Sqrt((wx - px) * (wx - px) + (wz - pz) * (wz - pz)) / mountainRadius;
            float profile = falloff.Evaluate(Mathf.Clamp01(d));
            float h = profile * mountainHeight;

            // 2) 多层噪声起伏（山脚也留一点，但靠近峰顶减弱避免削尖）
            float n = 0f, amp = 1f, freq = noiseScale, total = 0f;
            for (int o = 0; o < octaves; o++)
            {
                n += (Mathf.PerlinNoise(wx * freq + ox, wz * freq + oz) - 0.5f) * 2f * amp;
                total += amp;
                amp *= 0.5f; freq *= 2.2f;
            }
            n /= Mathf.Max(0.01f, total);
            float noiseWeight = Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(d)); // 峰顶处噪声减弱
            h += n * noiseAmplitude * noiseWeight;

            return Mathf.Max(0f, h);
        }

        // ---------------- 材质 ----------------
        static Material MakeMaterial(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
            return m;
        }
    }
}
