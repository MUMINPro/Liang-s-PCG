using UnityEditor;
using UnityEngine;

namespace MapGen.Editor
{
    [CustomEditor(typeof(TestPlatformBuilder))]
    public class TestPlatformBuilderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var b = (TestPlatformBuilder)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            // 自动找同物体上的投射器，省得手动拖
            if (b.projector == null)
                b.projector = b.GetComponent<BlueprintProjector>();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成踏板", GUILayout.Height(30)))
                b.Build();
            if (GUILayout.Button("清除踏板", GUILayout.Height(30), GUILayout.Width(100)))
                b.Clear();
            EditorGUILayout.EndHorizontal();

            if (b.projector == null)
                EditorGUILayout.HelpBox("需要同物体上有 BlueprintProjector，并先点过「投射蓝图」。",
                    MessageType.Warning);
            else
                EditorGUILayout.HelpBox("先在 BlueprintProjector 点「投射蓝图」，再点这里「生成踏板」。\n" +
                    "跳跃段不建斜坡，需要玩家自己跳过去。", MessageType.Info);
        }
    }
}
