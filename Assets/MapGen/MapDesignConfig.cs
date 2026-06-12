using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 攀爬地图生成系统 - 配置定义 v0.1
// 与《关卡设计参数表 v0.1》一一对应。
// 使用方式：
//   1. 把本文件放进 Assets/MapGen/ 目录
//   2. 右键 Create > MapGen > Map Design Config 创建配置资产
//   3. 在 Inspector 里填参数（默认值已按参数表预填）
// ============================================================

namespace MapGen
{
    /// <summary>攀爬段类型 —— 对应参数表第 2 节，也是你未来模块库的分类标准。</summary>
    public enum EdgeType
    {
        Walk,        // 行走段（恢复体力）
        Climb,       // 普通岩壁
        Overhang,    // 反斜面/悬垂
        JumpGap,     // 跳跃间隙
        Vine,        // 藤蔓/绳索
        Cave,        // 洞穴穿越
        PillarChain, // 岩柱跳跃链
    }

    /// <summary>分支类型 —— 对应参数表第 4 节"分支三件套"。</summary>
    public enum BranchType
    {
        Shortcut,   // 捷径：更难但跳过主路径节点
        SafeDetour, // 安全迂回：更长但每段更轻松
        LootSpur,   // 奖励支线：死胡同，终点有物资
    }

    /// <summary>玩家能力模型 —— 一切关卡设计的"尺子"。</summary>
    [Serializable]
    public class PlayerModel
    {
        [Tooltip("体力上限（抽象单位）")]
        public float maxStamina = 100f;

        [Tooltip("普通攀爬消耗：体力/垂直米")]
        public float climbCostPerMeter = 5f;

        [Tooltip("攀爬中跳跃的一次性消耗")]
        public float jumpCost = 12f;

        [Tooltip("悬挂静止消耗：体力/秒")]
        public float hangCostPerSecond = 1f;

        [Tooltip("平地恢复速度：体力/秒")]
        public float recoveryPerSecond = 25f;

        /// <summary>R：满体力连续攀爬距离（米）。所有间距参数都以它为单位。</summary>
        public float R => maxStamina / climbCostPerMeter;
    }

    /// <summary>每种边类型的消耗规则。</summary>
    [Serializable]
    public class EdgeTypeRule
    {
        public EdgeType type;

        [Tooltip("垂直距离消耗倍率（Walk=0, Climb=1, Overhang=1.6...）")]
        public float costMultiplier = 1f;

        [Tooltip("固定消耗（JumpGap/PillarChain 每次跳跃用这个，按次数累加）")]
        public float flatCost = 0f;

        [Tooltip("是否对水平距离也计费（目前只有 Cave）")]
        public bool chargeHorizontal = false;
    }

    /// <summary>单个群系的全部设计参数 —— 对应参数表第 3 节的一行。</summary>
    [Serializable]
    public class BiomeConfig
    {
        public string biomeName = "未命名群系";

        [Tooltip("海拔区间（占山体总高的比例，0~1）")]
        [Range(0f, 1f)] public float altitudeMin = 0f;
        [Range(0f, 1f)] public float altitudeMax = 0.15f;

        [Header("难度核心：主路径休息间距（单位 = R 的倍数）")]
        [Tooltip("难度曲线的本质就是这两个数")]
        [Range(0f, 1f)] public float restSpacingMin = 0.3f;
        [Range(0f, 1f)] public float restSpacingMax = 0.45f;

        [Header("拓扑")]
        [Tooltip("每个营火段内的分支数量区间")]
        public Vector2Int branchCountRange = new Vector2Int(2, 3);

        [Tooltip("是否在本群系中部额外放一个营火（大群系建议开）")]
        public bool midBiomeCampfire = false;

        [Header("内容")]
        [Tooltip("本群系允许出现的边类型及其抽取权重")]
        public List<WeightedEdgeType> edgeTypePool = new List<WeightedEdgeType>();

        [Tooltip("标志性威胁的描述（原型期仅作标记，不实现）")]
        public string signatureThreat = "";
    }

    [Serializable]
    public struct WeightedEdgeType
    {
        public EdgeType type;
        [Min(0f)] public float weight;
    }

    /// <summary>全局拓扑规则 —— 对应参数表第 4、5 节。</summary>
    [Serializable]
    public class TopologyRules
    {
        [Header("主路径硬约束")]
        [Tooltip("主路径单段消耗上限（R 的倍数）。保证零道具可登顶。")]
        [Range(0f, 1f)] public float mainPathMaxEdgeCost = 0.85f;

        [Header("总量约束（可随山体高度自动缩放）")]
        [Tooltip("勾选后，总消耗区间和段数区间按山体高度自动推算，" +
                 "下面两个手填区间将被忽略。改高度不必再手动同步这两个数。")]
        public bool autoScaleByAltitude = true;

        [Tooltip("自动缩放基准：每 100 米期望的总消耗（R 倍数）。" +
                 "默认 5.2 ≈ 完整版 450m 落在 18–28R 中段")]
        public float costPerHundredMeters = 5.2f;

        [Tooltip("自动缩放基准：每 100 米期望的攀爬段数")]
        public float edgesPerHundredMeters = 14f;

        [Tooltip("自动区间的容差（±比例），0.25 = 上下各放宽 25%")]
        [Range(0.05f, 0.6f)] public float autoTolerance = 0.25f;

        [Tooltip("【手填】主路径总消耗区间（R 倍数）。仅在不勾选自动缩放时生效")]
        public Vector2 totalCostRange = new Vector2(18f, 28f);

        [Tooltip("【手填】主路径攀爬段数区间。仅在不勾选自动缩放时生效")]
        public Vector2Int mainPathEdgeCountRange = new Vector2Int(30, 45);

        [Header("分支规则")]
        [Tooltip("捷径的单段消耗区间（R 的倍数）")]
        public Vector2 shortcutCostRange = new Vector2(0.8f, 0.95f);

        [Tooltip("安全迂回的单段消耗上限（R 的倍数）")]
        [Range(0f, 1f)] public float detourMaxEdgeCost = 0.4f;

        [Tooltip("奖励支线深度（节点数）区间")]
        public Vector2Int lootSpurDepthRange = new Vector2Int(1, 2);

        [Tooltip("奖励支线总数 = 营火段数 × 此系数（向下取整）")]
        public float lootSpurPerSegment = 1.5f;

        /// <summary>按山体高度返回期望的总消耗区间（R 倍数）。</summary>
        public Vector2 EffectiveCostRange(float totalAltitude)
        {
            if (!autoScaleByAltitude) return totalCostRange;
            float mid = costPerHundredMeters * totalAltitude / 100f;
            return new Vector2(mid * (1f - autoTolerance), mid * (1f + autoTolerance));
        }

        /// <summary>按山体高度返回期望的攀爬段数区间。</summary>
        public Vector2Int EffectiveEdgeCountRange(float totalAltitude)
        {
            if (!autoScaleByAltitude) return mainPathEdgeCountRange;
            float mid = edgesPerHundredMeters * totalAltitude / 100f;
            return new Vector2Int(
                Mathf.RoundToInt(mid * (1f - autoTolerance)),
                Mathf.RoundToInt(mid * (1f + autoTolerance)));
        }
    }

    /// <summary>螺旋形态参数 —— 控制山怎么盘旋上升。</summary>
    [Serializable]
    public class SpiralSettings
    {
        [Tooltip("整座山螺旋的圈数。越大盘得越多。2.5偏松，4~6有明显包裹感")]
        [Range(0.5f, 8f)] public float turns = 4f;

        [Tooltip("基准半径 = 总高度 × 此值。越大山越宽、盘旋越舒展")]
        [Range(0.05f, 0.6f)] public float radiusFrac = 0.28f;

        [Tooltip("半径起伏幅度（占基准半径比例）。让山忽胖忽瘦、不对称")]
        [Range(0f, 0.9f)] public float radiusNoiseAmp = 0.45f;

        [Tooltip("半径起伏频率。每座山约几次胖瘦变化")]
        [Range(0.5f, 5f)] public float radiusNoiseFreq = 1.8f;

        [Tooltip("角速度的不规则程度。让盘旋忽快忽慢")]
        [Range(0f, 1f)] public float angleNoiseAmp = 0.35f;

        [Tooltip("每个落点的局部抖动（占总高度比例）。破除完美曲线感")]
        [Range(0f, 0.2f)] public float localJitterFrac = 0.06f;

        [Tooltip("山顶收窄程度。1=不收窄(圆柱)，越小顶部越尖")]
        [Range(0.2f, 1f)] public float summitTaper = 0.55f;
    }

    /// <summary>
    /// 总配置资产。一座山的全部设计参数装在一个 .asset 文件里，
    /// 换一个资产 = 换一套完全不同的山。
    /// </summary>
    [CreateAssetMenu(fileName = "MapDesignConfig", menuName = "MapGen/Map Design Config")]
    public class MapDesignConfig : ScriptableObject
    {
        [Header("玩家能力模型")]
        public PlayerModel player = new PlayerModel();

        [Header("山体宏观参数")]
        [Tooltip("山体总爬升（米）")]
        public float totalAltitude = 450f;

        [Header("螺旋形态（可在 Inspector 直接调，改完点投射蓝图看效果）")]
        public SpiralSettings spiral = new SpiralSettings();

        [Header("边类型规则表")]
        public List<EdgeTypeRule> edgeTypeRules = new List<EdgeTypeRule>();

        [Header("群系（按海拔从低到高排列）")]
        public List<BiomeConfig> biomes = new List<BiomeConfig>();

        [Header("全局拓扑规则")]
        public TopologyRules topology = new TopologyRules();

        // ---------- 工具方法（生成器和验证器都会用到） ----------

        /// <summary>R：满体力连续攀爬距离（米）。</summary>
        public float R => player.R;

        /// <summary>按海拔比例（0~1）查询所属群系。</summary>
        public BiomeConfig GetBiomeAtAltitude(float normalizedAltitude)
        {
            foreach (var b in biomes)
                if (normalizedAltitude >= b.altitudeMin && normalizedAltitude < b.altitudeMax)
                    return b;
            return biomes.Count > 0 ? biomes[biomes.Count - 1] : null;
        }

        /// <summary>查询某边类型的消耗规则。</summary>
        public EdgeTypeRule GetEdgeRule(EdgeType type)
        {
            foreach (var r in edgeTypeRules)
                if (r.type == type) return r;
            return new EdgeTypeRule { type = type, costMultiplier = 1f };
        }

        /// <summary>
        /// 计算一段攀爬的体力消耗。
        /// vertical/horizontal 单位为米，jumpCount 为该段内的跳跃次数。
        /// </summary>
        public float ComputeEdgeCost(EdgeType type, float vertical, float horizontal, int jumpCount = 0)
        {
            var rule = GetEdgeRule(type);
            float dist = vertical + (rule.chargeHorizontal ? horizontal : 0f);
            return dist * rule.costMultiplier * player.climbCostPerMeter
                 + rule.flatCost * jumpCount
                 + (type == EdgeType.JumpGap ? player.jumpCost : 0f);
        }

        /// <summary>在编辑器里改参数时做基本合法性检查。</summary>
        private void OnValidate()
        {
            for (int i = 0; i < biomes.Count; i++)
            {
                var b = biomes[i];
                if (b.altitudeMax < b.altitudeMin) b.altitudeMax = b.altitudeMin;
                if (b.restSpacingMax < b.restSpacingMin) b.restSpacingMax = b.restSpacingMin;
                // 提醒：休息间距超过主路径单段上限的群系，主路径将被迫顶着上限生成
                if (b.restSpacingMax > topology.mainPathMaxEdgeCost)
                    Debug.LogWarning($"[MapGen] 群系「{b.biomeName}」的休息间距上限 " +
                        $"({b.restSpacingMax:F2}R) 超过主路径单段上限 " +
                        $"({topology.mainPathMaxEdgeCost:F2}R)，生成时会被钳制。", this);
            }
        }
    }
}
