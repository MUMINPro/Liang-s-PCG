using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// ============================================================
// 原型配置创建器 v0.1（必须放在 Editor 文件夹内）
// 菜单：Tools > MapGen > 创建原型配置（海岸+丛林）
//
// 按拍板结论预填：
//   - R = 20m（体力100，5/米）
//   - 原型期 2 个群系：海岸 + 丛林
//   - 山体总爬升 180m（完整版 450m 的约 40%，对应单局 30~50 分钟）
//   - 拓扑约束按 2 群系等比缩小（完整版数值见参数表）
// ============================================================

namespace MapGen.Editor
{
    public static class PrototypeConfigCreator
    {
        [MenuItem("Tools/MapGen/创建原型配置（海岸+丛林）")]
        static void Create()
        {
            var cfg = ScriptableObject.CreateInstance<MapDesignConfig>();

            // ---- 玩家模型：全部用类内默认值（R = 20m）----

            // ---- 山体 ----
            cfg.totalAltitude = 180f;

            // ---- 边类型规则表（参数表第 2 节）----
            cfg.edgeTypeRules = new List<EdgeTypeRule>
            {
                new EdgeTypeRule { type = EdgeType.Walk,        costMultiplier = 0f },
                new EdgeTypeRule { type = EdgeType.Climb,       costMultiplier = 1f },
                new EdgeTypeRule { type = EdgeType.Overhang,    costMultiplier = 1.6f },
                new EdgeTypeRule { type = EdgeType.JumpGap,     costMultiplier = 0f, flatCost = 12f },
                new EdgeTypeRule { type = EdgeType.Vine,        costMultiplier = 0.5f },
                new EdgeTypeRule { type = EdgeType.Cave,        costMultiplier = 1f, chargeHorizontal = true },
                new EdgeTypeRule { type = EdgeType.PillarChain, costMultiplier = 0f, flatCost = 12f },
            };

            // ---- 群系（参数表第 3 节，原型期取前两行）----
            cfg.biomes = new List<BiomeConfig>
            {
                new BiomeConfig
                {
                    biomeName = "海岸",
                    altitudeMin = 0f, altitudeMax = 0.4f,
                    restSpacingMin = 0.30f, restSpacingMax = 0.45f,
                    branchCountRange = new Vector2Int(2, 3),
                    midBiomeCampfire = false,
                    signatureThreat = "无（教学区）",
                    edgeTypePool = new List<WeightedEdgeType>
                    {
                        new WeightedEdgeType { type = EdgeType.Climb, weight = 4f },
                        new WeightedEdgeType { type = EdgeType.Vine,  weight = 1f },
                    },
                },
                new BiomeConfig
                {
                    biomeName = "丛林",
                    altitudeMin = 0.4f, altitudeMax = 1f,
                    restSpacingMin = 0.40f, restSpacingMax = 0.60f,
                    branchCountRange = new Vector2Int(2, 4),
                    midBiomeCampfire = true,
                    signatureThreat = "毒物、湿滑",
                    edgeTypePool = new List<WeightedEdgeType>
                    {
                        new WeightedEdgeType { type = EdgeType.Climb,    weight = 3f },
                        new WeightedEdgeType { type = EdgeType.Vine,     weight = 2f },
                        new WeightedEdgeType { type = EdgeType.Cave,     weight = 1f },
                        new WeightedEdgeType { type = EdgeType.Overhang, weight = 0.5f },
                    },
                },
            };

            // ---- 拓扑规则：按 180m 原型缩放 ----
            // 完整版（450m）：总消耗 18–28R，段数 30–45（见参数表）
            cfg.topology = new TopologyRules
            {
                mainPathMaxEdgeCost = 0.85f,
                totalCostRange = new Vector2(7f, 14f),
                mainPathEdgeCountRange = new Vector2Int(13, 28),
                shortcutCostRange = new Vector2(0.8f, 0.95f),
                detourMaxEdgeCost = 0.4f,
                lootSpurDepthRange = new Vector2Int(1, 2),
                lootSpurPerSegment = 1.5f,
            };

            // ---- 保存资产 ----
            if (!AssetDatabase.IsValidFolder("Assets/MapGen"))
                AssetDatabase.CreateFolder("Assets", "MapGen");
            string path = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/MapGen/PrototypeConfig.asset");
            AssetDatabase.CreateAsset(cfg, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(cfg);
            Selection.activeObject = cfg;
            Debug.Log("[MapGen] 原型配置已创建：" + path +
                "（R = " + cfg.R + "m，山体 " + cfg.totalAltitude + "m，2 群系）");
        }
    }
}
