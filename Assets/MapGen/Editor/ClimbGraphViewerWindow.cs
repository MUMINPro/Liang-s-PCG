using UnityEditor;
using UnityEngine;

// ============================================================
// 路线图可视化窗口 v0.1（必须放在 Editor 文件夹内）
// 打开方式：菜单 Tools > MapGen > 路线图查看器
//
// 操作：
//   鼠标滚轮 = 缩放    按住中键/右键拖动 = 平移
//   悬停节点 = 查看信息    F = 重置视图
// ============================================================

namespace MapGen.Editor
{
    public class ClimbGraphViewerWindow : EditorWindow
    {
        MapDesignConfig config;
        int seed = 12345;
        ClimbGraph graph;
        ValidationReport report;

        Vector2 pan;          // 世界坐标系中的视图中心
        float zoom = 2f;
        const float SidebarWidth = 290f;

        bool heatmapMode = false; // true = 所有边按难度热力配色（盖过角色配色）

        [MenuItem("Tools/MapGen/路线图查看器")]
        static void Open()
        {
            var w = GetWindow<ClimbGraphViewerWindow>("路线图查看器");
            w.minSize = new Vector2(800, 500);
        }

        void OnGUI()
        {
            DrawToolbar();
            Rect canvas = new Rect(0, EditorGUIUtility.singleLineHeight + 8,
                position.width - SidebarWidth, position.height - EditorGUIUtility.singleLineHeight - 8);

            HandleInput(canvas);
            DrawCanvas(canvas);
            DrawSidebar(new Rect(canvas.xMax, canvas.y, SidebarWidth, canvas.height));
        }

        // ---------------- 工具栏 ----------------
        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            config = (MapDesignConfig)EditorGUILayout.ObjectField(
                config, typeof(MapDesignConfig), false, GUILayout.Width(220));
            GUILayout.Label("种子", GUILayout.Width(30));
            int newSeed = EditorGUILayout.IntField(seed, GUILayout.Width(90));
            if (newSeed != seed) { seed = newSeed; Generate(); }
            if (GUILayout.Button("随机", EditorStyles.toolbarButton, GUILayout.Width(50)))
            { seed = Random.Range(0, int.MaxValue); Generate(); }
            if (GUILayout.Button("生成", EditorStyles.toolbarButton, GUILayout.Width(50)))
                Generate();
            if (GUILayout.Button("重置视图 (F)", EditorStyles.toolbarButton, GUILayout.Width(90)))
                FitView();
            bool newHeat = GUILayout.Toggle(heatmapMode, "难度热力",
                EditorStyles.toolbarButton, GUILayout.Width(70));
            if (newHeat != heatmapMode) { heatmapMode = newHeat; Repaint(); }
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(graph == null || config == null);
            if (GUILayout.Button("导出规格", EditorStyles.toolbarButton, GUILayout.Width(70)))
                GraphSpecExporter.Export(config, seed);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        void Generate()
        {
            if (config == null) return;
            graph = ClimbGraphGenerator.Generate(config, seed);
            report = ClimbGraphValidator.Validate(config, graph);
            FitView();
            Repaint();
        }

        void FitView()
        {
            if (graph == null || graph.nodes.Count == 0) return;
            Vector2 min = Flat(graph.nodes[0]), max = Flat(graph.nodes[0]);
            foreach (var n in graph.nodes)
            {
                min = Vector2.Min(min, Flat(n));
                max = Vector2.Max(max, Flat(n));
            }
            pan = (min + max) * 0.5f;
            Rect canvas = new Rect(0, 0, position.width - SidebarWidth, position.height - 30);
            float w = Mathf.Max(10f, max.x - min.x), h = Mathf.Max(10f, max.y - min.y);
            zoom = Mathf.Min(canvas.width / w, canvas.height / h) * 0.85f;
        }

        // ---------------- 交互 ----------------
        void HandleInput(Rect canvas)
        {
            Event e = Event.current;
            if (!canvas.Contains(e.mousePosition) && e.type != EventType.KeyDown) return;

            if (e.type == EventType.ScrollWheel)
            {
                zoom *= e.delta.y > 0 ? 0.9f : 1.1f;
                zoom = Mathf.Clamp(zoom, 0.2f, 50f);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2))
            {
                pan.x -= e.delta.x / zoom;
                pan.y += e.delta.y / zoom;
                e.Use(); Repaint();
            }
            else if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F)
            {
                FitView(); e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseMove) Repaint();
        }

        // 把 3D 节点投影到 2D 侧视：横轴=水平分量X，纵轴=海拔Y。
        // （螺旋的 Z 分量在这个 2D 窗口里被压扁，仅作骨架参考；
        //  立体形态请在场景的蓝图投射器里看。）
        static Vector2 Flat(ClimbNode n) => new Vector2(n.pos.x, n.pos.y);

        Vector2 W2S(Vector2 world, Rect canvas)
        {
            return new Vector2(
                canvas.center.x + (world.x - pan.x) * zoom,
                canvas.center.y - (world.y - pan.y) * zoom);
        }

        // ---------------- 绘制 ----------------
        static readonly Color[] BiomeBand = {
            new Color(0.95f, 0.85f, 0.55f, 0.10f), // 海岸·沙
            new Color(0.35f, 0.75f, 0.40f, 0.10f), // 丛林·绿
            new Color(0.60f, 0.70f, 0.85f, 0.10f), // 高山·灰蓝
            new Color(0.80f, 0.50f, 0.35f, 0.10f), // 台地·赭
            new Color(0.90f, 0.30f, 0.30f, 0.10f), // 峰顶·红
        };

        void DrawCanvas(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.13f, 0.13f, 0.14f));
            if (graph == null || config == null)
            {
                GUI.Label(new Rect(canvas.x + 20, canvas.y + 20, 400, 40),
                    "← 在工具栏选择一个 MapDesignConfig 资产，然后点「生成」", EditorStyles.boldLabel);
                return;
            }

            // 群系背景带
            for (int i = 0; i < config.biomes.Count; i++)
            {
                var b = config.biomes[i];
                float y0 = W2S(new Vector2(0, b.altitudeMin * config.totalAltitude), canvas).y;
                float y1 = W2S(new Vector2(0, b.altitudeMax * config.totalAltitude), canvas).y;
                Rect band = Rect.MinMaxRect(canvas.xMin, Mathf.Min(y0, y1), canvas.xMax, Mathf.Max(y0, y1));
                band = ClampRect(band, canvas);
                if (band.height > 0)
                {
                    EditorGUI.DrawRect(band, BiomeBand[i % BiomeBand.Length]);
                    GUI.Label(new Rect(canvas.xMin + 6, band.yMin + 2, 200, 18),
                        b.biomeName + "  " + (b.restSpacingMin) + "–" + (b.restSpacingMax) + "R",
                        EditorStyles.miniLabel);
                }
            }

            // 边
            float maxSt = config.player.maxStamina;
            foreach (var e in graph.edges)
            {
                Vector2 a = W2S(Flat(graph.GetNode(e.from)), canvas);
                Vector2 b = W2S(Flat(graph.GetNode(e.to)), canvas);
                if (!LineVisible(a, b, canvas)) continue;

                float frac = Mathf.Clamp01(e.cost / maxSt); // 占体力比例

                if (heatmapMode)
                {
                    // 热力模式：忽略角色，全部按难度上色（蓝→绿→黄→红）
                    Handles.color = HeatColor(frac);
                    float wHeat = e.role == EdgeRole.MainPath ? 4.5f : 2.5f;
                    if (e.type == EdgeType.Walk) { Handles.color = new Color(0.45f, 0.45f, 0.5f); wHeat = 2f; }
                    Handles.DrawAAPolyLine(wHeat, a, b);
                    continue;
                }

                Color costColor = Color.Lerp(new Color(0.3f, 0.85f, 0.4f),
                                             new Color(0.95f, 0.3f, 0.25f), frac);
                switch (e.role)
                {
                    case EdgeRole.MainPath:
                        Handles.color = e.type == EdgeType.Walk
                            ? new Color(0.6f, 0.6f, 0.6f) : costColor;
                        Handles.DrawAAPolyLine(e.type == EdgeType.Walk ? 2.5f : 4.5f, a, b);
                        break;
                    case EdgeRole.Shortcut:
                        Handles.color = new Color(0.85f, 0.45f, 0.95f); // 紫：高手路线
                        Handles.DrawAAPolyLine(2.5f, a, b);
                        break;
                    case EdgeRole.SafeDetour:
                        Handles.color = new Color(0.4f, 0.8f, 0.95f);   // 青：保险路线
                        Handles.DrawAAPolyLine(2f, a, b);
                        break;
                    case EdgeRole.LootSpur:
                        Handles.color = new Color(0.95f, 0.85f, 0.3f);  // 黄：奖励路线
                        Handles.DrawAAPolyLine(2f, a, b);
                        break;
                }
            }

            // 节点
            ClimbNode hover = null;
            foreach (var n in graph.nodes)
            {
                Vector2 p = W2S(Flat(n), canvas);
                if (!canvas.Contains(p)) continue;
                switch (n.kind)
                {
                    case NodeKind.Start:
                        DrawDisc(p, 7f, new Color(0.4f, 0.9f, 0.5f)); break;
                    case NodeKind.Campfire:
                        DrawDisc(p, 7f, new Color(1f, 0.55f, 0.15f)); break;
                    case NodeKind.Summit:
                        DrawDisc(p, 8f, new Color(1f, 0.25f, 0.25f)); break;
                    case NodeKind.Loot:
                        EditorGUI.DrawRect(new Rect(p.x - 4, p.y - 4, 8, 8),
                            new Color(0.95f, 0.85f, 0.3f)); break;
                    default:
                        DrawDisc(p, 3.5f, new Color(0.85f, 0.85f, 0.85f)); break;
                }
                if (Vector2.Distance(Event.current.mousePosition, p) < 10f) hover = n;
            }

            // 悬停信息
            if (hover != null)
            {
                string biomeName = hover.biomeIndex >= 0 && hover.biomeIndex < config.biomes.Count
                    ? config.biomes[hover.biomeIndex].biomeName : "?";
                string txt = "#" + hover.id + "  " + hover.kind +
                    "\n海拔 " + hover.pos.y.ToString("F1") + " m   群系: " + biomeName;
                Vector2 p = W2S(Flat(hover), canvas) + new Vector2(12, -10);
                Rect box = new Rect(p.x, p.y, 190, 34);
                EditorGUI.DrawRect(box, new Color(0f, 0f, 0f, 0.8f));
                GUI.Label(box, txt, EditorStyles.whiteMiniLabel);
            }
        }

        void DrawSidebar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.18f, 0.18f, 0.19f));
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, rect.height - 16));

            GUILayout.Label("图例", EditorStyles.boldLabel);
            LegendLine(new Color(0.3f, 0.85f, 0.4f), "主路径（绿→红 = 体力消耗）");
            LegendLine(new Color(0.85f, 0.45f, 0.95f), "捷径（难但快）");
            LegendLine(new Color(0.4f, 0.8f, 0.95f), "安全迂回");
            LegendLine(new Color(0.95f, 0.85f, 0.3f), "奖励支线 / 奖励点 ■");
            LegendLine(new Color(1f, 0.55f, 0.15f), "● 营火    ● 峰顶(红)  ● 起点(绿)");

            GUILayout.Space(8);
            if (graph != null && config != null)
            {
                GUILayout.Label("统计", EditorStyles.boldLabel);
                float totalCost = 0f;
                int climbs = 0;
                foreach (var e in graph.edges)
                    if (e.role == EdgeRole.MainPath)
                    { totalCost += e.cost; if (e.type != EdgeType.Walk) climbs++; }
                GUILayout.Label(
                    "节点 " + graph.nodes.Count + "   边 " + graph.edges.Count +
                    "\n主路径攀爬段 " + climbs +
                    "\n主路径总消耗 " + (totalCost / config.player.maxStamina).ToString("F1") + "R" +
                    "\n营火段 " + graph.SegmentCount,
                    EditorStyles.miniLabel);
            }

            GUILayout.Space(8);
            GUILayout.Label("验收报告", EditorStyles.boldLabel);
            if (report != null)
            {
                foreach (var item in report.items)
                {
                    var style = new GUIStyle(EditorStyles.miniLabel);
                    string mark;
                    if (item.advisory)
                    {
                        // 仅参考项：永远灰色，不报红，用 ○ 区分
                        style.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                        mark = item.pass ? "○ " : "○ ";
                    }
                    else
                    {
                        style.normal.textColor = item.pass
                            ? new Color(0.5f, 0.9f, 0.5f) : new Color(1f, 0.45f, 0.4f);
                        mark = item.pass ? "✓ " : "✗ ";
                    }
                    GUILayout.Label(mark + item.label, style);
                    GUILayout.Label("    " + item.detail, EditorStyles.miniLabel);
                }
                GUILayout.Space(4);
                var sum = new GUIStyle(EditorStyles.boldLabel);
                sum.normal.textColor = report.AllPass
                    ? new Color(0.5f, 0.9f, 0.5f) : new Color(1f, 0.45f, 0.4f);
                GUILayout.Label(report.AllPass ? "✓ 本图通过验收" : "✗ 本图不通过（换种子）", sum);
            }
            else GUILayout.Label("（生成后显示）", EditorStyles.miniLabel);

            GUILayout.EndArea();
        }

        // ---------------- 小工具 ----------------
        // 难度热力：0=蓝(轻松) 0.4=绿 0.7=黄 1=红(极限)
        static Color HeatColor(float t)
        {
            t = Mathf.Clamp01(t);
            Color blue = new Color(0.25f, 0.55f, 0.95f);
            Color green = new Color(0.35f, 0.85f, 0.4f);
            Color yellow = new Color(0.95f, 0.85f, 0.25f);
            Color red = new Color(0.95f, 0.3f, 0.25f);
            if (t < 0.4f) return Color.Lerp(blue, green, t / 0.4f);
            if (t < 0.7f) return Color.Lerp(green, yellow, (t - 0.4f) / 0.3f);
            return Color.Lerp(yellow, red, (t - 0.7f) / 0.3f);
        }

        static void DrawDisc(Vector2 p, float r, Color c)
        {
            Handles.color = c;
            Handles.DrawSolidDisc(new Vector3(p.x, p.y, 0), Vector3.forward, r);
        }

        static void LegendLine(Color c, string text)
        {
            Rect r = GUILayoutUtility.GetRect(1, 16);
            EditorGUI.DrawRect(new Rect(r.x, r.y + 5, 18, 4), c);
            GUI.Label(new Rect(r.x + 24, r.y, r.width - 24, 16), text, EditorStyles.miniLabel);
        }

        static Rect ClampRect(Rect r, Rect bounds)
        {
            float xMin = Mathf.Max(r.xMin, bounds.xMin);
            float yMin = Mathf.Max(r.yMin, bounds.yMin);
            float xMax = Mathf.Min(r.xMax, bounds.xMax);
            float yMax = Mathf.Min(r.yMax, bounds.yMax);
            return Rect.MinMaxRect(xMin, yMin, xMax, Mathf.Max(yMin, yMax));
        }

        static bool LineVisible(Vector2 a, Vector2 b, Rect r)
        {
            Rect box = Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return box.Overlaps(r);
        }
    }
}
