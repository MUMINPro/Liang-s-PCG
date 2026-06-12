using UnityEditor;
using UnityEngine;

namespace MapGen.Editor
{
    [CustomEditor(typeof(MacroTerrainGenerator))]
    public class MacroTerrainGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var g = (MacroTerrainGenerator)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成地形", GUILayout.Height(30)))
                g.Generate();
            if (GUILayout.Button("随机种子", GUILayout.Height(30), GUILayout.Width(90)))
            {
                g.seed = Random.Range(0, int.MaxValue);
                g.Generate();
                EditorUtility.SetDirty(g);
            }
            if (GUILayout.Button("清除", GUILayout.Height(30), GUILayout.Width(70)))
                g.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "第 1 层 · 宏观山体（毛坯）\n" +
                "「Falloff」曲线就是山的剪影，拖它捏山形。\n" +
                "满意后别删此组件——后续层（小峰群/路网/洞口）要靠它的高度函数贴地。",
                MessageType.Info);
        }
    }
}
