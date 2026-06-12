using UnityEditor;
using UnityEngine;

// ============================================================
// 蓝图投射器 - 编辑器 v0.1（放在 Editor 文件夹内）
// 负责：Inspector 的操作按钮 + Scene 视图里的占位标记/连线/标注绘制。
// ============================================================

namespace MapGen.Editor
{
    [CustomEditor(typeof(BlueprintProjector))]
    public class BlueprintProjectorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var bp = (BlueprintProjector)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("投射蓝图", GUILayout.Height(30)))
            {
                bp.Project();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("随机种子", GUILayout.Height(30), GUILayout.Width(90)))
            {
                bp.seed = Random.Range(0, int.MaxValue);
                bp.Project();
                SceneView.RepaintAll();
                EditorUtility.SetDirty(bp);
            }
            if (GUILayout.Button("清除", GUILayout.Height(30), GUILayout.Width(70)))
            {
                bp.Clear();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            if (bp.graph != null)
            {
                EditorGUILayout.HelpBox(
                    $"已投射：{bp.graph.nodes.Count} 节点 / {bp.graph.edges.Count} 边 / " +
                    $"{bp.graph.SegmentCount} 营火段\n" +
                    "切到 Scene 视图，照着占位标记手工搭建地形。",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("点「投射蓝图」在场景里生成参照标记。", MessageType.None);
            }
        }

        // ---------------- Scene 视图绘制 ----------------
        void OnSceneGUI()
        {
            var bp = (BlueprintProjector)target;
            if (bp.graph == null) return;
            var g = bp.graph;
            var cfg = bp.config;
            float maxSt = cfg != null ? cfg.player.maxStamina : 100f;

            // ---- 边（连线）----
            if (bp.showEdges)
            {
                foreach (var e in g.edges)
                {
                    if (bp.mainPathOnly && e.role != EdgeRole.MainPath) continue;
                    Vector3 a = bp.ToWorld(g.GetNode(e.from).pos);
                    Vector3 b = bp.ToWorld(g.GetNode(e.to).pos);

                    Handles.color = EdgeColor(e, maxSt);
                    float w = e.role == EdgeRole.MainPath ? 4f : 2f;
                    Handles.DrawAAPolyLine(w, a, b);

                    // 边中点的规格标注
                    if (bp.showLabels && e.type != EdgeType.Walk)
                    {
                        Vector3 mid = (a + b) * 0.5f;
                        float r = e.cost / maxSt;
                        var style = new GUIStyle();
                        style.normal.textColor = EdgeColor(e, maxSt);
                        style.fontSize = 10;
                        Handles.Label(mid + Vector3.up * 0.5f,
                            $"{TypeCN(e.type)} {e.climbLength:F0}m {r:F2}R", style);
                    }
                }
            }

            // ---- 节点（标记）----
            if (bp.showNodes)
            {
                foreach (var n in g.nodes)
                {
                    if (bp.mainPathOnly && !g.mainPath.Contains(n.id)
                        && n.kind != NodeKind.Loot) continue;
                    Vector3 p = bp.ToWorld(n.pos);
                    float s = bp.markerScale;

                    switch (n.kind)
                    {
                        case NodeKind.Start:
                            Handles.color = new Color(0.4f, 0.9f, 0.5f);
                            Handles.SphereHandleCap(0, p, Quaternion.identity, 1.6f * s, EventType.Repaint);
                            LabelIf(bp, p, "起点", new Color(0.4f, 0.9f, 0.5f));
                            break;
                        case NodeKind.Campfire:
                            Handles.color = new Color(1f, 0.55f, 0.15f);
                            Handles.SphereHandleCap(0, p, Quaternion.identity, 1.8f * s, EventType.Repaint);
                            DrawRing(p, 2.5f * s, new Color(1f, 0.55f, 0.15f));
                            LabelIf(bp, p, "★营火", new Color(1f, 0.7f, 0.3f));
                            break;
                        case NodeKind.Summit:
                            Handles.color = new Color(1f, 0.25f, 0.25f);
                            Handles.ConeHandleCap(0, p + Vector3.up * 1.5f * s,
                                Quaternion.LookRotation(Vector3.up), 2f * s, EventType.Repaint);
                            LabelIf(bp, p, "▲峰顶", new Color(1f, 0.4f, 0.4f));
                            break;
                        case NodeKind.Loot:
                            Handles.color = new Color(0.95f, 0.85f, 0.3f);
                            Handles.CubeHandleCap(0, p, Quaternion.identity, 1.4f * s, EventType.Repaint);
                            LabelIf(bp, p, "宝箱", new Color(0.95f, 0.85f, 0.3f));
                            break;
                        default: // Rest
                            Handles.color = new Color(0.8f, 0.8f, 0.8f);
                            Handles.SphereHandleCap(0, p, Quaternion.identity, 0.8f * s, EventType.Repaint);
                            break;
                    }
                }
            }
        }

        static void LabelIf(BlueprintProjector bp, Vector3 p, string text, Color c)
        {
            if (!bp.showLabels) return;
            var style = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold };
            style.normal.textColor = c;
            Handles.Label(p + Vector3.up * 2.6f * bp.markerScale, text, style);
        }

        static void DrawRing(Vector3 center, float radius, Color c)
        {
            Handles.color = c;
            Handles.DrawWireDisc(center, Vector3.forward, radius);
        }

        static Color EdgeColor(ClimbEdge e, float maxSt)
        {
            if (e.type == EdgeType.Walk) return new Color(0.6f, 0.6f, 0.6f);
            switch (e.role)
            {
                case EdgeRole.MainPath:
                    float frac = Mathf.Clamp01(e.cost / maxSt);
                    return Color.Lerp(new Color(0.35f, 0.85f, 0.4f),
                                      new Color(0.95f, 0.3f, 0.25f), frac);
                case EdgeRole.Shortcut: return new Color(0.85f, 0.45f, 0.95f);
                case EdgeRole.SafeDetour: return new Color(0.4f, 0.8f, 0.95f);
                case EdgeRole.LootSpur: return new Color(0.95f, 0.85f, 0.3f);
                default: return Color.white;
            }
        }

        static string TypeCN(EdgeType t)
        {
            switch (t)
            {
                case EdgeType.Walk: return "行走";
                case EdgeType.Climb: return "岩壁";
                case EdgeType.Overhang: return "反斜面";
                case EdgeType.JumpGap: return "跳跃";
                case EdgeType.Vine: return "藤蔓";
                case EdgeType.Cave: return "洞穴";
                case EdgeType.PillarChain: return "岩柱链";
                default: return t.ToString();
            }
        }
    }
}
