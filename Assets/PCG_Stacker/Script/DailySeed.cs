using System;
using UnityEngine;

namespace PCG
{
    /// <summary>
    /// 日期种子（决策 D5）。根据当前 UTC 日期算出种子，提供可复现的随机源。
    ///
    /// 保证：同一天，所有玩家算出的 seed 相同 → 抽取序列相同 → 地图相同。
    /// 第二天日期变了，地图自动不同。
    ///
    /// 这是一个静态工具类，不是 MonoBehaviour——它不需要挂在物体上。
    /// </summary>
    public static class DailySeed
    {
        /// <summary>
        /// 算出今天（UTC）的种子。例如 2026-05-22 → 20260522。
        /// </summary>
        public static int GetTodaySeed()
        {
            return int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"));
        }

        /// <summary>
        /// 用今天的种子创建一个随机器。
        /// </summary>
        public static System.Random CreateTodayRng()
        {
            return new System.Random(GetTodaySeed());
        }

        /// <summary>
        /// 用指定种子创建随机器（预留：以后支持玩家手动输入 seed，决策 D5 备选）。
        /// </summary>
        public static System.Random CreateRng(int seed)
        {
            return new System.Random(seed);
        }
    }
}
