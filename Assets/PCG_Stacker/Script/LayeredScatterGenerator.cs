using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCG
{
    /// <summary>
    /// 分层撒点生成器（★核心）。取代旧的 SegmentStacker。
    ///
    /// v2.1 改进：解决"漂浮点云、没有主次"的问题。
    /// 核心思想升级为「主路 + 分支」：
    ///   1. 保证链做成一条【连续的主路径】——每层的保证点从【上一层的保证点】往上接，
    ///      形成一条蜿蜒向上、可追踪的脊柱，而不是东跳西跳。
    ///   2. 自由点【围绕主路就近撒】——在当前层主路点附近的小半径内撒，
    ///      让分支簇拥在主路周围，形成主次，而不是在大圆里满天乱撒。
    ///   3. 撒点带【最小间距检查】——离已有点太近就重撒，消除重叠穿模。
    ///
    /// 所有落脚点都在规整水平层、垂直永远向上，因此塔不会自己撞自己，无需空间冲突检测。
    /// </summary>
    public class LayeredScatterGenerator
    {
        /// <summary>一个已放置落脚点的记录。isMainPath 标记它是否属于主路（便于调试/以后做视觉区分）。</summary>
        public struct PlacedFoothold
        {
            public Vector3 position;
            public GameObject instance;
            public bool isMainPath;
        }

        public List<PlacedFoothold> Generate(
            FootholdLibrary library,
            System.Random rng,
            ScatterSettings settings,
            Vector3 startPosition,
            Transform parent = null)
        {
            var allPlaced = new List<PlacedFoothold>();

            if (library == null)
            {
                Debug.LogError("[LayeredScatterGenerator] library 为 null，无法生成。");
                return allPlaced;
            }

            // 主路的当前末端。第 0 层从起点往上接。
            Vector3 mainPathTip = startPosition;

            // 记录所有已放点的位置，用于最小间距检查（避免重叠）
            var allPositions = new List<Vector3> { startPosition };

            for (int layer = 0; layer < settings.layerCount; layer++)
            {
                float layerY = startPosition.y + (layer + 1) * settings.layerHeight;

                // ---- ① 主路点：从【上一层主路末端】往上接，保证连续 ----
                Vector3 mainPos = PickReachablePoint(mainPathTip, layerY, settings, rng, allPositions);
                PlaceFoothold(library, rng, mainPos, parent, allPlaced, allPositions, $"L{layer}_MAIN", true);
                // 更新主路末端，下一层接着它往上长
                mainPathTip = mainPos;

                // ---- ② 分支自由点：围绕【这一层的主路点】就近撒 ----
                int extra = rng.Next(settings.minBranchPerLayer, settings.maxBranchPerLayer + 1);
                for (int e = 0; e < extra; e++)
                {
                    Vector3 branchPos = PickPointNear(mainPos, layerY, settings.branchRadius, rng, allPositions, settings.minSpacing);
                    if (branchPos == Vector3.positiveInfinity) continue; // 找不到合法位置就跳过这个分支点
                    PlaceFoothold(library, rng, branchPos, parent, allPlaced, allPositions, $"L{layer}_B{e}", false);
                }
            }

            return allPlaced;
        }

        // 在 jumpSource 的可达环内取一个保证可达的点（带间距检查，多次尝试）
        private Vector3 PickReachablePoint(
            Vector3 jumpSource, float layerY, ScatterSettings settings,
            System.Random rng, List<Vector3> existing)
        {
            const int maxTries = 12;
            Vector3 candidate = Vector3.zero;
            for (int t = 0; t < maxTries; t++)
            {
                double angle = rng.NextDouble() * Math.PI * 2.0;
                double dist = settings.minJumpDistance +
                              rng.NextDouble() * (settings.maxJumpDistance - settings.minJumpDistance);

                float x = jumpSource.x + (float)(Math.Cos(angle) * dist);
                float z = jumpSource.z + (float)(Math.Sin(angle) * dist);
                candidate = new Vector3(x, layerY, z);

                // 主路点必须保证放下（可达性优先于间距），所以间距不满足也只是再试，
                // 最后一次尝试无论如何都返回，绝不丢失主路点。
                if (FarEnough(candidate, existing, settings.minSpacing) || t == maxTries - 1)
                    return candidate;
            }
            return candidate;
        }

        // 在 center 附近的小半径内取一个点（带间距检查；找不到返回 positiveInfinity 表示放弃）
        private Vector3 PickPointNear(
            Vector3 center, float layerY, float radius,
            System.Random rng, List<Vector3> existing, float minSpacing)
        {
            const int maxTries = 10;
            for (int t = 0; t < maxTries; t++)
            {
                double angle = rng.NextDouble() * Math.PI * 2.0;
                double r = radius * Math.Sqrt(rng.NextDouble());
                float x = center.x + (float)(Math.Cos(angle) * r);
                float z = center.z + (float)(Math.Sin(angle) * r);
                Vector3 candidate = new Vector3(x, layerY, z);

                if (FarEnough(candidate, existing, minSpacing))
                    return candidate;
            }
            return Vector3.positiveInfinity; // 试了多次都太挤，放弃这个分支点
        }

        // 检查 candidate 是否与所有已有点保持最小间距（只比水平距离即可，因为分层了）
        private bool FarEnough(Vector3 candidate, List<Vector3> existing, float minSpacing)
        {
            float sqrMin = minSpacing * minSpacing;
            for (int i = 0; i < existing.Count; i++)
            {
                // 只在同一层附近才需要查（不同层 Y 差很大，水平再近也不会真重叠），
                // 但简单起见这里用 3D 距离，minSpacing 一般小于 layerHeight，效果等价。
                if ((existing[i] - candidate).sqrMagnitude < sqrMin)
                    return false;
            }
            return true;
        }

        private void PlaceFoothold(
            FootholdLibrary library, System.Random rng, Vector3 pos, Transform parent,
            List<PlacedFoothold> allPlaced, List<Vector3> allPositions, string nameSuffix, bool isMainPath)
        {
            FootholdData data = library.GetRandomFoothold(rng);
            if (data == null || data.prefab == null)
            {
                Debug.LogWarning($"[LayeredScatterGenerator] {nameSuffix} 抽取失败，跳过。");
                return;
            }

            GameObject instance = UnityEngine.Object.Instantiate(data.prefab, pos, Quaternion.identity, parent);
            instance.name = $"{nameSuffix}_{data.footholdName}";

            allPlaced.Add(new PlacedFoothold { position = pos, instance = instance, isMainPath = isMainPath });
            allPositions.Add(pos);
        }
    }
}
