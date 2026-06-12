using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 分层地图生成 · 第 3 层：攀爬路网生成器 v0.2（地形驱动寻路）
//
// 与 v0.1 螺旋版的根本区别：路线不再是凭空画的螺旋，而是
// 在「攀爬代价场」上从登山口到峰顶找最省力路径 → 路线天生
// 顺着可攀地形（缓坡/山脊/台地）走，并在被屏障挡住、绕行
// 太远时主动"挂辅助过去"。
//
// 流程：
//   1. 建 ClimbCostField（坡度+地貌分档：可走/攀爬/屏障）
//   2. 选 N 个不同方位的登山口，逐条 Dijkstra 到峰顶
//      —— 每条路给已用格加分离惩罚，逼后续路线另辟蹊径
//   3. 路径转节点：屏障跨越处=必经辅助(Aid)，按累积体力放营火
//   4. 峰间藤蔓：石峰/高点配对，净空允许就挂藤蔓(可选捆绑)
//   5. 相邻路线在相近高度、净空允许处加可选捆绑(CrossLink)
//
// 连接实体的"为什么在这"由 RouteEdge.barrier 记录，第4层据此摆形态。
// ============================================================

namespace MapGen
{
    [RequireComponent(typeof(MacroTerrainGenerator))]
    public class ClimbRouteGenerator : MonoBehaviour
    {
        [Header("种子")]
        public int seed = 2024;

        [Header("路线")]
        [Tooltip("登顶主路线条数")]
        [Range(2, 6)] public int routeCount = 3;
        [Tooltip("路径点之间的目标间距（米）。越小路线点越密")]
        [Range(8f, 40f)] public float nodeSpacing = 18f;
        [Tooltip("路线分离惩罚：已被别条路占用的格加多少代价，越大各路越分散")]
        [Range(0f, 200f)] public float separationPenalty = 60f;
        [Tooltip("分离惩罚的扩散半径（格）")]
        [Range(1, 8)] public int separationRadius = 3;

        [Header("代价场")]
        [Tooltip("导航网格分辨率（独立于地形渲染分辨率，越高寻路越精细越慢）")]
        [Range(48, 192)] public int navResolution = 96;
        [Tooltip("登山口距山脚边缘的内缩（占半径比例）")]
        [Range(0.7f, 1f)] public float trailheadRadiusFrac = 0.9f;

        [Header("营火")]
        [Tooltip("每多少体力代价放一个营火（越小营火越密）")]
        [Range(40f, 400f)] public float campfireEffortInterval = 140f;

        [Header("峰间藤蔓（可选捆绑）")]
        public bool generatePeakVines = true;
        [Tooltip("两个高点间多远以内才挂藤蔓（米）")]
        public float maxPeakVineDistance = 120f;
        [Tooltip("识别为'高点'的最低海拔比例")]
        [Range(0.3f, 1f)] public float peakMinAltitudeFrac = 0.55f;

        [Header("相邻路线捆绑（可选）")]
        public bool generateCrossLinks = true;
        [Tooltip("捆绑连接的最大水平距离（米）")]
        public float maxCrossLinkDistance = 80f;
        [Range(0, 4)] public int crossLinksPerPair = 1;

        [Header("净空检查")]
        [Tooltip("连接跨度离山面至少留多少米（低于此判定为穿山，不连）")]
        public float clearanceMargin = 1.5f;

        // 产物
        [System.NonSerialized] public RouteNetwork network;

        MacroTerrainGenerator macro;
        TerrainFeatureGenerator features;
        ClimbCostField field;

        static readonly Color[] RouteColors = {
            new Color(0.25f,0.55f,0.95f), new Color(0.53f,0.46f,0.72f),
            new Color(0.85f,0.45f,0.30f), new Color(0.30f,0.75f,0.55f),
            new Color(0.90f,0.55f,0.65f), new Color(0.60f,0.70f,0.30f),
        };

        public void Generate()
        {
            macro = GetComponent<MacroTerrainGenerator>();
            if (macro == null) { Debug.LogWarning("[第3层] 需要第1层 MacroTerrainGenerator。"); return; }
            features = GetComponent<TerrainFeatureGenerator>();

            var rng = new System.Random(seed);
            network = new RouteNetwork { seed = seed };

            // 1) 建代价场
            field = new ClimbCostField(SampleGround, macro.terrainSize, macro.mountainHeight, navResolution);
            field.Build();

            // 峰顶节点（所有路线共用）
            Vector3 peak = PeakWorld();
            int summitId = AddNode(peak, RouteNodeKind.Summit, -1);
            network.summitId = summitId;
            Vector2Int goalCell = field.WorldToCell(peak.x, peak.z);

            // 路线分离惩罚场
            int nav = navResolution;
            var extra = new float[nav * nav];

            // 2) 逐条路线寻路
            float startAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
            for (int r = 0; r < routeCount; r++)
            {
                float ang = startAngle + r * Mathf.PI * 2f / routeCount;
                Vector2Int startCell = TrailheadCell(ang);
                var path = field.FindPath(startCell, goalCell, extra);
                if (path == null || path.Count < 2)
                {
                    Debug.LogWarning($"[第3层] 路线{r + 1} 无解（登山口可能在屏障里），跳过。");
                    continue;
                }
                BuildRouteFromPath(r, path, summitId);
                ApplySeparationPenalty(path, extra);
            }

            // 3) 峰间藤蔓
            if (generatePeakVines) BuildPeakVines(rng);
            // 4) 相邻路线捆绑
            if (generateCrossLinks) BuildCrossLinks(rng);

            int aidCount = 0, linkCount = 0;
            foreach (var e in network.edges)
            { if (e.role == EdgeRole.Aid) aidCount++; else if (e.role == EdgeRole.CrossLink) linkCount++; }
            Debug.Log($"[第3层·地形驱动寻路] {network.routes.Count} 条登顶路线，" +
                      $"{network.nodes.Count} 节点；必经辅助 {aidCount} 处（崖/谷），可选捆绑 {linkCount} 处。");
        }

        // ---------------- 路径 → 路网 ----------------
        void BuildRouteFromPath(int routeIndex, List<PathStep> path, int summitId)
        {
            var route = new ClimbRoute
            {
                index = routeIndex,
                name = $"路线{routeIndex + 1}",
                difficulty = (RouteDifficulty)(routeIndex % 3),
                color = RouteColors[routeIndex % RouteColors.Length],
            };

            // 决定哪些 path 索引落成节点：起点、终点、所有辅助跨越的两端、按间距取样
            int count = path.Count;
            var keep = new bool[count];
            keep[0] = keep[count - 1] = true;
            for (int i = 0; i < count; i++)
                if (path[i].viaAid) { keep[i] = true; if (i > 0) keep[i - 1] = true; }

            // 按距离取样普通点
            Vector3 last = field.CellToWorld(path[0].cell);
            float acc = 0f;
            for (int i = 1; i < count - 1; i++)
            {
                Vector3 p = field.CellToWorld(path[i].cell);
                acc += Vector3.Distance(last, p); last = p;
                if (acc >= nodeSpacing) { keep[i] = true; acc = 0f; }
            }

            int prevId = -1; int prevPathIdx = -1;
            float effortSinceCamp = 0f;
            for (int i = 0; i < count; i++)
            {
                if (!keep[i]) continue;
                Vector3 pos = field.CellToWorld(path[i].cell);

                RouteNodeKind kind;
                if (i == 0) kind = RouteNodeKind.Trailhead;
                else if (path[i].viaAid) kind = RouteNodeKind.Junction; // 辅助落点=交叉口
                else kind = RouteNodeKind.Waypoint;

                int id = AddNode(pos, kind, routeIndex);
                SampleNearbyFeature(network.GetNode(id));
                route.nodeIds.Add(id);

                if (prevId >= 0)
                {
                    // 这一段是不是辅助跨越？看区间内有没有 viaAid 步
                    bool aidSeg = false; BarrierKind bk = BarrierKind.None; float vg = 0f;
                    for (int k = prevPathIdx + 1; k <= i; k++)
                        if (path[k].viaAid) { aidSeg = true; bk = path[k].barrier; vg = path[k].verticalGain; break; }

                    if (aidSeg)
                    {
                        var node = network.GetNode(id);
                        if (node.kind == RouteNodeKind.Waypoint) node.kind = RouteNodeKind.Junction;
                        AddAidEdge(prevId, id, bk, vg, route.difficulty);
                    }
                    else
                    {
                        var ea = AddMainEdge(prevId, id, route.difficulty);
                        effortSinceCamp += ea.effort;
                        // 累计体力够了，把这个落脚点升级成营火
                        if (effortSinceCamp >= campfireEffortInterval &&
                            network.GetNode(id).kind == RouteNodeKind.Waypoint)
                        { network.GetNode(id).kind = RouteNodeKind.Campfire; effortSinceCamp = 0f; }
                    }
                }
                prevId = id; prevPathIdx = i;
            }

            // 末点连峰顶
            if (prevId >= 0 && prevId != summitId)
                AddMainEdge(prevId, summitId, route.difficulty);
            if (!route.nodeIds.Contains(summitId)) route.nodeIds.Add(summitId);

            network.routes.Add(route);
        }

        void ApplySeparationPenalty(List<PathStep> path, float[] extra)
        {
            if (separationPenalty <= 0f) return;
            int nav = navResolution;
            foreach (var step in path)
            {
                int cx = step.cell % nav, cz = step.cell / nav;
                for (int dz = -separationRadius; dz <= separationRadius; dz++)
                    for (int dx = -separationRadius; dx <= separationRadius; dx++)
                    {
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nx >= nav || nz < 0 || nz >= nav) continue;
                        float falloff = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / (separationRadius + 1));
                        extra[nz * nav + nx] += separationPenalty * falloff;
                    }
            }
        }

        // ---------------- 峰间藤蔓 ----------------
        void BuildPeakVines(System.Random rng)
        {
            // 收集高点：第2层石峰记录 + 路网里的高海拔节点
            var peaks = new List<Vector3>();
            if (features != null && features.records != null)
                foreach (var rec in features.records)
                    if (rec.type == TerrainFeatureType.SpirePeak)
                        peaks.Add(new Vector3(rec.worldPosition.x,
                            SampleGround(rec.worldPosition.x, rec.worldPosition.z), rec.worldPosition.z));

            float minAlt = macro.mountainHeight * peakMinAltitudeFrac;
            // 也把路线上的局部高点纳入（避免只有石峰才有藤蔓）
            foreach (var nd in network.nodes)
                if (nd.kind != RouteNodeKind.Summit && nd.pos.y >= minAlt) peaks.Add(nd.pos);

            // 配对：近且净空允许，每个高点最多连一次，避免藤蔓爆炸
            var used = new HashSet<int>();
            for (int i = 0; i < peaks.Count; i++)
            {
                if (used.Contains(i)) continue;
                int best = -1; float bestD = maxPeakVineDistance;
                for (int j = i + 1; j < peaks.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    float d = Vector3.Distance(peaks[i], peaks[j]);
                    if (d < bestD && d > 20f && SpanClears(peaks[i], peaks[j]))
                    { bestD = d; best = j; }
                }
                if (best < 0) continue;
                used.Add(i); used.Add(best);
                int a = AddNode(peaks[i], RouteNodeKind.Junction, -1);
                int b = AddNode(peaks[best], RouteNodeKind.Junction, -1);
                AddLinkEdge(a, b, LinkKind.Vine, BarrierKind.PeakGap, RouteDifficulty.Hard);
            }
        }

        // ---------------- 相邻路线捆绑 ----------------
        void BuildCrossLinks(System.Random rng)
        {
            var routes = network.routes;
            for (int a = 0; a < routes.Count; a++)
            {
                int b = (a + 1) % routes.Count;
                if (routes.Count == 2 && a == 1) break;
                int made = 0, tries = 0;
                while (made < crossLinksPerPair && tries++ < 40)
                {
                    int na = PickMidNode(routes[a], rng);
                    int nb = PickMidNode(routes[b], rng);
                    if (na < 0 || nb < 0) continue;
                    var pa = network.GetNode(na); var pb = network.GetNode(nb);
                    if (Mathf.Abs(pa.pos.y - pb.pos.y) > 22f) continue;
                    float horiz = Vector2.Distance(new Vector2(pa.pos.x, pa.pos.z), new Vector2(pb.pos.x, pb.pos.z));
                    if (horiz > maxCrossLinkDistance) continue;
                    if (!SpanClears(pa.pos, pb.pos)) continue; // 穿山的不连

                    LinkKind lk = horiz > 50f ? LinkKind.Vine : LinkKind.Ledge;
                    if (pa.kind == RouteNodeKind.Waypoint) pa.kind = RouteNodeKind.Junction;
                    if (pb.kind == RouteNodeKind.Waypoint) pb.kind = RouteNodeKind.Junction;
                    AddLinkEdge(na, nb, lk, BarrierKind.None, RouteDifficulty.Medium);
                    made++;
                }
            }
        }

        int PickMidNode(ClimbRoute route, System.Random rng)
        {
            int lo = 2, hi = route.nodeIds.Count - 3;
            if (hi <= lo) return -1;
            return route.nodeIds[rng.Next(lo, hi + 1)];
        }

        /// <summary>净空检查：沿跨度采样地表，连接弧若处处高于地表+余量则放行。</summary>
        bool SpanClears(Vector3 a, Vector3 b)
        {
            int samples = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / 6f), 4, 40);
            for (int i = 1; i < samples; i++)
            {
                float t = i / (float)samples;
                Vector3 p = Vector3.Lerp(a, b, t);
                // 连接弧近似取两端连线（藤蔓下垂会更低，这里保守用直线高度）
                float ground = SampleGround(p.x, p.z);
                if (p.y < ground + clearanceMargin) return false;
            }
            return true;
        }

        // ---------------- 地貌采样 ----------------
        void SampleNearbyFeature(RouteNode node)
        {
            node.hasNearbyFeature = false;
            if (features == null || features.records == null) return;
            float best = float.MaxValue;
            foreach (var p in features.records)
            {
                if (p == null) continue;
                float d = Vector3.Distance(p.worldPosition, node.pos);
                if (d < best && d < 60f) { best = d; node.nearbyFeature = p.type; node.hasNearbyFeature = true; }
            }
            node.bandIndex = Mathf.Clamp(
                Mathf.FloorToInt(node.pos.y / Mathf.Max(1f, macro.mountainHeight) * 3f), 0, 2);
        }

        // ---------------- 工具 ----------------
        Vector2Int TrailheadCell(float angle)
        {
            float px = macro.peakPosition.x * macro.terrainSize;
            float pz = macro.peakPosition.y * macro.terrainSize;
            float radius = macro.mountainRadius * trailheadRadiusFrac;
            float wx = Mathf.Clamp(px + Mathf.Cos(angle) * radius, 0f, macro.terrainSize);
            float wz = Mathf.Clamp(pz + Mathf.Sin(angle) * radius, 0f, macro.terrainSize);
            return field.WorldToCell(wx, wz);
        }

        Vector3 PeakWorld()
        {
            float px = macro.peakPosition.x * macro.terrainSize;
            float pz = macro.peakPosition.y * macro.terrainSize;
            return new Vector3(px, SampleGround(px, pz), pz);
        }

        float SampleGround(float wx, float wz)
        {
            if (features != null) return features.SampleHeightWithFeatures(wx, wz);
            return macro.SampleHeight(wx, wz);
        }

        int AddNode(Vector3 pos, RouteNodeKind kind, int routeIndex)
        {
            var n = new RouteNode { id = network.nodes.Count, pos = pos, kind = kind, routeIndex = routeIndex };
            network.nodes.Add(n);
            return n.id;
        }

        RouteEdge AddMainEdge(int from, int to, RouteDifficulty diff)
        {
            var a = network.GetNode(from).pos; var b = network.GetNode(to).pos;
            float len = Vector3.Distance(a, b);
            float vGain = Mathf.Max(0f, b.y - a.y);
            var e = new RouteEdge
            {
                from = from, to = to, isLink = false, role = EdgeRole.MainPath,
                barrier = BarrierKind.None, difficulty = diff, length = len,
                effort = vGain * 2.5f + len * 0.3f, // 爬升为主、水平少量
            };
            network.edges.Add(e);
            return e;
        }

        void AddAidEdge(int from, int to, BarrierKind barrier, float vGain, RouteDifficulty diff)
        {
            var a = network.GetNode(from).pos; var b = network.GetNode(to).pos;
            LinkKind lk = barrier switch
            {
                BarrierKind.Canyon => LinkKind.Bridge,
                BarrierKind.CavePass => LinkKind.Cave,
                _ => Mathf.Abs(vGain) > 18f ? LinkKind.Vine : LinkKind.Ledge, // 崖壁：高落差挂藤蔓，否则岩架
            };
            network.edges.Add(new RouteEdge
            {
                from = from, to = to, isLink = true, role = EdgeRole.Aid,
                linkKind = lk, barrier = barrier, difficulty = diff,
                length = Vector3.Distance(a, b),
                effort = Mathf.Abs(vGain) * 3f + 20f, // 辅助跨越费力
            });
        }

        void AddLinkEdge(int from, int to, LinkKind lk, BarrierKind barrier, RouteDifficulty diff)
        {
            var a = network.GetNode(from).pos; var b = network.GetNode(to).pos;
            network.edges.Add(new RouteEdge
            {
                from = from, to = to, isLink = true, role = EdgeRole.CrossLink,
                linkKind = lk, barrier = barrier, difficulty = diff,
                length = Vector3.Distance(a, b), effort = 15f,
            });
        }
    }
}
