using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MapGen.Editor
{
    [CustomEditor(typeof(FeatureModelPlacer))]
    public class FeatureModelPlacerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var p = (FeatureModelPlacer)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("摆放模型", GUILayout.Height(30)))
                p.Place();
            if (GUILayout.Button("清除", GUILayout.Height(30), GUILayout.Width(80)))
                p.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            if (GUILayout.Button("为所有地貌类型建空模型库条目"))
            {
                Undo.RecordObject(p, "Init Model Library");
                p.modelLibrary = new List<FeatureModelEntry>();
                foreach (TerrainFeatureType t in Enum.GetValues(typeof(TerrainFeatureType)))
                    p.modelLibrary.Add(new FeatureModelEntry { type = t });
                EditorUtility.SetDirty(p);
            }

            EditorGUILayout.HelpBox(
                "把你的模型(商城资源/精细模型)拖进对应地貌类型的 prefabs 列表。\n" +
                "每类可放多个，摆放时随机选。没配模型的类型会保留占位块作提示。\n" +
                "模型会按占位块尺寸自动缩放、按朝向旋转。",
                MessageType.Info);
        }
    }
}
