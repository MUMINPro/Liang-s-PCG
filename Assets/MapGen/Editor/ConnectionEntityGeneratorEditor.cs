using UnityEditor;
using UnityEngine;

namespace MapGen.Editor
{
    [CustomEditor(typeof(ConnectionEntityGenerator))]
    public class ConnectionEntityGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var g = (ConnectionEntityGenerator)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成连接实体", GUILayout.Height(30)))
            {
                g.Generate();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("清除", GUILayout.Height(30), GUILayout.Width(80)))
            {
                g.Clear();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "第 4 层 · 连接实体（参考 PEAK）\n" +
                "为第 3 层每条横向连接生成占位实体：\n" +
                "藤蔓=悬垂的绿色曲线  桥=棕色平板\n" +
                "岩架=米色窄道  洞穴=两端黑色洞口+紫线\n" +
                "都是占位指引，后续用真实模型替换。",
                MessageType.Info);
        }

        void OnSceneGUI()
        {
            var g = (ConnectionEntityGenerator)target;
            if (!g.showLabels || g.records == null) return;
            var style = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold };
            foreach (var r in g.records)
            {
                Vector3 mid = (r.from + r.to) * 0.5f + Vector3.up * 2f;
                Color c; string text;
                switch (r.kind)
                {
                    case LinkKind.Vine: c = new Color(0.3f,0.85f,0.4f); text = $"藤蔓 {r.length:F0}m"; break;
                    case LinkKind.Bridge: c = new Color(0.8f,0.55f,0.3f); text = $"桥 {r.length:F0}m"; break;
                    case LinkKind.Ledge: c = new Color(0.85f,0.75f,0.5f); text = $"岩架 {r.length:F0}m"; break;
                    case LinkKind.Cave: c = new Color(0.6f,0.5f,0.85f); text = $"洞穴 {r.length:F0}m"; break;
                    default: c = Color.white; text = ""; break;
                }
                style.normal.textColor = c;
                Handles.Label(mid, text, style);
            }
        }
    }
}
