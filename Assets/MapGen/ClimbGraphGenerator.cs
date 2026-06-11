using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 攀爬路线图 - 生成器 v0.1
// 输入：MapDesignConfig + 种子。输出：ClimbGraph。
// 完全确定性：同配置 + 同种子 = 同一座山（联机同步的基础）。
//
// 生成顺序（对应参数表第 3、4 节）：
//   1. 按群系边界排布营火海拔
//   2. 逐营火段生成主路径（休息间距由群系参数驱动）
//   3. 每段内生成分支（捷径 / 安全迂回 / 奖励支线）
//   4. 补足奖励支线配额
// ============================================================

namespace MapGen
{
    public static class ClimbGraphGenerator
    {
        // 攀爬段的"垂直度"：攀爬距离中有多少转化为海拔。
        // 调低 = 山更斜更宽（盘旋形态）；捷径近乎直上。
        const float MainVerticalityMin = 0.5f;
        const float MainVerticalityMax = 0.72f;
        const float ShortcutVerticality = 0.95f;
        const float WalkChance = 0.18f; // 休息点后接一段平走的概率

        // 螺旋形态参数现在全部来自 cfg.spiral（可在 Inspector 调）。

        public static ClimbGraph Generate(MapDesignConfig cfg, int seed)
        {
            var rng = new System.Random(seed);
            var g = new ClimbGraph { seed = seed, totalAltitude = cfg.totalAltitude };
            float maxSt = cfg.player.maxStamina;
            float perM = cfg.player.climbCostPerMeter;

            // ---------- 1. 营火海拔 ----------
            var fires = ComputeCampfireAltitudes(cfg);

            // ---------- 2. 主路径 ----------
            int prev = AddNode(g, Vector3.zero, NodeKind.Start, BiomeIndexAt(cfg, 0f));
            g.mainPath.Add(prev);
            g.campfireIndicesInMainPath.Add(0);

            // 螺旋骨架参数（每张图随机化起点，保证种子间差异）
            float baseRadius = cfg.totalAltitude * cfg.spiral.radiusFrac;
            float startAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
            int spinDir = rng.NextDouble() < 0.5 ? -1 : 1; // 顺/逆时针随机
            // 噪声采样的随机偏移（让每个种子的噪声场不同）
            float noiseOX = (float)rng.NextDouble() * 1000f;
            float noiseOZ = (float)rng.NextDouble() * 1000f;
            float jitterAmp = cfg.totalAltitude * cfg.spiral.localJitterFrac;

            for (int f = 1; f < fires.Count; f++)
            {
                float targetAlt = fires[f];
                bool isSummit = (f == fires.Count - 1);
                int segIndex = f - 1;
                int guard = 0;

                while (g.GetNode(prev).pos.y < targetAlt - 0.01f && guard++ < 200)
                {
                    float alt = g.GetNode(prev).pos.y;
                    var biome = cfg.GetBiomeAtAltitude(alt / cfg.totalAltitude);
                    int biomeIdx = cfg.biomes.IndexOf(biome);

                    // 体力预算：群系休息间距，钳制到主路径单段上限
                    float frac = Mathf.Min(
                        Mathf.Lerp(biome.restSpacingMin, biome.restSpacingMax, (float)rng.NextDouble()),
                        cfg.topology.mainPathMaxEdgeCost);
                    float staminaBudget = frac * maxSt;

                    EdgeType type = PickClimbType(cfg, biome, rng);
                    var rule = cfg.GetEdgeRule(type);

                    float climbLen, altGain, cost;
                    int jumps = 0;
                    float verticality = Mathf.Lerp(MainVerticalityMin, MainVerticalityMax, (float)rng.NextDouble());

                    if (rule.flatCost > 0f) // 跳跃类（JumpGap / PillarChain）
                    {
                        jumps = 1 + rng.Next(2);
                        cost = rule.flatCost * jumps;
                        altGain = jumps * 2.5f;
                        climbLen = altGain;
                        verticality = 0.6f;
                    }
                    else // 距离计费类
                    {
                        float mult = Mathf.Max(0.01f, rule.costMultiplier);
                        climbLen = staminaBudget / (mult * perM);
                        altGain = climbLen * verticality;
                        cost = staminaBudget;
                    }

                    // 不越过营火海拔：到顶就收
                    float remaining = targetAlt - alt;
                    if (altGain > remaining)
                    {
                        altGain = remaining;
                        climbLen = altGain / verticality;
                        if (rule.flatCost <= 0f)
                            cost = climbLen * Mathf.Max(0.01f, rule.costMultiplier) * perM;
                    }

                    float newAlt = alt + altGain;
                    bool reached = newAlt >= targetAlt - 0.01f;

                    // 3D 螺旋：由新海拔算出水平面位置
                    Vector3 prevPos = g.GetNode(prev).pos;
                    Vector3 newPos = SpiralPos(cfg, newAlt, baseRadius, startAngle, spinDir,
                        noiseOX, noiseOZ, jitterAmp, rng);

                    NodeKind kind = reached
                        ? (isSummit ? NodeKind.Summit : NodeKind.Campfire)
                        : NodeKind.Rest;

                    if (rule.flatCost > 0f)
                    {
                        // 跳跃类：不切分，直接成段
                        int nJump = AddNode(g, newPos, kind, biomeIdx);
                        AddEdge(g, prev, nJump, type, EdgeRole.MainPath, cost, climbLen, jumps, segIndex);
                        g.mainPath.Add(nJump);
                        prev = nJump;
                    }
                    else
                    {
                        // 距离计费类：若 3D 绕行距离超过单段上限，沿螺旋插入中间落脚点切分，
                        // 保证每段都在体力能承受范围内（解决盘旋离群长段爬不上去的问题）。
                        float capDist = cfg.topology.mainPathMaxEdgeCost * maxSt
                                        / (Mathf.Max(0.01f, rule.costMultiplier) * perM);
                        float fullDist = Vector3.Distance(prevPos, newPos);
                        int subdiv = Mathf.Max(1, Mathf.CeilToInt(fullDist / Mathf.Max(1f, capDist)));

                        for (int s = 1; s <= subdiv; s++)
                        {
                            float tt = s / (float)subdiv;
                            float midAlt = Mathf.Lerp(alt, newAlt, tt);
                            // 中间点也落在螺旋上（而不是直线插值），保持盘旋曲线
                            Vector3 segPos = (s == subdiv)
                                ? newPos
                                : SpiralPos(cfg, midAlt, baseRadius, startAngle, spinDir,
                                    noiseOX, noiseOZ, jitterAmp, rng);
                            Vector3 from = g.GetNode(prev).pos;
                            float segDist = Vector3.Distance(from, segPos);
                            float segCost = segDist * Mathf.Max(0.01f, rule.costMultiplier) * perM;
                            NodeKind segKind = (s == subdiv) ? kind : NodeKind.Rest;
                            int n = AddNode(g, segPos, segKind, biomeIdx);
                            AddEdge(g, prev, n, type, EdgeRole.MainPath, segCost, segDist, 0, segIndex);
                            g.mainPath.Add(n);
                            prev = n;
                        }
                    }

                    if (reached)
                    {
                        g.campfireIndicesInMainPath.Add(g.mainPath.Count - 1);
                    }
                    else if (rng.NextDouble() < WalkChance)
                    {
                        // 平走段：沿螺旋切线方向水平走一小段，免费回体力。
                        float wd = 4f + (float)rng.NextDouble() * 4f;
                        Vector3 tangent = SpiralPos(cfg, newAlt + 1f, baseRadius, startAngle, spinDir,
                            noiseOX, noiseOZ, jitterAmp, rng) - newPos;
                        tangent.y = 0f;
                        if (tangent.sqrMagnitude < 0.001f) tangent = Vector3.right;
                        Vector3 walkPos = newPos + tangent.normalized * wd;
                        int walkNode = AddNode(g, walkPos, NodeKind.Rest, biomeIdx);
                        AddEdge(g, prev, walkNode, EdgeType.Walk, EdgeRole.MainPath, 0f, wd, 0, segIndex);
                        g.mainPath.Add(walkNode);
                        prev = walkNode;
                    }
                }
            }

            // ---------- 3. 每段分支 ----------
            int lootCount = 0;
            for (int s = 0; s < g.SegmentCount; s++)
            {
                int i0 = g.campfireIndicesInMainPath[s];
                int i1 = g.campfireIndicesInMainPath[s + 1];
                float midAlt = (g.GetNode(g.mainPath[i0]).pos.y + g.GetNode(g.mainPath[i1]).pos.y) * 0.5f;
                var biome = cfg.GetBiomeAtAltitude(midAlt / cfg.totalAltitude);

                // 硬核密度：在群系区间基础上 +1，让选路更丰富
                int want = rng.Next(biome.branchCountRange.x, biome.branchCountRange.y + 1) + 1;
                for (int b = 0; b < want; b++)
                {
                    double roll = rng.NextDouble();
                    bool made;
                    // 捷径/迂回各占约 40%，奖励支线 20%；
                    // 且捷径/迂回若放置失败，先尝试对方，尽量不退化成奖励支线。
                    if (roll < 0.42)
                        made = TryShortcut(cfg, g, rng, i0, i1, s)
                            || TryDetour(cfg, g, rng, i0, i1, s, biome);
                    else if (roll < 0.82)
                        made = TryDetour(cfg, g, rng, i0, i1, s, biome)
                            || TryShortcut(cfg, g, rng, i0, i1, s);
                    else
                        made = false;

                    if (!made)
                    {
                        TryLootSpur(cfg, g, rng, i0, i1, s, biome);
                        lootCount++;
                    }
                }
            }

            // ---------- 4. 奖励支线配额 ----------
            int quota = Mathf.FloorToInt(g.SegmentCount * cfg.topology.lootSpurPerSegment);
            int guard2 = 0;
            while (lootCount < quota && guard2++ < 50)
            {
                int s = rng.Next(g.SegmentCount);
                int i0 = g.campfireIndicesInMainPath[s];
                int i1 = g.campfireIndicesInMainPath[s + 1];
                float midAlt = (g.GetNode(g.mainPath[i0]).pos.y + g.GetNode(g.mainPath[i1]).pos.y) * 0.5f;
                var biome = cfg.GetBiomeAtAltitude(midAlt / cfg.totalAltitude);
                if (TryLootSpur(cfg, g, rng, i0, i1, s, biome)) lootCount++;
            }

            return g;
        }

        // ====================== 分支构造 ======================

        /// <summary>捷径：直接连 i 与 i+2，跳过一个主路径节点。仅在几何上"贵得合理"时放置。</summary>
        static bool TryShortcut(MapDesignConfig cfg, ClimbGraph g, System.Random rng,
            int i0, int i1, int seg)
        {
            if (i1 - i0 < 3) return false;
            int i = i0 + rng.Next(i1 - i0 - 1); // [i0, i1-2]
            int a = g.mainPath[i], b = g.mainPath[i + 2];
            float altDiff = g.GetNode(b).pos.y - g.GetNode(a).pos.y;
            if (altDiff < 2f) return false;

            // 捷径优先用反斜面（在池里的话），否则普通岩壁
            EdgeType type = PoolContains(cfg, g.GetNode(a).biomeIndex, EdgeType.Overhang)
                ? EdgeType.Overhang : EdgeType.Climb;
            var rule = cfg.GetEdgeRule(type);
            float climbLen = altDiff / ShortcutVerticality;
            float cost = climbLen * rule.costMultiplier * cfg.player.climbCostPerMeter;

            // 太贵 = 物理上爬不动，放弃这个位置
            if (cost > cfg.topology.shortcutCostRange.y * cfg.player.maxStamina) return false;

            AddEdge(g, a, b, type, EdgeRole.Shortcut, cost, climbLen, 0, seg);
            return true;
        }

        /// <summary>安全迂回：i 与 i+1 之间加一条 2~3 节点的轻松绕行链。</summary>
        static bool TryDetour(MapDesignConfig cfg, ClimbGraph g, System.Random rng,
            int i0, int i1, int seg, BiomeConfig biome)
        {
            if (i1 - i0 < 2) return false;
            int i = i0 + rng.Next(i1 - i0 - 1);
            int a = g.mainPath[i], b = g.mainPath[i + 1];
            float altDiff = g.GetNode(b).pos.y - g.GetNode(a).pos.y;
            if (altDiff < 3f) return false;

            EdgeType type = PoolContains(cfg, g.GetNode(a).biomeIndex, EdgeType.Vine)
                ? EdgeType.Vine : EdgeType.Climb;
            var rule = cfg.GetEdgeRule(type);
            float perM = cfg.player.climbCostPerMeter;

            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            int hops = 2 + rng.Next(2); // 2~3 个中间节点
            int prev = a;
            Vector3 pa = g.GetNode(a).pos, pb = g.GetNode(b).pos;
            // 侧向 = a→b 在水平面的方向，绕 Y 轴转 90°
            Vector3 dirH = pb - pa; dirH.y = 0f;
            if (dirH.sqrMagnitude < 0.001f) dirH = Vector3.right;
            Vector3 sideDir = Vector3.Cross(Vector3.up, dirH.normalized);

            for (int k = 1; k <= hops; k++)
            {
                float t = k / (float)(hops + 1);
                Vector3 onLine = Vector3.Lerp(pa, pb, t);
                float off = side * (8f + (float)rng.NextDouble() * 6f);
                Vector3 pos = onLine + sideDir * off;
                int n = AddNode(g, pos, NodeKind.Rest, g.GetNode(a).biomeIndex);

                float climbLen = Vector3.Distance(g.GetNode(prev).pos, pos);
                float cost = Mathf.Min(climbLen * rule.costMultiplier * perM,
                    cfg.topology.detourMaxEdgeCost * cfg.player.maxStamina);
                AddEdge(g, prev, n, type, EdgeRole.SafeDetour, cost, climbLen, 0, seg);
                prev = n;
            }
            // 汇回主路径（规则 3：不存在不可逆分叉）
            float lastLen = Mathf.Max(2f, Vector3.Distance(g.GetNode(prev).pos, pb));
            AddEdge(g, prev, b, type, EdgeRole.SafeDetour,
                lastLen * rule.costMultiplier * perM, lastLen, 0, seg);
            return true;
        }

        /// <summary>奖励支线：死胡同，终点是 Loot 节点。</summary>
        static bool TryLootSpur(MapDesignConfig cfg, ClimbGraph g, System.Random rng,
            int i0, int i1, int seg, BiomeConfig biome)
        {
            if (i1 - i0 < 1) return false;
            int i = i0 + rng.Next(i1 - i0);
            int a = g.mainPath[i];

            EdgeType type =
                PoolContains(cfg, g.GetNode(a).biomeIndex, EdgeType.Cave) ? EdgeType.Cave :
                PoolContains(cfg, g.GetNode(a).biomeIndex, EdgeType.PillarChain) ? EdgeType.PillarChain :
                EdgeType.Climb;
            var rule = cfg.GetEdgeRule(type);
            float perM = cfg.player.climbCostPerMeter;

            int depth = rng.Next(cfg.topology.lootSpurDepthRange.x,
                cfg.topology.lootSpurDepthRange.y + 1);
            int prev = a;

            for (int k = 0; k < depth; k++)
            {
                Vector3 p = g.GetNode(prev).pos;
                // 水平面随机方向偏移
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float hr = 8f + (float)rng.NextDouble() * 6f;
                float dx = Mathf.Cos(ang) * hr;
                float dz = Mathf.Sin(ang) * hr;
                float dy = -2f + (float)rng.NextDouble() * 6f; // 可平可微升微降
                bool last = (k == depth - 1);
                Vector3 pos = new Vector3(p.x + dx, p.y + dy, p.z + dz);
                int n = AddNode(g, pos,
                    last ? NodeKind.Loot : NodeKind.Rest, g.GetNode(a).biomeIndex);

                float cost;
                float dist = Vector3.Distance(p, pos);
                int jumps = 0;
                if (rule.flatCost > 0f) { jumps = 1 + rng.Next(2); cost = rule.flatCost * jumps; }
                else if (rule.chargeHorizontal) cost = dist * rule.costMultiplier * perM;
                else cost = Mathf.Max(2f, Mathf.Max(0f, dy) * rule.costMultiplier * perM);

                AddEdge(g, prev, n, type, EdgeRole.LootSpur, cost, dist, jumps, seg);
                prev = n;
            }
            return true;
        }

        // ====================== 工具 ======================

        static List<float> ComputeCampfireAltitudes(MapDesignConfig cfg)
        {
            var alts = new List<float> { 0f };
            foreach (var b in cfg.biomes)
            {
                if (b.midBiomeCampfire)
                    alts.Add((b.altitudeMin + b.altitudeMax) * 0.5f * cfg.totalAltitude);
                alts.Add(Mathf.Min(b.altitudeMax, 1f) * cfg.totalAltitude);
            }
            alts.Sort();
            // 去重（边界重合时）
            var result = new List<float>();
            foreach (var a in alts)
                if (result.Count == 0 || a - result[result.Count - 1] > 1f) result.Add(a);
            return result;
        }

        static EdgeType PickClimbType(MapDesignConfig cfg, BiomeConfig biome, System.Random rng)
        {
            float total = 0f;
            foreach (var w in biome.edgeTypePool)
                if (w.type != EdgeType.Walk) total += w.weight;
            if (total <= 0f) return EdgeType.Climb;

            float r = (float)rng.NextDouble() * total;
            foreach (var w in biome.edgeTypePool)
            {
                if (w.type == EdgeType.Walk) continue;
                r -= w.weight;
                if (r <= 0f) return w.type;
            }
            return EdgeType.Climb;
        }

        static int BiomeIndexAt(MapDesignConfig cfg, float normalizedAltitude)
        {
            var b = cfg.GetBiomeAtAltitude(normalizedAltitude);
            return b != null ? cfg.biomes.IndexOf(b) : -1;
        }

        /// <summary>
        /// 给定海拔，算出主路径在水平面（X-Z）上的螺旋位置。
        /// 大尺度：绕中轴螺旋上升；噪声：半径忽胖忽瘦 + 角速度不规则 + 局部抖动。
        /// </summary>
        static Vector3 SpiralPos(MapDesignConfig cfg, float alt,
            float baseRadius, float startAngle, int spinDir,
            float noiseOX, float noiseOZ, float jitterAmp,
            System.Random rng)
        {
            float t = Mathf.Clamp01(alt / Mathf.Max(1f, cfg.totalAltitude)); // 0~1 进度

            // 角度推进：基础线性绕圈 + 噪声让角速度不均匀（盘旋忽快忽慢）
            float angleNoise = (Mathf.PerlinNoise(t * 3f + noiseOX, 7.3f) - 0.5f) * 2f * cfg.spiral.angleNoiseAmp;
            float angle = startAngle + spinDir * (t * cfg.spiral.turns * Mathf.PI * 2f + angleNoise * Mathf.PI);

            // 半径：基准 + 噪声起伏（山忽胖忽瘦、不对称）。低海拔略宽，高处收窄成峰。
            float radiusTaper = Mathf.Lerp(1.1f, cfg.spiral.summitTaper, t); // 越高越收
            float rNoise = (Mathf.PerlinNoise(t * cfg.spiral.radiusNoiseFreq + noiseOX, 2.1f) - 0.5f) * 2f * cfg.spiral.radiusNoiseAmp;
            float radius = baseRadius * radiusTaper * (1f + rNoise);

            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;

            // 局部抖动：每个落点在水平面上微微偏移，破除完美曲线感
            float jx = (Mathf.PerlinNoise(alt * 0.15f + noiseOX, 11f) - 0.5f) * 2f * jitterAmp;
            float jz = (Mathf.PerlinNoise(alt * 0.15f + noiseOZ, 17f) - 0.5f) * 2f * jitterAmp;

            return new Vector3(x + jx, alt, z + jz);
        }

        static bool PoolContains(MapDesignConfig cfg, int biomeIdx, EdgeType t)
        {
            if (biomeIdx < 0 || biomeIdx >= cfg.biomes.Count) return false;
            foreach (var w in cfg.biomes[biomeIdx].edgeTypePool)
                if (w.type == t && w.weight > 0f) return true;
            return false;
        }

        static int AddNode(ClimbGraph g, Vector3 pos, NodeKind kind, int biomeIdx)
        {
            var n = new ClimbNode { id = g.nodes.Count, pos = pos, kind = kind, biomeIndex = biomeIdx };
            g.nodes.Add(n);
            return n.id;
        }

        static void AddEdge(ClimbGraph g, int from, int to, EdgeType type, EdgeRole role,
            float cost, float climbLength, int jumps, int seg)
        {
            g.edges.Add(new ClimbEdge
            {
                from = from, to = to, type = type, role = role,
                cost = cost, climbLength = climbLength, jumpCount = jumps, segmentIndex = seg
            });
        }
    }
}
