using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 分层地图生成 · 第 3 层：攀爬路网 - 数据结构 v0.1
// 几条主路从不同方位登顶 + 横向连接段让路线互通，形成网状。
// 所有点都贴在真实山体（第1层高度函数）上，优先穿合适地貌（第2层）。
//
// 与早期 ClimbGraph 的区别：那是凭空螺旋，这个贴地、串地貌、多路互通。
// ============================================================

namespace MapGen
{
    public enum RouteNodeKind
    {
        Trailhead,  // 登山口（山脚起点）
        Waypoint,   // 路径点（普通休息/落脚）
        Campfire,   // 营火（存档+回体力）
        Junction,   // 交叉口（连接段的接驳点）
        Summit,     // 峰顶（所有路线终点）
    }

    public enum RouteDifficulty { Easy, Medium, Hard }

    public enum LinkKind
    {
        Vine,    // 藤蔓（空中横向连接）
        Ledge,   // 岩架（沿山面横切）
        Cave,    // 洞穴（穿山体内部连接）
        Bridge,  // 桥梁（跨越峡谷连接）
    }

    /// <summary>
    /// 一条连接/辅助边"为什么存在"——它跨越的是什么屏障。
    /// 地形驱动寻路时，路径被屏障挡住、必须借助辅助才能过去，就记在这里。
    /// None = 不是屏障跨越（如峰间藤蔓那种可选捆绑路径）。
    /// </summary>
    public enum BarrierKind
    {
        None,        // 非屏障（可选捆绑/装饰连接）
        Cliff,       // 垂直崖壁：需藤蔓/岩架向上翻越
        Canyon,      // 峡谷裂隙：需桥横跨
        CavePass,    // 洞穴穿行：两洞口之间
        PeakGap,     // 峰间空隙：石峰/高点之间荡藤蔓（玩法爽点）
    }

    /// <summary>一条边在路网里扮演的角色。</summary>
    public enum EdgeRole
    {
        MainPath,    // 主路段（沿可攀地形走的路径）
        Aid,         // 必经辅助（路径过屏障的唯一手段，藤蔓/桥/洞穴）
        CrossLink,   // 可选捆绑（相邻路线/峰间互通，非必经）
    }

    [Serializable]
    public class RouteNode
    {
        public int id;
        public Vector3 pos;            // 世界坐标（已贴山面）
        public RouteNodeKind kind;
        public int routeIndex;         // 属于哪条主路（-1 = 连接段专属节点）
        public int bandIndex;          // 所在海拔层带
        public TerrainFeatureType nearbyFeature; // 附近主导地貌（供布模块参考）
        public bool hasNearbyFeature;
    }

    [Serializable]
    public class RouteEdge
    {
        public int from;
        public int to;
        public bool isLink;            // false=主路段, true=横向连接段（含辅助/捆绑）
        public LinkKind linkKind;      // 仅 isLink 时有意义：连接实体形态
        public EdgeRole role;          // 这条边的角色：主路/必经辅助/可选捆绑
        public BarrierKind barrier;    // 这条边跨越的屏障类型（None=非屏障）
        public RouteDifficulty difficulty;
        public float length;           // 3D 长度（米）
        public float effort;           // 攀爬代价（体力消耗估算，供放营火/难度用）
    }

    /// <summary>一条主登顶路线。</summary>
    [Serializable]
    public class ClimbRoute
    {
        public int index;
        public string name;
        public RouteDifficulty difficulty;
        public Color color;            // 可视化用
        public List<int> nodeIds = new List<int>(); // 从登山口到峰顶有序排列
    }

    /// <summary>整张攀爬路网。</summary>
    public class RouteNetwork
    {
        public int seed;
        public List<RouteNode> nodes = new List<RouteNode>();
        public List<RouteEdge> edges = new List<RouteEdge>();
        public List<ClimbRoute> routes = new List<ClimbRoute>();
        public int summitId = -1;

        public RouteNode GetNode(int id) => nodes[id];
    }
}
