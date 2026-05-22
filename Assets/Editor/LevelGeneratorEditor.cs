using UnityEditor;
using UnityEngine;

namespace PCG
{
    /// <summary>
    /// LevelGenerator 的自定义编辑器：Inspector 里加两个按钮，不进运行模式就能预览。
    /// 重要：本文件必须放在名为 "Editor" 的文件夹下，否则打包报错。
    /// </summary>
    [CustomEditor(typeof(LevelGenerator))]
    public class LevelGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (LevelGenerator)target;

            EditorGUILayout.Space();

            if (GUILayout.Button("生成关卡"))
                generator.GenerateLevel();

            if (GUILayout.Button("清除关卡"))
                generator.ClearLevel();
        }
    }
}
