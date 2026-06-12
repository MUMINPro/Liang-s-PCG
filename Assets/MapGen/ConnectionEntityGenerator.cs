using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 分层地图生成 · 第 4 层：连接实体生成器 v0.1（参考 PEAK）
//
// 第 3 层的横向连接段(虚线)在这一层变成有形态的占位指引：
//   - 藤蔓 Vine     : 从锚点垂下的悬链曲线（PEAK 核心机制）
//   - 桥 Bridge     : 跨越峡谷的水平平板
//   - 岩架 Ledge    : 沿山面的窄长横切道
//   - 洞穴 Cave     : 两端洞口标记 + 连线
//
// 每个占位都带数据 ConnectionRecord（两端点/长度/类型/朝向），
// 配套的 ModelPlacer 思路同第2层：你拖真实模型替换它们。
// ============================================================

namespace MapGen
{
    /// <summary>一条连接实体的记录（纯数据）。</summary>
    [Serializable]
    public class ConnectionRecord
    {
        public LinkKind kind;
        public Vector3 from;        // 起点（世界坐标）
        public Vector3 to;          // 终点
        public float length;        // 3D 长度
        public int seed;            // 本连接的随机种子（摆放变体用）
        // 形态附加参数（PEAK 风格）：
        public float sag;           // 藤蔓的下垂量（米），桥则忽略
        public float width;         // 桥/岩架的宽度
    }

    [RequireComponent(typeof(ClimbRouteGenerator))]
    public class ConnectionEntityGenerator : MonoBehaviour
    {
        [Header("种子")]
        public int seed = 9000;

        [Header("藤蔓（PEAK 核心）")]
        [Tooltip("藤蔓的下垂量占跨度的比例（0=拉直，0.3=明显下垂像 PEAK）")]
        [Range(0f, 0.5f)] public float vineSagFrac = 0.3f;
        [Tooltip("绘制藤蔓曲线的分段数（多了平滑、少了棱角）")]
        [Range(4, 24)] public int vineSegments = 12;

        [Header("桥")]
        public float bridgeWidth = 3.5f;
        [Tooltip("桥微微拱起的高度（0 = 完全水平）")]
        public float bridgeArch = 1.5f;

        [Header("岩架")]
        public float ledgeWidth = 2f;

        [Header("洞穴")]
        public float caveMouthRadius = 4f;

        [Header("显示")]
        public bool showLabels = true;
        [Range(0.2f, 1f)] public float entityAlpha = 0.6f;

        // 产物
        [System.NonSerialized] public List<ConnectionRecord> records = new List<ConnectionRecord>();

        ClimbRouteGenerator routeGen;
        TerrainFeatureGenerator featuresRef;  // 净空采样用
        MacroTerrainGenerator macroRef;
        const string RootName = "_ConnectionEntities";

        float GroundAt(float wx, float wz)
        {
            if (featuresRef != null) return featuresRef.SampleHeightWithFeatures(wx, wz);
            if (macroRef != null) return macroRef.SampleHeight(wx, wz);
            return 0f;
        }

        public void Generate()
        {
            Clear();
            routeGen = GetComponent<ClimbRouteGenerator>();
            if (routeGen == null || routeGen.network == null)
            {
                Debug.LogWarning("[第4层] 需要第3层 ClimbRouteGenerator 已生成路网。");
                return;
            }
            // 用第1、2层做贴地校正（避免嵌入或悬空于真实地貌之上）
            var features = featuresRef = GetComponent<TerrainFeatureGenerator>();
            var macro = macroRef = GetComponent<MacroTerrainGenerator>();

            var net = routeGen.network;
            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);

            records.Clear();
            var rng = new System.Random(seed);
            int cv = 0, cb = 0, cl = 0, cc = 0;

            foreach (var e in net.edges)
            {
                if (!e.isLink) continue;
                var a = net.GetNode(e.from).pos;
                var b = net.GetNode(e.to).pos;
                // 贴地校正
                a = SnapAboveGround(a, features, macro, e.linkKind);
                b = SnapAboveGround(b, features, macro, e.linkKind);

                var rec = new ConnectionRecord
                {
                    kind = e.linkKind, from = a, to = b,
                    length = Vector3.Distance(a, b),
                    seed = rng.Next(),
                };
                CreateEntity(root.transform, e.linkKind, a, b, rec);
                records.Add(rec);
                switch (e.linkKind)
                {
                    case LinkKind.Vine: cv++; break;
                    case LinkKind.Bridge: cb++; break;
                    case LinkKind.Ledge: cl++; break;
                    case LinkKind.Cave: cc++; break;
                }
            }

            Debug.Log($"[第4层·连接实体] 共 {records.Count} 个 — " +
                      $"藤蔓 {cv}，桥 {cb}，岩架 {cl}，洞穴 {cc}。");
        }

        /// <summary>把锚点贴回真实山面（含地貌），按类型抬升不同高度避免嵌入。</summary>
        Vector3 SnapAboveGround(Vector3 p, TerrainFeatureGenerator features,
            MacroTerrainGenerator macro, LinkKind kind)
        {
            float h = features != null
                ? features.SampleHeightWithFeatures(p.x, p.z)
                : (macro != null ? macro.SampleHeight(p.x, p.z) : p.y);
            // 不同连接类型的抬升量（让两端站在山面上方一点点，不嵌入）
            float lift;
            switch (kind)
            {
                case LinkKind.Vine: lift = 1.5f; break;   // 藤蔓挂壁，稍微离面
                case LinkKind.Bridge: lift = 0.8f; break; // 桥稍抬
                case LinkKind.Ledge: lift = 1.0f; break;  // 岩架贴在表面之上
                case LinkKind.Cave: lift = 0.5f; break;   // 洞口贴近地面
                default: lift = 1f; break;
            }
            return new Vector3(p.x, h + lift, p.z);
        }

        public void Clear()
        {
            var t = transform.Find(RootName);
            if (t != null)
            {
                if (Application.isPlaying) Destroy(t.gameObject);
                else DestroyImmediate(t.gameObject);
            }
            records?.Clear();
        }

        // ---------------- 各类型占位实体 ----------------
        void CreateEntity(Transform parent, LinkKind kind, Vector3 a, Vector3 b, ConnectionRecord rec)
        {
            switch (kind)
            {
                case LinkKind.Vine: CreateVine(parent, a, b, rec); break;
                case LinkKind.Bridge: CreateBridge(parent, a, b, rec); break;
                case LinkKind.Ledge: CreateLedge(parent, a, b, rec); break;
                case LinkKind.Cave: CreateCave(parent, a, b, rec); break;
            }
        }

        /// <summary>藤蔓：用一串小圆柱拼出悬链曲线（中央下垂，像 PEAK 的藤蔓）。</summary>
        void CreateVine(Transform parent, Vector3 a, Vector3 b, ConnectionRecord rec)
        {
            float sag = Mathf.Max(0f, vineSagFrac) * rec.length;
            rec.sag = sag;
            var go = new GameObject($"Vine_{rec.seed}");
            go.transform.SetParent(parent, false);

            // 用 LineRenderer 画悬链：y(t) = lerp + sag*(0.5-|t-0.5|)*2 的负偏移
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.startWidth = 0.18f; lr.endWidth = 0.18f;
            lr.material = MakeUnlit(new Color(0.2f, 0.55f, 0.25f, 1f));
            int n = vineSegments + 1;
            lr.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Vector3 lin = Vector3.Lerp(a, b, t);
                float dropT = 4f * t * (1f - t); // 0→1→0 拱形，再翻负就是下垂
                lin.y -= dropT * sag;
                // 净空保护：下垂点不得低于山面（否则藤蔓扎进山里）
                float ground = GroundAt(lin.x, lin.z) + 0.5f;
                if (lin.y < ground) lin.y = ground;
                lr.SetPosition(i, lin);
            }
            // 两端各加一个锚点小球，提示挂载位置
            MakeAnchor(go.transform, a, new Color(0.3f, 0.7f, 0.3f));
            MakeAnchor(go.transform, b, new Color(0.3f, 0.7f, 0.3f));
        }

        /// <summary>桥：跨在两端之间的薄板，可微拱。</summary>
        void CreateBridge(Transform parent, Vector3 a, Vector3 b, ConnectionRecord rec)
        {
            rec.width = bridgeWidth;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Bridge_{rec.seed}";
            go.transform.SetParent(parent, false);
            // 把桥微微抬到中点+拱高
            Vector3 mid = (a + b) * 0.5f + Vector3.up * bridgeArch;
            Vector3 dir = b - a;
            go.transform.position = mid;
            go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            go.transform.localScale = new Vector3(bridgeWidth, 0.4f, rec.length);
            ApplyMat(go, new Color(0.65f, 0.45f, 0.28f));
            DestroyCollider(go);
        }

        /// <summary>岩架：沿山面的窄长道（细长方体）。</summary>
        void CreateLedge(Transform parent, Vector3 a, Vector3 b, ConnectionRecord rec)
        {
            rec.width = ledgeWidth;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Ledge_{rec.seed}";
            go.transform.SetParent(parent, false);
            Vector3 mid = (a + b) * 0.5f;
            Vector3 dir = b - a;
            go.transform.position = mid + Vector3.up * 0.3f;
            go.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            go.transform.localScale = new Vector3(ledgeWidth, 0.5f, rec.length);
            ApplyMat(go, new Color(0.78f, 0.7f, 0.5f));
            DestroyCollider(go);
        }

        /// <summary>洞穴：两端各一个洞口标记 + 中间虚线连接（实体由洞穴层做）。</summary>
        void CreateCave(Transform parent, Vector3 a, Vector3 b, ConnectionRecord rec)
        {
            var go = new GameObject($"Cave_{rec.seed}");
            go.transform.SetParent(parent, false);
            MakeCaveMouth(go.transform, a);
            MakeCaveMouth(go.transform, b);

            // 中间画一条虚拟连接线（提示这两个洞口相通，实际通道由第5层做）
            var line = new GameObject("InnerLink").AddComponent<LineRenderer>();
            line.transform.SetParent(go.transform, false);
            line.useWorldSpace = true;
            line.startWidth = 0.1f; line.endWidth = 0.1f;
            line.material = MakeUnlit(new Color(0.5f, 0.4f, 0.7f, 0.7f));
            line.positionCount = 2;
            line.SetPosition(0, a); line.SetPosition(1, b);
        }

        // ---------------- 小工具 ----------------
        void MakeAnchor(Transform parent, Vector3 pos, Color c)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "Anchor";
            s.transform.SetParent(parent, false);
            s.transform.position = pos;
            s.transform.localScale = Vector3.one * 0.6f;
            ApplyMat(s, c);
            DestroyCollider(s);
        }

        void MakeCaveMouth(Transform parent, Vector3 pos)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "CaveMouth";
            s.transform.SetParent(parent, false);
            s.transform.position = pos;
            s.transform.localScale = Vector3.one * caveMouthRadius * 2f;
            ApplyMat(s, new Color(0.15f, 0.15f, 0.18f));
            DestroyCollider(s);
        }

        void ApplyMat(GameObject go, Color c)
        {
            var mr = go.GetComponent<Renderer>();
            if (mr == null) return;
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            c.a = entityAlpha;
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
            mr.sharedMaterial = m;
        }

        Material MakeUnlit(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
            return m;
        }

        void DestroyCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
        }
    }
}
