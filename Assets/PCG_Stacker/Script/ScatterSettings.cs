using System;
using UnityEngine;

namespace PCG
{
    /// <summary>
    /// 撒点参数集合。集中放所有可调参数，方便在 Inspector 里调手感。
    /// [Serializable]，可直接作为 LevelGenerator 的字段显示。
    ///
    /// 调参提示：layerHeight / maxJumpDistance 必须 ≤ 玩家实际跳跃能力，
    /// 否则主路也跳不上去。建议先在游戏里测出玩家最大跳跃距离再设。
    /// </summary>
    [Serializable]
    public class ScatterSettings
    {
        [Header("层结构")]
        [Tooltip("总共生成多少层")]
        [Min(1)]
        public int layerCount = 30;

        [Tooltip("相邻两层的垂直高度差（米）。必须 ≤ 玩家垂直跳跃能力")]
        [Min(0.1f)]
        public float layerHeight = 3f;

        [Header("主路（保证链）")]
        [Tooltip("主路点距上一主路点的最小水平距离（米）")]
        [Min(0f)]
        public float minJumpDistance = 1f;

        [Tooltip("主路点距上一主路点的最大水平距离（米）。必须 ≤ 玩家水平跳跃能力")]
        [Min(0.1f)]
        public float maxJumpDistance = 4f;

        [Header("分支自由点（围绕主路就近撒）")]
        [Tooltip("分支点围绕当前层主路点撒布的半径（米）。建议略大于 maxJumpDistance，让分支点也大致可达")]
        [Min(0.1f)]
        public float branchRadius = 5f;

        [Tooltip("每层分支点的最小数量")]
        [Min(0)]
        public int minBranchPerLayer = 1;

        [Tooltip("每层分支点的最大数量")]
        [Min(0)]
        public int maxBranchPerLayer = 3;

        [Header("防重叠")]
        [Tooltip("任意两个落脚点之间的最小间距（米）。小于这个距离的点会被重撒或放弃")]
        [Min(0f)]
        public float minSpacing = 1.5f;
    }
}
