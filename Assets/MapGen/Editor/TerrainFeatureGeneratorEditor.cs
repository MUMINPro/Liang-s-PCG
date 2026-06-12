using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MapGen.Editor
{
    [CustomEditor(typeof(TerrainFeatureGenerator))]
    public class TerrainFeatureGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var g = (TerrainFeatureGenerator)target;
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成地貌", GUILayout.Height(30)))
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

            EditorGUILayout.Space(4);
            if (GUILayout.Button("填入默认分层权重（山脚缓→峰顶险）"))
            {
                Undo.RecordObject(g, "Fill Default Bands");
                FillDefaultBands(g);
                EditorUtility.SetDirty(g);
            }

            EditorGUILayout.HelpBox(
                "第 2 层 · 深层修法（直接改山体网格）\n" +
                "十种地貌真实叠进山面：石峰凸起、崖壁竖起、台地削平、\n" +
                "岩脊起棱、峡谷下切、阶梯量化、石堆隆起、缓坡抹平、洞口/悬垂近似+标记。\n" +
                "先点「填入默认分层权重」，再「生成地貌」。\n" +
                "选中本物体时 Scene 飘字显示每个地貌名称。\n" +
                "真实精细模型后续用 FeatureModelPlacer 按记录摆放。",
                MessageType.Info);
        }

        static void FillDefaultBands(TerrainFeatureGenerator g)
        {
            g.bands = new List<TerrainBand>
            {
                new TerrainBand
                {
                    bandName = "山脚（缓·新手）",
                    altitudeMin = 0f, altitudeMax = 0.4f,
                    intensity = 0.6f, density = 1f,
                    featureWeights = new List<WeightedFeature>
                    {
                        new WeightedFeature{ type = TerrainFeatureType.GentleSlope, weight = 4f },
                        new WeightedFeature{ type = TerrainFeatureType.Plateau,     weight = 2f },
                        new WeightedFeature{ type = TerrainFeatureType.Boulders,    weight = 1.5f },
                        new WeightedFeature{ type = TerrainFeatureType.StepTerrace, weight = 1f },
                    },
                },
                new TerrainBand
                {
                    bandName = "山腰（中·混合）",
                    altitudeMin = 0.4f, altitudeMax = 0.75f,
                    intensity = 0.75f, density = 1.2f,
                    featureWeights = new List<WeightedFeature>
                    {
                        new WeightedFeature{ type = TerrainFeatureType.SpirePeak,     weight = 1.5f },
                        new WeightedFeature{ type = TerrainFeatureType.VerticalCliff, weight = 2f },
                        new WeightedFeature{ type = TerrainFeatureType.Plateau,       weight = 2f },
                        new WeightedFeature{ type = TerrainFeatureType.Canyon,        weight = 1f },
                        new WeightedFeature{ type = TerrainFeatureType.StepTerrace,   weight = 1.5f },
                    },
                },
                new TerrainBand
                {
                    bandName = "山顶（险·硬核）",
                    altitudeMin = 0.75f, altitudeMax = 1f,
                    intensity = 0.9f, density = 1.4f,
                    featureWeights = new List<WeightedFeature>
                    {
                        new WeightedFeature{ type = TerrainFeatureType.VerticalCliff, weight = 4f },
                        new WeightedFeature{ type = TerrainFeatureType.Overhang,      weight = 2f },
                        new WeightedFeature{ type = TerrainFeatureType.SharpRidge,    weight = 2f },
                        new WeightedFeature{ type = TerrainFeatureType.SpirePeak,     weight = 1f },
                    },
                },
            };
        }
        void OnSceneGUI()
        {
            var g = (TerrainFeatureGenerator)target;
            if (!g.showLabels || g.records == null) return;
            var style = new GUIStyle();
            style.fontSize = 12;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;
            style.alignment = TextAnchor.MiddleCenter;

            foreach (var fr in g.records)
            {
                Vector3 top = fr.worldPosition + Vector3.up * (fr.size.y + 6f);
                Handles.Label(top, fr.TypeNameCN, style);
            }
        }
    }
}
