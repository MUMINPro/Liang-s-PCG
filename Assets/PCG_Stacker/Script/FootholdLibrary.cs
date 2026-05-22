using System;
using UnityEngine;

namespace PCG
{
    /// <summary>
    /// 落脚点库（ScriptableObject）。管理一批落脚点，提供"按权重随机抽一个"。
    /// 逻辑复用自旧 SegmentLibrary——随机器由外部传入，库不持有随机状态。
    ///
    /// 创建方式：Project 窗口右键 → Create → PCG → Foothold Library。
    ///
    /// 用法：可以给不同高度区间配不同的库（地面小山库 / 跳跳乐库 / 攀岩库），
    /// 实现"越往上主题越变"。具体哪段高度用哪个库，在 LevelGenerator 里配。
    /// </summary>
    [CreateAssetMenu(fileName = "FootholdLibrary", menuName = "PCG/Foothold Library")]
    public class FootholdLibrary : ScriptableObject
    {
        [Tooltip("这个库里的所有落脚点")]
        public FootholdData[] footholds;

        /// <summary>
        /// 用传入的随机器，按权重抽一个落脚点。
        /// </summary>
        public FootholdData GetRandomFoothold(System.Random rng)
        {
            if (footholds == null || footholds.Length == 0)
            {
                Debug.LogError("[FootholdLibrary] footholds 为空，无法抽取。");
                return null;
            }

            float totalWeight = 0f;
            for (int i = 0; i < footholds.Length; i++)
            {
                FootholdData f = footholds[i];
                if (f != null && f.weight > 0f)
                    totalWeight += f.weight;
            }

            if (totalWeight <= 0f)
            {
                Debug.LogError("[FootholdLibrary] 所有权重之和为 0，无法抽取。");
                return null;
            }

            double roll = rng.NextDouble() * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < footholds.Length; i++)
            {
                FootholdData f = footholds[i];
                if (f == null || f.weight <= 0f) continue;

                cumulative += f.weight;
                if (roll < cumulative)
                    return f;
            }

            for (int i = footholds.Length - 1; i >= 0; i--)
            {
                if (footholds[i] != null && footholds[i].weight > 0f)
                    return footholds[i];
            }

            return null;
        }
    }
}
