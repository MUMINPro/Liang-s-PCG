using UnityEditor;
using UnityEngine;

namespace MapGen.Editor
{
    [CustomEditor(typeof(ClimbRouteGenerator))]
    public class ClimbRouteGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var g = (ClimbRouteGenerator)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成路网", GUILayout.Height(30)))
            {
                g.Generate();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("随机种子", GUILayout.Height(30), GUILayout.Width(90)))
            {
                g.seed = Random.Range(0, int.MaxValue);
                g.Generate();
                SceneView.RepaintAll();
                EditorUtility.SetDirty(g);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "第 3 层 · 攀爬路网（地形驱动寻路）\n" +
                "彩色实线 = 主路：在可攀地形上从山脚找最省力的登顶路径。\n" +
                "粗虚线 = 必经辅助：路径被崖/谷挡住、必须靠藤蔓/桥翻越处（标'崖/谷'）。\n" +
                "细虚线 = 可选捆绑：峰间藤蔓 / 相邻路线互通（非必经）。\n" +
                "● 营火（按累积体力放）  ◆ 交叉口  ▲ 峰顶\n" +
                "建议先做完第1、2层再生成（路线会读地貌坡度与屏障）。",
                MessageType.Info);
        }

        void OnSceneGUI()
        {
            var g = (ClimbRouteGenerator)target;
            var net = g.network;
            if (net == null) return;

            // 边
            foreach (var e in net.edges)
            {
                Vector3 a = net.GetNode(e.from).pos;
                Vector3 b = net.GetNode(e.to).pos;
                if (e.isLink)
                {
                    Handles.color = LinkColor(e.linkKind);
                    bool aid = e.role == EdgeRole.Aid;
                    // 必经辅助画粗虚线 + 屏障原因；可选捆绑画细虚线
                    Handles.DrawDottedLine(a + Vector3.up * 1.5f, b + Vector3.up * 1.5f, aid ? 2f : 6f);
                    if (aid)
                        Handles.DrawDottedLine(a + Vector3.up * 1.7f, b + Vector3.up * 1.7f, 2f); // 加粗
                    Vector3 mid = (a + b) * 0.5f + Vector3.up * 3f;
                    string label = LinkCN(e.linkKind) + (aid ? $"·{BarrierCN(e.barrier)}" : "");
                    DrawMini(mid, label, LinkColor(e.linkKind));
                }
                else
                {
                    var route = RouteOf(net, e.from);
                    Handles.color = route != null ? route.color : Color.white;
                    Handles.DrawAAPolyLine(5f, a + Vector3.up * 1f, b + Vector3.up * 1f);
                }
            }

            // 节点
            foreach (var n in net.nodes)
            {
                switch (n.kind)
                {
                    case RouteNodeKind.Trailhead:
                        Handles.color = new Color(0.4f, 0.9f, 0.5f);
                        Handles.SphereHandleCap(0, n.pos, Quaternion.identity, 6f, EventType.Repaint);
                        DrawMini(n.pos + Vector3.up * 6f, "登山口", new Color(0.4f,0.9f,0.5f));
                        break;
                    case RouteNodeKind.Campfire:
                        Handles.color = new Color(1f, 0.55f, 0.15f);
                        Handles.SphereHandleCap(0, n.pos, Quaternion.identity, 6f, EventType.Repaint);
                        DrawMini(n.pos + Vector3.up * 6f, "营火", new Color(1f,0.7f,0.3f));
                        break;
                    case RouteNodeKind.Junction:
                        Handles.color = new Color(0.95f, 0.85f, 0.3f);
                        Handles.CubeHandleCap(0, n.pos, Quaternion.Euler(45,45,0), 5f, EventType.Repaint);
                        break;
                    case RouteNodeKind.Summit:
                        Handles.color = new Color(1f, 0.25f, 0.25f);
                        Handles.ConeHandleCap(0, n.pos + Vector3.up * 4f,
                            Quaternion.LookRotation(Vector3.up), 8f, EventType.Repaint);
                        DrawMini(n.pos + Vector3.up * 10f, "峰顶", new Color(1f,0.4f,0.4f));
                        break;
                    default:
                        Handles.color = new Color(0.85f, 0.85f, 0.85f);
                        Handles.SphereHandleCap(0, n.pos, Quaternion.identity, 2.5f, EventType.Repaint);
                        break;
                }
            }
        }

        static ClimbRoute RouteOf(RouteNetwork net, int nodeId)
        {
            int ri = net.GetNode(nodeId).routeIndex;
            foreach (var r in net.routes) if (r.index == ri) return r;
            return null;
        }

        static void DrawMini(Vector3 pos, string text, Color c)
        {
            var style = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold };
            style.normal.textColor = c;
            Handles.Label(pos, text, style);
        }

        static Color LinkColor(LinkKind k)
        {
            switch (k)
            {
                case LinkKind.Vine: return new Color(0.3f, 0.85f, 0.4f);
                case LinkKind.Ledge: return new Color(0.8f, 0.7f, 0.5f);
                case LinkKind.Cave: return new Color(0.5f, 0.4f, 0.7f);
                case LinkKind.Bridge: return new Color(0.7f, 0.5f, 0.3f);
                default: return Color.gray;
            }
        }

        static string LinkCN(LinkKind k)
        {
            switch (k)
            {
                case LinkKind.Vine: return "藤蔓";
                case LinkKind.Ledge: return "岩架";
                case LinkKind.Cave: return "洞穴";
                case LinkKind.Bridge: return "桥";
                default: return "";
            }
        }

        static string BarrierCN(BarrierKind b)
        {
            switch (b)
            {
                case BarrierKind.Cliff: return "崖";
                case BarrierKind.Canyon: return "谷";
                case BarrierKind.CavePass: return "洞";
                case BarrierKind.PeakGap: return "峰";
                default: return "";
            }
        }
    }
}
