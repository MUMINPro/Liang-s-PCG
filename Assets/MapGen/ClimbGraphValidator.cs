using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 攀爬路线图 - 验证器 v0.1
// 对应《关卡设计参数表》第 5 节"一张好图的验收指标"。
// 离线运行，烘焙管线里验证失败的图直接换种子重生成。
// ============================================================

namespace MapGen
{
    public class ValidationItem
    {
        public string label;
        public bool pass;
        public string detail;
        public bool advisory; // true = 仅参考，不参与合格判定
    }

    public class ValidationReport
    {
        public List<ValidationItem> items = new List<ValidationItem>();

        /// <summary>是否合格：只看硬性项（advisory 项即使报红也不影响）。</summary>
        public bool AllPass
        {
            get { foreach (var i in items) if (!i.advisory && !i.pass) return false; return true; }
        }

        public void Add(string label, bool pass, string detail, bool advisory = false) =>
            items.Add(new ValidationItem
            { label = label, pass = pass, detail = detail, advisory = advisory });
    }

    public static class ClimbGraphValidator
    {
        // 时间估算用的粗糙常数（后期用实测数据替换）
        const float ClimbSpeed = 1.2f;   // 米/秒
        const float WalkSpeed = 3f;      // 米/秒
        const float RestPause = 5f;      // 每个休息点平均停留秒数
        static readonly Vector2 SegmentMinutesRange = new Vector2(4f, 18f);

        public static ValidationReport Validate(MapDesignConfig cfg, ClimbGraph g)
        {
            var rep = new ValidationReport();
            float maxSt = cfg.player.maxStamina;
            float R = cfg.R;

            // 收集主路径攀爬边（排除平走）
            var mainClimbs = new List<ClimbEdge>();
            float totalMainCost = 0f;
            foreach (var e in g.edges)
                if (e.role == EdgeRole.MainPath)
                {
                    totalMainCost += e.cost;
                    if (e.type != EdgeType.Walk) mainClimbs.Add(e);
                }

            // 1. 主路径单段上限
            float capCost = cfg.topology.mainPathMaxEdgeCost * maxSt;
            ClimbEdge worst = null;
            foreach (var e in mainClimbs)
                if (worst == null || e.cost > worst.cost) worst = e;
            bool capOk = worst == null || worst.cost <= capCost + 0.01f;
            rep.Add("主路径单段 ≤ " + cfg.topology.mainPathMaxEdgeCost.ToString("F2") + "R",
                capOk,
                worst == null ? "无攀爬边" :
                "最贵一段 " + (worst.cost / maxSt).ToString("F2") + "R");

            // 2. 主路径总消耗（区间随山体高度自动缩放）
            float totalR = totalMainCost / maxSt;
            var range = cfg.topology.EffectiveCostRange(g.totalAltitude);
            rep.Add("总消耗在 " + range.x.ToString("F0") + "–" + range.y.ToString("F0") + "R",
                totalR >= range.x && totalR <= range.y,
                "实际 " + totalR.ToString("F1") + "R"
                + (cfg.topology.autoScaleByAltitude ? "（按高度自动）" : ""));

            // 3. 攀爬段数（区间随山体高度自动缩放）
            var cntRange = cfg.topology.EffectiveEdgeCountRange(g.totalAltitude);
            rep.Add("攀爬段数在 " + cntRange.x + "–" + cntRange.y,
                mainClimbs.Count >= cntRange.x && mainClimbs.Count <= cntRange.y,
                "实际 " + mainClimbs.Count + " 段");

            // 4. 连通性（从起点 BFS，所有节点可达）
            int reachable = CountReachable(g);
            rep.Add("无孤立/不可达节点",
                reachable == g.nodes.Count,
                reachable + " / " + g.nodes.Count + " 可达");

            // 5. 难度递增：各群系主路径平均消耗严格递增
            var biomeAvg = new Dictionary<int, (float sum, int n)>();
            foreach (var e in mainClimbs)
            {
                int bi = g.GetNode(e.from).biomeIndex;
                if (!biomeAvg.ContainsKey(bi)) biomeAvg[bi] = (0f, 0);
                var v = biomeAvg[bi];
                biomeAvg[bi] = (v.sum + e.cost, v.n + 1);
            }
            bool increasing = true;
            float prevAvg = -1f;
            var detail = new System.Text.StringBuilder();
            for (int bi = 0; bi < cfg.biomes.Count; bi++)
            {
                if (!biomeAvg.ContainsKey(bi) || biomeAvg[bi].n == 0) continue;
                float avg = biomeAvg[bi].sum / biomeAvg[bi].n;
                if (prevAvg >= 0f && avg <= prevAvg) increasing = false;
                if (detail.Length > 0) detail.Append(" → ");
                detail.Append(cfg.biomes[bi].biomeName).Append(" ")
                      .Append((avg / maxSt).ToString("F2")).Append("R");
                prevAvg = avg;
            }
            rep.Add("难度逐群系递增", increasing, detail.ToString());

            // 6. 每营火段分支数符合群系区间
            // 分支按"起源"数而不是边数统计：用每段的非主路径起点去重
            var branchStarts = new HashSet<(int seg, int from, EdgeRole role)>();
            foreach (var e in g.edges)
                if (e.role != EdgeRole.MainPath)
                    branchStarts.Add((e.segmentIndex, RootOfBranch(g, e), e.role));
            var perSeg = new int[Mathf.Max(1, g.SegmentCount)];
            foreach (var b in branchStarts)
                if (b.seg >= 0 && b.seg < perSeg.Length) perSeg[b.seg]++;
            var segDetail = new System.Text.StringBuilder("各段: ");
            for (int s = 0; s < perSeg.Length; s++)
                segDetail.Append(perSeg[s]).Append(s < perSeg.Length - 1 ? ", " : "");
            rep.Add("每段均有分支", AllPositive(perSeg), segDetail.ToString());

            // 7. 奖励支线配额
            int lootNodes = 0;
            foreach (var n in g.nodes) if (n.kind == NodeKind.Loot) lootNodes++;
            int quota = g.SegmentCount; // 至少每段 1 个
            rep.Add("奖励点 ≥ 营火段数", lootNodes >= quota,
                lootNodes + " 个奖励点 / " + g.SegmentCount + " 段");

            // 8. 每段预估时长（粗糙估算，原型期仅供参考）
            float worstSegMin = 0f, bestSegMin = float.MaxValue;
            for (int s = 0; s < g.SegmentCount; s++)
            {
                float t = 0f;
                foreach (var e in g.edges)
                {
                    if (e.role != EdgeRole.MainPath || e.segmentIndex != s) continue;
                    t += e.type == EdgeType.Walk
                        ? e.climbLength / WalkSpeed
                        : e.climbLength / ClimbSpeed + RestPause;
                }
                float min = t / 60f;
                worstSegMin = Mathf.Max(worstSegMin, min);
                bestSegMin = Mathf.Min(bestSegMin, min);
            }
            bool timeOk = g.SegmentCount == 0 ||
                (bestSegMin >= SegmentMinutesRange.x * 0.5f && worstSegMin <= SegmentMinutesRange.y);
            rep.Add("营火段时长（仅参考·待实测校准）", timeOk,
                g.SegmentCount == 0 ? "-" :
                bestSegMin.ToString("F1") + "–" + worstSegMin.ToString("F1") + " 分钟",
                advisory: true);

            return rep;
        }

        /// <summary>沿支线往回找到它挂接在主路径上的起点（用于把多条边的支线算作 1 个分支）。</summary>
        static int RootOfBranch(ClimbGraph g, ClimbEdge edge)
        {
            var mainSet = new HashSet<int>(g.mainPath);
            int cur = edge.from;
            int guard = 0;
            while (!mainSet.Contains(cur) && guard++ < 100)
            {
                bool found = false;
                foreach (var e in g.edges)
                    if (e.to == cur && e.role == edge.role) { cur = e.from; found = true; break; }
                if (!found) break;
            }
            return cur;
        }

        static int CountReachable(ClimbGraph g)
        {
            if (g.nodes.Count == 0) return 0;
            var adj = new Dictionary<int, List<int>>();
            foreach (var e in g.edges)
            {
                if (!adj.ContainsKey(e.from)) adj[e.from] = new List<int>();
                if (!adj.ContainsKey(e.to)) adj[e.to] = new List<int>();
                adj[e.from].Add(e.to);
                adj[e.to].Add(e.from); // 可视化原型期按无向图算连通性
            }
            var seen = new HashSet<int> { 0 };
            var queue = new Queue<int>();
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                if (!adj.ContainsKey(cur)) continue;
                foreach (var nb in adj[cur])
                    if (seen.Add(nb)) queue.Enqueue(nb);
            }
            return seen.Count;
        }

        static bool AllPositive(int[] arr)
        {
            foreach (var v in arr) if (v <= 0) return false;
            return true;
        }
    }
}
