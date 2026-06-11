using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// ============================================================
// 路线图规格导出 v0.1（放在 Editor 文件夹内）
// 把抽象路线图导成两份建模施工依据：
//   1. edges.csv —— 每条边的完整规格（类型/长度/朝向/难度/角色）
//   2. summary.txt —— 人类可读的整体摘要 + 模块需求清单
//
// 这是"设计阶段"通向"建模阶段"的交接物：
// 你照着 CSV 就知道每段需要什么模块、多长、朝哪、多难。
// ============================================================

namespace MapGen.Editor
{
    public static class GraphSpecExporter
    {
        /// <summary>导出指定配置 + 种子的路线图规格到 Assets/MapGen/Exports/。</summary>
        public static string Export(MapDesignConfig cfg, int seed)
        {
            var g = ClimbGraphGenerator.Generate(cfg, seed);
            var rep = ClimbGraphValidator.Validate(cfg, g);

            string dir = "Assets/MapGen/Exports";
            if (!AssetDatabase.IsValidFolder("Assets/MapGen"))
                AssetDatabase.CreateFolder("Assets", "MapGen");
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets/MapGen", "Exports");

            string csvPath = $"{dir}/map_{seed}_edges.csv";
            string sumPath = $"{dir}/map_{seed}_summary.txt";
            File.WriteAllText(csvPath, BuildCsv(cfg, g), Encoding.UTF8);
            File.WriteAllText(sumPath, BuildSummary(cfg, g, rep), Encoding.UTF8);

            AssetDatabase.Refresh();
            Debug.Log($"[MapGen] 规格已导出：\n{csvPath}\n{sumPath}");
            return csvPath;
        }

        static string BuildCsv(MapDesignConfig cfg, ClimbGraph g)
        {
            var sb = new StringBuilder();
            // 表头
            sb.AppendLine("边序号,角色,攀爬类型,起点节点,终点节点,起点海拔(m),终点海拔(m)," +
                          "攀爬距离(m),海拔增量(m),水平跨度(m),朝向角(度),难度(R),难度占比%,跳跃次数,所属营火段");
            float maxSt = cfg.player.maxStamina;
            int idx = 0;
            foreach (var e in g.edges)
            {
                var a = g.GetNode(e.from);
                var b = g.GetNode(e.to);
                float dAlt = b.pos.y - a.pos.y;
                float dX = b.pos.x - a.pos.x;
                // 朝向角：在侧视 2D 平面里，攀爬方向相对水平的角度（90=纯垂直）
                float angle = Mathf.Atan2(dAlt, Mathf.Abs(dX) < 0.01f ? 0.01f : dX) * Mathf.Rad2Deg;
                float r = e.cost / maxSt;
                sb.AppendLine(
                    $"{idx}," +
                    $"{RoleCN(e.role)}," +
                    $"{TypeCN(e.type)}," +
                    $"{e.from},{e.to}," +
                    $"{a.pos.y:F1},{b.pos.y:F1}," +
                    $"{e.climbLength:F1},{dAlt:F1},{dX:F1}," +
                    $"{angle:F0}," +
                    $"{r:F2},{r * 100f:F0}," +
                    $"{e.jumpCount}," +
                    $"{e.segmentIndex}");
                idx++;
            }
            return sb.ToString();
        }

        static string BuildSummary(MapDesignConfig cfg, ClimbGraph g, ValidationReport rep)
        {
            var sb = new StringBuilder();
            sb.AppendLine("====== 路线图规格摘要 ======");
            sb.AppendLine($"种子: {g.seed}");
            sb.AppendLine($"山体总爬升: {g.totalAltitude:F0} m   (R = {cfg.R:F0} m)");
            sb.AppendLine($"节点 {g.nodes.Count}   边 {g.edges.Count}   营火段 {g.SegmentCount}");
            sb.AppendLine($"验收: {(rep.AllPass ? "通过 ✓" : "不通过 ✗")}");
            sb.AppendLine();

            // 各边类型的数量统计（= 你要建多少种、各多少个模块）
            sb.AppendLine("------ 模块需求清单（按攀爬类型）------");
            var typeCount = new Dictionary<EdgeType, int>();
            var typeLenSum = new Dictionary<EdgeType, float>();
            foreach (var e in g.edges)
            {
                if (!typeCount.ContainsKey(e.type)) { typeCount[e.type] = 0; typeLenSum[e.type] = 0; }
                typeCount[e.type]++;
                typeLenSum[e.type] += e.climbLength;
            }
            foreach (var kv in typeCount)
                sb.AppendLine($"  {TypeCN(kv.Key),-6}: {kv.Value,3} 段   " +
                              $"总长 {typeLenSum[kv.Key]:F0} m   " +
                              $"平均 {typeLenSum[kv.Key] / kv.Value:F1} m/段");
            sb.AppendLine();

            // 各营火段的难度画像
            sb.AppendLine("------ 各营火段难度画像 ------");
            float maxSt = cfg.player.maxStamina;
            for (int s = 0; s < g.SegmentCount; s++)
            {
                float cost = 0f, maxEdge = 0f; int climbs = 0;
                foreach (var e in g.edges)
                {
                    if (e.role != EdgeRole.MainPath || e.segmentIndex != s) continue;
                    if (e.type == EdgeType.Walk) continue;
                    cost += e.cost; climbs++;
                    maxEdge = Mathf.Max(maxEdge, e.cost);
                }
                int i0 = g.campfireIndicesInMainPath[s];
                int i1 = g.campfireIndicesInMainPath[s + 1];
                float a0 = g.GetNode(g.mainPath[i0]).pos.y;
                float a1 = g.GetNode(g.mainPath[i1]).pos.y;
                var biome = cfg.GetBiomeAtAltitude((a0 + a1) * 0.5f / g.totalAltitude);
                sb.AppendLine($"  段{s} [{biome?.biomeName}] 海拔 {a0:F0}→{a1:F0}m: " +
                              $"{climbs}段  总{cost / maxSt:F1}R  最难一段{maxEdge / maxSt:F2}R");
            }
            sb.AppendLine();
            sb.AppendLine("提示: 详细逐边规格见同目录 *_edges.csv，可用 Excel 打开。");
            return sb.ToString();
        }

        static string RoleCN(EdgeRole r) => r switch
        {
            EdgeRole.MainPath => "主路径",
            EdgeRole.Shortcut => "捷径",
            EdgeRole.SafeDetour => "安全迂回",
            EdgeRole.LootSpur => "奖励支线",
            _ => r.ToString()
        };

        static string TypeCN(EdgeType t) => t switch
        {
            EdgeType.Walk => "行走",
            EdgeType.Climb => "岩壁",
            EdgeType.Overhang => "反斜面",
            EdgeType.JumpGap => "跳跃",
            EdgeType.Vine => "藤蔓",
            EdgeType.Cave => "洞穴",
            EdgeType.PillarChain => "岩柱链",
            _ => t.ToString()
        };
    }
}
