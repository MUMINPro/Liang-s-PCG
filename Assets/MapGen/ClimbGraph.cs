using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 攀爬路线图 - 数据结构 v0.1
// 这是"抽象路线图"层的数据：只有拓扑和数值，没有几何模型。
// 后续的几何实现、验证器、可视化工具全都读这个结构。
// ============================================================

namespace MapGen
{
    public enum NodeKind
    {
        Start,    // 山脚出发点
        Rest,     // 普通休息点（可站立岩架）
        Campfire, // 营火（存档 + 回满体力）
        Loot,     // 奖励点（行李/物资）
        Summit,   // 峰顶
    }

    public enum EdgeRole
    {
        MainPath,   // 主路径
        Shortcut,   // 捷径（更难，跳过主路径节点）
        SafeDetour, // 安全迂回（更长，每段更轻松）
        LootSpur,   // 奖励支线（死胡同）
    }

    [Serializable]
    public class ClimbNode
    {
        public int id;
        public Vector3 pos;       // x,z = 水平面位置（螺旋）, y = 海拔（米）
        public NodeKind kind;
        public int biomeIndex;    // 所属群系（config.biomes 的下标）
    }

    [Serializable]
    public class ClimbEdge
    {
        public int from;
        public int to;
        public EdgeType type;     // 攀爬段类型（决定未来用哪类模块）
        public EdgeRole role;
        public float cost;        // 体力消耗（绝对值）
        public float climbLength; // 攀爬距离（米），几何实现时的长度依据
        public int jumpCount;     // JumpGap / PillarChain 的跳跃次数
        public int segmentIndex;  // 属于第几个营火段（验证器用）
    }

    /// <summary>一座山的完整抽象路线图。</summary>
    public class ClimbGraph
    {
        public int seed;
        public float totalAltitude;
        public List<ClimbNode> nodes = new List<ClimbNode>();
        public List<ClimbEdge> edges = new List<ClimbEdge>();

        /// <summary>主路径节点 id，从山脚到峰顶按顺序排列。</summary>
        public List<int> mainPath = new List<int>();

        /// <summary>营火/起点/峰顶在 mainPath 列表中的下标，用于切分营火段。</summary>
        public List<int> campfireIndicesInMainPath = new List<int>();

        public ClimbNode GetNode(int id) => nodes[id];

        public int SegmentCount => Mathf.Max(0, campfireIndicesInMainPath.Count - 1);
    }
}
