using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 临时踏板生成器 v0.1
// 把蓝图路线图变成可站立的实体方块，让你在没有任何地形的情况下
// 也能沿螺旋爬上山，验证攀爬节奏。
//
// 用法：
//   1. 场景里挂了 BlueprintProjector 的那个物体上，再挂这个组件
//   2. 点 Inspector 的「生成踏板」，每个节点变成一个方块、每条边变成一条斜坡
//   3. 用攀爬角色爬上去；「清除踏板」删掉所有方块
//
// 这些是测试脚手架，正式开发时换成你建的地形。
// ============================================================

namespace MapGen
{
    public class TestPlatformBuilder : MonoBehaviour
    {
        public BlueprintProjector projector;

        [Header("外观")]
        [Tooltip("休息点踏板的尺寸（米）")]
        public Vector3 restSize = new Vector3(3f, 0.5f, 3f);
        [Tooltip("营火/起点/峰顶平台的尺寸")]
        public Vector3 campSize = new Vector3(6f, 0.5f, 6f);
        [Tooltip("边（攀爬段）是否生成连接斜坡，方便走上去")]
        public bool buildRamps = true;
        [Tooltip("斜坡宽度")]
        public float rampWidth = 1.5f;

        [Header("材质颜色")]
        public Color restColor = new Color(0.8f, 0.8f, 0.8f);
        public Color campColor = new Color(1f, 0.55f, 0.15f);
        public Color summitColor = new Color(1f, 0.3f, 0.3f);
        public Color startColor = new Color(0.4f, 0.9f, 0.5f);
        public Color rampColor = new Color(0.55f, 0.55f, 0.6f);

        // 生成出来的物体都挂在这个子物体下，方便一键清除
        const string RootName = "_TestPlatforms";

        public void Build()
        {
            Clear();
            if (projector == null || projector.graph == null)
            {
                Debug.LogWarning("[踏板] 请先在 BlueprintProjector 上点「投射蓝图」。");
                return;
            }
            var g = projector.graph;
            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);

            var sharedMatCache = new Dictionary<Color, Material>();
            // 自动适配渲染管线：URP 用 URP/Lit，内置管线回退到 Standard。
            Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
            if (litShader == null) litShader = Shader.Find("Standard");
            Material Mat(Color c)
            {
                if (!sharedMatCache.TryGetValue(c, out var m))
                {
                    m = new Material(litShader);
                    // URP/Lit 的主色属性是 _BaseColor，Standard 是 _Color，两个都设以兼容。
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                    m.color = c;
                    sharedMatCache[c] = m;
                }
                return m;
            }

            // 节点 → 方块
            var nodePos = new Dictionary<int, Vector3>();
            foreach (var n in g.nodes)
            {
                Vector3 p = projector.ToWorld(n.pos);
                nodePos[n.id] = p;

                Vector3 size; Color col;
                switch (n.kind)
                {
                    case NodeKind.Campfire: size = campSize; col = campColor; break;
                    case NodeKind.Summit: size = campSize; col = summitColor; break;
                    case NodeKind.Start: size = campSize; col = startColor; break;
                    case NodeKind.Loot: size = restSize; col = new Color(0.95f, 0.85f, 0.3f); break;
                    default: size = restSize; col = restColor; break;
                }

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Node_{n.id}_{n.kind}";
                cube.transform.SetParent(root.transform, false);
                cube.transform.position = p;
                cube.transform.localScale = size;
                cube.GetComponent<Renderer>().sharedMaterial = Mat(col);
            }

            // 边 → 斜坡（只给主路径和能走的边建，跳跃段不建，留空让玩家跳）
            if (buildRamps)
            {
                foreach (var e in g.edges)
                {
                    if (e.type == EdgeType.JumpGap || e.type == EdgeType.PillarChain) continue;
                    Vector3 a = nodePos[e.from];
                    Vector3 b = nodePos[e.to];
                    Vector3 mid = (a + b) * 0.5f;
                    Vector3 dir = b - a;
                    float len = dir.magnitude;
                    if (len < 0.01f) continue;

                    var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ramp.name = $"Ramp_{e.from}_{e.to}";
                    ramp.transform.SetParent(root.transform, false);
                    ramp.transform.position = mid;
                    ramp.transform.rotation = Quaternion.LookRotation(dir.normalized);
                    ramp.transform.localScale = new Vector3(rampWidth, 0.3f, len);
                    ramp.GetComponent<Renderer>().sharedMaterial = Mat(rampColor);
                }
            }

            Debug.Log($"[踏板] 已生成 {g.nodes.Count} 个踏板" +
                      (buildRamps ? " + 斜坡" : "") + "。用攀爬角色爬上去试试。");
        }

        public void Clear()
        {
            var existing = transform.Find(RootName);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }
        }
    }
}
