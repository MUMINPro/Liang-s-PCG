using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 分层地图生成 · 第 3 层支撑：攀爬代价场 + 寻路 v0.1
//
// 把山面离散成一张导航网格（独立于地形渲染分辨率），每格按
// 「坡度 + 第2层地貌」分三档：
//   - 可走 Walkable  : 缓坡草甸/台地/阶梯，水平移动便宜
//   - 攀爬 Scramble  : 陡坡，能徒手爬但费体力（高代价）
//   - 屏障 Barrier   : 崖壁/悬垂/峡谷壁，徒手过不去 → 只能靠"辅助边"穿越
//
// 辅助边(AidEdge)：预计算「跨越一条屏障带」的捷径，连接屏障两侧的可走格。
//   崖壁带 → 标记 Cliff（上层放藤蔓/岩架）
//   峡谷洼 → 标记 Canyon（上层放桥）
// Dijkstra 在「普通格边 + 辅助边」上找最省力路径：当绕行太远时，
// 它会主动选择"挂辅助过屏障" → 连接实体因此天生出现在玩家真过不去的地方。
//
// 产物：从登山口到峰顶的格路径，并标出哪几步是辅助、属于哪种屏障。
// ============================================================

namespace MapGen
{
    /// <summary>一格在代价场里的可通行档位。</summary>
    public enum CellTier { Walkable, Scramble, Barrier }

    /// <summary>一条辅助边：跨越屏障带，连接两侧可走格。</summary>
    public struct AidEdge
    {
        public int toCell;          // 目标格索引
        public BarrierKind barrier; // 屏障类型（决定上层用藤蔓还是桥）
        public float cost;          // 通行代价
        public float verticalGain;  // 跨越的垂直落差（正=向上爬）
    }

    /// <summary>寻路结果里的一步。</summary>
    public struct PathStep
    {
        public int cell;            // 格索引
        public bool viaAid;         // 是否经辅助边到达本格
        public BarrierKind barrier; // viaAid 时的屏障类型
        public float verticalGain;  // viaAid 时的落差
    }

    public class ClimbCostField
    {
        // ---- 配置 ----
        public int navRes = 96;            // 导航网格边格数（独立于渲染分辨率）
        public float walkSlope = 0.6f;     // ≤ 此坡度(rise/run, ~31°) = 可走
        public float scrambleSlope = 1.4f; // ≤ 此坡度(~54°) = 攀爬；超过 = 屏障
        public float scrambleMult = 3.2f;  // 攀爬格的水平代价倍率（费体力）
        public float climbFactor = 2.5f;   // 垂直爬升相对水平的代价权重
        public int maxAidSpanCells = 6;    // 辅助边最多跨几格屏障
        public float aidBaseCost = 40f;    // 挂一次辅助的固定代价（米当量）
        public float aidSpanCost = 2.5f;   // 辅助边每米跨度附加代价

        // ---- 数据 ----
        public float terrainSize;
        public float navStep;
        public float mountainHeight;
        float[] height;        // 每格地表高度（含第2层地貌）
        CellTier[] tier;       // 每格档位
        Dictionary<int, List<AidEdge>> aids = new Dictionary<int, List<AidEdge>>();

        System.Func<float, float, float> sampleHeight;

        public ClimbCostField(System.Func<float, float, float> heightSampler,
            float terrainSize, float mountainHeight, int navRes)
        {
            this.sampleHeight = heightSampler;
            this.terrainSize = terrainSize;
            this.mountainHeight = mountainHeight;
            this.navRes = Mathf.Max(16, navRes);
        }

        // ---------------- 构建 ----------------
        public void Build()
        {
            navStep = terrainSize / (navRes - 1);
            int n = navRes * navRes;
            height = new float[n];
            tier = new CellTier[n];

            // 1) 采样地表高度
            for (int z = 0; z < navRes; z++)
                for (int x = 0; x < navRes; x++)
                    height[z * navRes + x] = sampleHeight(x * navStep, z * navStep);

            // 2) 坡度分档
            for (int z = 0; z < navRes; z++)
                for (int x = 0; x < navRes; x++)
                {
                    float s = SlopeAt(x, z);
                    int i = z * navRes + x;
                    tier[i] = s <= walkSlope ? CellTier.Walkable
                            : s <= scrambleSlope ? CellTier.Scramble
                            : CellTier.Barrier;
                }

            // 3) 预计算辅助边（跨越屏障带）
            BuildAidEdges();
        }

        float SlopeAt(int x, int z)
        {
            int xl = Mathf.Max(0, x - 1), xr = Mathf.Min(navRes - 1, x + 1);
            int zl = Mathf.Max(0, z - 1), zr = Mathf.Min(navRes - 1, z + 1);
            float dhx = (height[z * navRes + xr] - height[z * navRes + xl]) / ((xr - xl) * navStep);
            float dhz = (height[zr * navRes + x] - height[zl * navRes + x]) / ((zr - zl) * navStep);
            return Mathf.Sqrt(dhx * dhx + dhz * dhz);
        }

        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>对每个非屏障格，向8方向探测：若紧邻屏障带，跨过它连到对面可走格。</summary>
        void BuildAidEdges()
        {
            aids.Clear();
            for (int z = 0; z < navRes; z++)
                for (int x = 0; x < navRes; x++)
                {
                    int from = z * navRes + x;
                    if (tier[from] == CellTier.Barrier) continue;

                    for (int d = 0; d < 8; d++)
                    {
                        // 紧邻必须是屏障，才考虑"跨越"
                        int nx = x + DX[d], nz = z + DZ[d];
                        if (!InBounds(nx, nz)) continue;
                        if (tier[nz * navRes + nx] != CellTier.Barrier) continue;

                        // 沿该方向march，跨过连续屏障，落到对面非屏障格
                        for (int span = 2; span <= maxAidSpanCells; span++)
                        {
                            int tx = x + DX[d] * span, tz = z + DZ[d] * span;
                            if (!InBounds(tx, tz)) break;
                            int to = tz * navRes + tx;
                            if (tier[to] == CellTier.Barrier) continue; // 还在屏障里，继续跨
                            // 落地：from→to 是一条辅助边
                            AddAid(from, to, x, z, tx, tz, span, d);
                            break;
                        }
                    }
                }
        }

        void AddAid(int from, int to, int x, int z, int tx, int tz, int span, int dir)
        {
            float dist = span * navStep * (dir < 4 ? 1f : 1.41421f);
            float vGain = height[to] - height[from];

            // 分类：跨度中点比两端明显低 = 峡谷洼地(架桥)，否则 = 崖壁(藤蔓/岩架)
            int mx = (x + tx) / 2, mz = (z + tz) / 2;
            float midH = height[mz * navRes + mx];
            float endMin = Mathf.Min(height[from], height[to]);
            BarrierKind kind = midH < endMin - 4f ? BarrierKind.Canyon : BarrierKind.Cliff;

            float cost = aidBaseCost + dist * aidSpanCost + Mathf.Max(0f, vGain) * climbFactor;
            if (!aids.TryGetValue(from, out var list)) { list = new List<AidEdge>(); aids[from] = list; }
            list.Add(new AidEdge { toCell = to, barrier = kind, cost = cost, verticalGain = vGain });
        }

        bool InBounds(int x, int z) => x >= 0 && x < navRes && z >= 0 && z < navRes;

        // ---------------- 坐标转换 ----------------
        public Vector2Int WorldToCell(float wx, float wz)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt(wx / navStep), 0, navRes - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(wz / navStep), 0, navRes - 1);
            return new Vector2Int(x, z);
        }

        public Vector3 CellToWorld(int cell)
        {
            int x = cell % navRes, z = cell / navRes;
            return new Vector3(x * navStep, height[cell], z * navStep);
        }
        public Vector3 CellToWorld(Vector2Int c) => CellToWorld(c.y * navRes + c.x);
        public int CellIndex(Vector2Int c) => c.y * navRes + c.x;
        public CellTier TierAt(int cell) => tier[cell];

        // ---------------- Dijkstra 寻路 ----------------
        /// <summary>
        /// 从 start 到 goal 找最省力路径。extraCellCost 给每格附加代价（路线分离用：
        /// 已被别的路占用的格加价，逼后续路线另辟蹊径）。无解返回 null。
        /// </summary>
        public List<PathStep> FindPath(Vector2Int start, Vector2Int goal, float[] extraCellCost)
        {
            int n = navRes * navRes;
            var dist = new float[n];
            var prev = new int[n];
            var prevAid = new bool[n];
            var prevBarrier = new BarrierKind[n];
            var prevVGain = new float[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }

            int s = CellIndex(start), g = CellIndex(goal);
            // 起点/终点若落在屏障上，拉到最近的非屏障格
            s = NearestNonBarrier(s); g = NearestNonBarrier(g);
            dist[s] = 0f;

            var heap = new MinHeap(n);
            heap.Push(s, 0f);

            while (heap.Count > 0)
            {
                int cur = heap.Pop();
                if (cur == g) break;
                float dc = dist[cur];
                int cx = cur % navRes, cz = cur / navRes;

                // 普通8邻格边
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], nz = cz + DZ[d];
                    if (!InBounds(nx, nz)) continue;
                    int nb = nz * navRes + nx;
                    if (tier[nb] == CellTier.Barrier) continue; // 屏障普通边不可走，靠辅助边

                    float horiz = navStep * (d < 4 ? 1f : 1.41421f);
                    float vGain = Mathf.Abs(height[nb] - height[cur]);
                    float mult = tier[nb] == CellTier.Scramble ? scrambleMult : 1f;
                    float w = (horiz + vGain * climbFactor) * mult
                            + (extraCellCost != null ? extraCellCost[nb] : 0f);
                    Relax(cur, nb, dc + w, false, default, height[nb] - height[cur],
                          dist, prev, prevAid, prevBarrier, prevVGain, heap);
                }

                // 辅助边（跨屏障）
                if (aids.TryGetValue(cur, out var list))
                    foreach (var a in list)
                    {
                        float w = a.cost + (extraCellCost != null ? extraCellCost[a.toCell] : 0f);
                        Relax(cur, a.toCell, dc + w, true, a.barrier, a.verticalGain,
                              dist, prev, prevAid, prevBarrier, prevVGain, heap);
                    }
            }

            if (prev[g] == -1 && s != g) return null; // 无解

            // 回溯
            var path = new List<PathStep>();
            for (int c = g; c != -1; c = prev[c])
            {
                path.Add(new PathStep
                {
                    cell = c, viaAid = prevAid[c],
                    barrier = prevBarrier[c], verticalGain = prevVGain[c]
                });
                if (c == s) break;
            }
            path.Reverse();
            return path;
        }

        void Relax(int from, int to, float nd, bool aid, BarrierKind bk, float vg,
            float[] dist, int[] prev, bool[] prevAid, BarrierKind[] prevBarrier, float[] prevVGain,
            MinHeap heap)
        {
            if (nd >= dist[to]) return;
            dist[to] = nd; prev[to] = from;
            prevAid[to] = aid; prevBarrier[to] = bk; prevVGain[to] = vg;
            heap.Push(to, nd);
        }

        int NearestNonBarrier(int cell)
        {
            if (tier[cell] != CellTier.Barrier) return cell;
            int cx = cell % navRes, cz = cell / navRes;
            for (int r = 1; r < navRes; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue; // 只查环
                        int nx = cx + dx, nz = cz + dz;
                        if (InBounds(nx, nz) && tier[nz * navRes + nx] != CellTier.Barrier)
                            return nz * navRes + nx;
                    }
            return cell;
        }

        // ---------------- 极简二叉堆 ----------------
        class MinHeap
        {
            int[] cell; float[] pri; int count;
            public MinHeap(int cap) { cell = new int[cap + 1]; pri = new float[cap + 1]; }
            public int Count => count;
            public void Push(int c, float p)
            {
                if (count + 1 >= cell.Length)
                { System.Array.Resize(ref cell, cell.Length * 2); System.Array.Resize(ref pri, pri.Length * 2); }
                count++; cell[count] = c; pri[count] = p;
                int i = count;
                while (i > 1 && pri[i] < pri[i / 2]) { Swap(i, i / 2); i /= 2; }
            }
            public int Pop()
            {
                int top = cell[1];
                cell[1] = cell[count]; pri[1] = pri[count]; count--;
                int i = 1;
                while (true)
                {
                    int l = i * 2, r = i * 2 + 1, m = i;
                    if (l <= count && pri[l] < pri[m]) m = l;
                    if (r <= count && pri[r] < pri[m]) m = r;
                    if (m == i) break;
                    Swap(i, m); i = m;
                }
                return top;
            }
            void Swap(int a, int b)
            { (cell[a], cell[b]) = (cell[b], cell[a]); (pri[a], pri[b]) = (pri[b], pri[a]); }
        }
    }
}
