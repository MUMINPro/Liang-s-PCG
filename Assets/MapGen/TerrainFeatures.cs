using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 分层地图生成 · 第 2 层：地貌系统 - 核心定义 v0.1
// 十种纯几何地貌，按海拔分层用权重表混合。
// 同时埋好"环境标记"接口，供后续水体/材质层使用（不在本层实现）。
// ============================================================

namespace MapGen
{
    /// <summary>十种纯几何地貌类型（第 2 层只管形状，不管材质/水/植被）。</summary>
    public enum TerrainFeatureType
    {
        SpirePeak,     // 独立石峰（桂林/阿凡达式孤峰）
        SharpRidge,    // 尖锐岩脊（刀刃山脊线）
        Plateau,       // 台地高原（平坦桌状台面）
        Canyon,        // 峡谷裂隙（深切沟壑）
        VerticalCliff, // 垂直崖壁（大面积近垂直岩壁）
        Overhang,      // 悬垂反斜（向外突出的岩檐）
        StepTerrace,   // 阶梯岩台（层层错落的天然楼梯）
        Boulders,      // 巨石散布（散落的大圆石）
        Cave,          // 溶洞岩穴（山体内部空腔与洞口）
        GentleSlope,   // 缓坡草甸（平缓步行坡）
    }

    /// <summary>
    /// 环境标记 —— 第 2 层生成地貌时顺便打的"路标"，供后续层使用。
    /// 例如：峡谷底标记 WaterBasin（水体层可蓄水），
    ///       崖壁标记 CliffFace + 朝向/落差（水体层可挂瀑布）。
    /// 本层只负责打标记，不实现水/冰/植被本身。
    /// </summary>
    [Serializable]
    public class EnvironmentMarker
    {
        public enum Kind { WaterBasin, CliffFace, CaveMouth, SnowZone, FlatRest }
        public Kind kind;
        public Vector3 position;   // 世界坐标
        public Vector3 facing;     // 朝向（崖壁法线 / 洞口方向），无方向时为 up
        public float magnitude;    // 落差/半径/规模，含义随 kind 而定
        public TerrainFeatureType sourceFeature; // 由哪种地貌产生
    }

    /// <summary>单种地貌在权重表中的一项。</summary>
    [Serializable]
    public struct WeightedFeature
    {
        public TerrainFeatureType type;
        [Min(0f)] public float weight;
    }

    /// <summary>
    /// 一个海拔层带的地貌配置 —— 对应一个"群系"。
    /// 越往上的层带，权重越偏向难地貌（崖壁/悬垂），自然形成难度曲线。
    /// </summary>
    [Serializable]
    public class TerrainBand
    {
        public string bandName = "未命名层带";

        [Tooltip("本层带的海拔区间（占山体总高比例 0~1）")]
        [Range(0f, 1f)] public float altitudeMin = 0f;
        [Range(0f, 1f)] public float altitudeMax = 0.5f;

        [Tooltip("本层带内各地貌的出现权重")]
        public List<WeightedFeature> featureWeights = new List<WeightedFeature>();

        [Tooltip("本层带地貌叠加的总体强度（0=不改变第1层毛坯，1=完全主导）")]
        [Range(0f, 1f)] public float intensity = 0.7f;

        [Tooltip("本层带地貌特征的密度（每平方百米大约几个特征）")]
        [Range(0.1f, 5f)] public float density = 1f;
    }

    /// <summary>
    /// 占位指引块的数据载体 —— 挂在每个 proxy 物体上。
    /// 这是第 2 层（生成占位）和摆放脚本（放实物模型）之间的数据接口。
    /// 一条地貌记录（纯数据，不挂物体）。
    /// 深层修法下，地貌已经叠进山体网格，这条记录只用于：
    ///   - 显示名称标签（告诉你"这里是石峰"）
    ///   - 供后续摆放真实模型时定位
    /// </summary>
    [Serializable]
    public class FeatureRecord
    {
        public TerrainFeatureType type;   // 地貌类型 → 决定用模型库哪一类
        public Vector3 worldPosition;     // 地貌中心（贴地）
        public Vector3 facing = Vector3.up; // 朝向（崖壁法线/洞口方向等）
        public Vector3 size = Vector3.one; // 规模（米）：x宽 y高 z深
        public int bandIndex;             // 所属海拔层带
        public string bandName;
        public int featureSeed;           // 本特征的随机种子（摆放时选变体用）

        public string TypeNameCN => TypeToCN(type);

        public static string TypeToCN(TerrainFeatureType t)
        {
            switch (t)
            {
                case TerrainFeatureType.SpirePeak: return "石峰";
                case TerrainFeatureType.SharpRidge: return "岩脊";
                case TerrainFeatureType.Plateau: return "台地";
                case TerrainFeatureType.Canyon: return "峡谷";
                case TerrainFeatureType.VerticalCliff: return "崖壁";
                case TerrainFeatureType.Overhang: return "悬垂";
                case TerrainFeatureType.StepTerrace: return "阶梯";
                case TerrainFeatureType.Boulders: return "巨石";
                case TerrainFeatureType.Cave: return "洞口";
                case TerrainFeatureType.GentleSlope: return "缓坡";
                default: return t.ToString();
            }
        }
    }
}

