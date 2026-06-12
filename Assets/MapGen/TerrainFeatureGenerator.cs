using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 分层地图生成 · 第 2 层：地貌生成器 v0.4（深层修法 · 十种全实现）
//
// 与占位版的区别：地貌真正修改山体网格高度，不再生成占位块实体。
//   - 维护一张 delta 高度场，每种地貌把高度修改累加进去
//   - 用 第1层毛坯 + delta 重建网格（石峰凸起、崖壁竖起、台地削平）
//   - 保留每个地貌的名称标签(Scene飘字) + 数据记录(FeatureRecord)
//   - 暴露 SampleHeightWithFeatures()，第3层路线贴地改调它 → 自然贴合地貌
//
// 十种地貌全部修改高度场网格。其中：
//   - 悬垂/洞口受高度场限制（单xz单高度），做近似形 + 标记，
//     真正的外凸岩檐/洞穴内腔留给后续模型摆放(第2层标记)与第5层洞穴系统。
//   - 巨石做成成片石堆隆起，真实圆石靠按记录摆模型。
// ============================================================

namespace MapGen
{
    [RequireComponent(typeof(MacroTerrainGenerator))]
    public class TerrainFeatureGenerator : MonoBehaviour
    {
        [Header("种子")]
        public int seed = 777;

        [Header("海拔层带（按从低到高排列）")]
        public List<TerrainBand> bands = new List<TerrainBand>();

        [Header("散布")]
        [Tooltip("特征点之间的最小间距（米）")]
        public float minFeatureSpacing = 60f;
        [Tooltip("最多生成多少个地貌")]
        public int maxFeatures = 35;

        [Header("显示")]
        [Tooltip("是否显示地貌名称标签（Scene飘字，选中本物体时）")]
        public bool showLabels = true;
        public Color terrainColor = new Color(0.52f, 0.57f, 0.43f);

        // 产物
        [System.NonSerialized] public List<FeatureRecord> records = new List<FeatureRecord>();
        [System.NonSerialized] public List<EnvironmentMarker> markers = new List<EnvironmentMarker>();

        MacroTerrainGenerator macro;
        const string MeshChildName = "_FeatureTerrain";

        // delta 高度场（叠加在第1层之上）
        float[] heightDelta;
        int res;
        float step;
        bool hasGenerated;

        public void Generate()
        {
            Clear();
            macro = GetComponent<MacroTerrainGenerator>();
            if (macro == null) { Debug.LogWarning("[第2层] 需要第1层 MacroTerrainGenerator。"); return; }

            res = macro.resolution;
            step = macro.terrainSize / (res - 1);
            heightDelta = new float[res * res];
            records.Clear();
            markers.Clear();

            var rng = new System.Random(seed);
            var points = ScatterPoints(rng);

            foreach (var p in points)
            {
                float baseH = macro.SampleHeight(p.x, p.y);
                float normAlt = Mathf.Clamp01(baseH / Mathf.Max(1f, macro.mountainHeight));
                int bi = BandIndexAt(normAlt);
                if (bi < 0) continue;
                var band = bands[bi];
                var type = PickFeature(band, rng);
                ApplyFeature(type, p, band, bi, rng);
            }

            // 先置位：BuildMesh 经 SampleHeightWithFeatures 采样，需要 delta 已生效
            hasGenerated = true;
            BuildMesh();
            Debug.Log($"[第2层·深层修法] 叠加 {records.Count} 个地貌到山体网格，" +
                      $"打了 {markers.Count} 个环境标记。第3层贴地请调 SampleHeightWithFeatures。");
        }

        public void Clear()
        {
            var t = transform.Find(MeshChildName);
            if (t != null)
            {
                if (Application.isPlaying) Destroy(t.gameObject);
                else DestroyImmediate(t.gameObject);
            }
            // 恢复显示第1层毛坯
            var macroMesh = transform.Find("_MacroTerrain");
            if (macroMesh != null) macroMesh.gameObject.SetActive(true);
            hasGenerated = false;
        }

        /// <summary>第3层贴地用：第1层毛坯高度 + 第2层地貌修改。双线性采样 delta 场。</summary>
        public float SampleHeightWithFeatures(float wx, float wz)
        {
            float baseH = macro != null ? macro.SampleHeight(wx, wz)
                                        : GetComponent<MacroTerrainGenerator>().SampleHeight(wx, wz);
            if (!hasGenerated || heightDelta == null) return baseH;

            float fx = wx / step, fz = wz / step;
            int x0 = Mathf.Clamp(Mathf.FloorToInt(fx), 0, res - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(fz), 0, res - 1);
            int x1 = Mathf.Min(x0 + 1, res - 1);
            int z1 = Mathf.Min(z0 + 1, res - 1);
            float tx = Mathf.Clamp01(fx - x0), tz = Mathf.Clamp01(fz - z0);
            float d00 = heightDelta[z0 * res + x0], d10 = heightDelta[z0 * res + x1];
            float d01 = heightDelta[z1 * res + x0], d11 = heightDelta[z1 * res + x1];
            float d = Mathf.Lerp(Mathf.Lerp(d00, d10, tx), Mathf.Lerp(d01, d11, tx), tz);
            return baseH + d;
        }

        // ---------------- 散布 ----------------
        List<Vector2> ScatterPoints(System.Random rng)
        {
            var pts = new List<Vector2>();
            float size = macro.terrainSize;
            int tries = maxFeatures * 30;
            for (int i = 0; i < tries && pts.Count < maxFeatures; i++)
            {
                Vector2 c = new Vector2((float)rng.NextDouble() * size, (float)rng.NextDouble() * size);
                bool ok = true;
                foreach (var e in pts)
                    if (Vector2.Distance(c, e) < minFeatureSpacing) { ok = false; break; }
                if (ok) pts.Add(c);
            }
            return pts;
        }

        int BandIndexAt(float normAlt)
        {
            for (int i = 0; i < bands.Count; i++)
                if (normAlt >= bands[i].altitudeMin && normAlt < bands[i].altitudeMax) return i;
            return bands.Count > 0 ? bands.Count - 1 : -1;
        }

        TerrainFeatureType PickFeature(TerrainBand band, System.Random rng)
        {
            float total = 0f;
            foreach (var w in band.featureWeights) total += w.weight;
            if (total <= 0f) return TerrainFeatureType.GentleSlope;
            float r = (float)rng.NextDouble() * total;
            foreach (var w in band.featureWeights)
            {
                r -= w.weight;
                if (r <= 0f) return w.type;
            }
            return band.featureWeights[band.featureWeights.Count - 1].type;
        }

        // ---------------- 地貌叠加（十种全部修改高度场网格） ----------------
        void ApplyFeature(TerrainFeatureType type, Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            switch (type)
            {
                case TerrainFeatureType.SpirePeak:     ApplySpire(c, band, bi, rng); break;
                case TerrainFeatureType.VerticalCliff: ApplyCliff(c, band, bi, rng); break;
                case TerrainFeatureType.Plateau:       ApplyPlateau(c, band, bi, rng); break;
                case TerrainFeatureType.SharpRidge:    ApplyRidge(c, band, bi, rng); break;
                case TerrainFeatureType.Canyon:        ApplyCanyon(c, band, bi, rng); break;
                case TerrainFeatureType.Overhang:      ApplyOverhang(c, band, bi, rng); break;
                case TerrainFeatureType.StepTerrace:   ApplyStepTerrace(c, band, bi, rng); break;
                case TerrainFeatureType.Boulders:      ApplyBoulders(c, band, bi, rng); break;
                case TerrainFeatureType.Cave:          ApplyCave(c, band, bi, rng); break;
                case TerrainFeatureType.GentleSlope:   ApplyGentleSlope(c, band, bi, rng); break;
                default:
                    Record(type, new Vector3(c.x, macro.SampleHeight(c.x, c.y), c.y),
                           Vector3.up, Vector3.one * 20f, band, bi, rng);
                    break;
            }
        }

        /// <summary>独立石峰：陡峭尖峰从山面拔起。</summary>
        void ApplySpire(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float radius = 22f + (float)rng.NextDouble() * 18f;
            float height = (55f + (float)rng.NextDouble() * 75f) * band.intensity;
            ForEachVertexInRadius(c, radius, (gx, gz, wx, wz, dist) =>
            {
                float t = 1f - dist / radius;
                heightDelta[gz * res + gx] += height * Mathf.Pow(Mathf.Clamp01(t), 2.4f);
            });
            Record(TerrainFeatureType.SpirePeak,
                new Vector3(c.x, macro.SampleHeight(c.x, c.y), c.y),
                Vector3.up, new Vector3(radius * 2f, height, radius * 2f), band, bi, rng);
        }

        /// <summary>垂直崖壁：沿一条线一侧抬起陡壁，打崖壁标记。</summary>
        void ApplyCliff(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float len = 55f + (float)rng.NextDouble() * 45f;
            float rise = (45f + (float)rng.NextDouble() * 50f) * band.intensity;
            float ang = (float)rng.NextDouble() * Mathf.PI;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 nrm = new Vector2(-dir.y, dir.x);

            ForEachVertexInRadius(c, len, (gx, gz, wx, wz, dist) =>
            {
                Vector2 rel = new Vector2(wx - c.x, wz - c.y);
                float along = Vector2.Dot(rel, dir);
                float across = Vector2.Dot(rel, nrm);
                if (Mathf.Abs(along) > len * 0.5f) return;
                // across 从 -带宽 到 +带宽 平滑抬升，形成陡壁；窄过渡=近垂直
                float band2 = 12f;
                float tt = Mathf.Clamp01((across + band2) / (band2 * 2f));
                heightDelta[gz * res + gx] += rise * Mathf.SmoothStep(0f, 1f, tt);
            });
            Vector3 cliffPos = new Vector3(c.x, macro.SampleHeight(c.x, c.y), c.y);
            Vector3 facing = new Vector3(nrm.x, 0f, nrm.y);
            Record(TerrainFeatureType.VerticalCliff, cliffPos, facing,
                new Vector3(len, rise, 12f), band, bi, rng);
            markers.Add(new EnvironmentMarker
            {
                kind = EnvironmentMarker.Kind.CliffFace,
                position = cliffPos, facing = facing, magnitude = rise,
                sourceFeature = TerrainFeatureType.VerticalCliff
            });
        }

        /// <summary>台地：一片区域削/垫成平台，打平坦休息标记。</summary>
        void ApplyPlateau(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float radius = 32f + (float)rng.NextDouble() * 25f;
            float baseH = macro.SampleHeight(c.x, c.y);
            float top = baseH + (8f + (float)rng.NextDouble() * 16f) * band.intensity;
            ForEachVertexInRadius(c, radius, (gx, gz, wx, wz, dist) =>
            {
                float current = macro.SampleHeight(wx, wz) + heightDelta[gz * res + gx];
                float t = Mathf.SmoothStep(1f, 0f, dist / radius);
                float target = Mathf.Lerp(current, top, t);
                heightDelta[gz * res + gx] += (target - current);
            });
            Vector3 pos = new Vector3(c.x, top, c.y);
            Record(TerrainFeatureType.Plateau, pos, Vector3.up,
                new Vector3(radius * 2f, top - baseH, radius * 2f), band, bi, rng);
            markers.Add(new EnvironmentMarker
            {
                kind = EnvironmentMarker.Kind.FlatRest,
                position = pos, facing = Vector3.up, magnitude = radius,
                sourceFeature = TerrainFeatureType.Plateau
            });
        }

        /// <summary>尖锐岩脊：沿一条线抬起刀刃状山脊，两侧陡降，脊顶尖。</summary>
        void ApplyRidge(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float len = 60f + (float)rng.NextDouble() * 60f;
            float halfWidth = 14f + (float)rng.NextDouble() * 10f;
            float rise = (40f + (float)rng.NextDouble() * 55f) * band.intensity;
            float ang = (float)rng.NextDouble() * Mathf.PI;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 nrm = new Vector2(-dir.y, dir.x);
            float reach = Mathf.Max(len * 0.5f, halfWidth);

            ForEachVertexInRadius(c, reach, (gx, gz, wx, wz, dist) =>
            {
                Vector2 rel = new Vector2(wx - c.x, wz - c.y);
                float along = Vector2.Dot(rel, dir);
                float across = Vector2.Dot(rel, nrm);
                if (Mathf.Abs(along) > len * 0.5f) return;
                float acrossT = Mathf.Clamp01(Mathf.Abs(across) / halfWidth);
                // 沿脊线两端渐隐，横向用尖三角剖面（脊顶=1，两侧=0）
                float alongFade = 1f - Mathf.Abs(along) / (len * 0.5f);
                float crest = (1f - acrossT);            // 三角剖面 → 尖锐
                heightDelta[gz * res + gx] += rise * crest * crest * alongFade;
            });
            Vector3 pos = new Vector3(c.x, macro.SampleHeight(c.x, c.y), c.y);
            Record(TerrainFeatureType.SharpRidge, pos, new Vector3(dir.x, 0f, dir.y),
                new Vector3(len, rise, halfWidth * 2f), band, bi, rng);
        }

        /// <summary>峡谷裂隙：沿一条线下切出深沟，打蓄水标记（峡谷底可蓄水）。</summary>
        void ApplyCanyon(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float len = 70f + (float)rng.NextDouble() * 70f;
            float halfWidth = 12f + (float)rng.NextDouble() * 10f;
            float depth = (28f + (float)rng.NextDouble() * 40f) * band.intensity;
            float ang = (float)rng.NextDouble() * Mathf.PI;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 nrm = new Vector2(-dir.y, dir.x);
            float reach = Mathf.Max(len * 0.5f, halfWidth);

            ForEachVertexInRadius(c, reach, (gx, gz, wx, wz, dist) =>
            {
                Vector2 rel = new Vector2(wx - c.x, wz - c.y);
                float along = Vector2.Dot(rel, dir);
                float across = Vector2.Dot(rel, nrm);
                if (Mathf.Abs(along) > len * 0.5f) return;
                float acrossT = Mathf.Clamp01(Mathf.Abs(across) / halfWidth);
                float alongFade = 1f - Mathf.Abs(along) / (len * 0.5f);
                // U 形沟：中央最深、两侧抬回；SmoothStep 让谷壁平滑
                float profile = Mathf.SmoothStep(1f, 0f, acrossT);
                heightDelta[gz * res + gx] -= depth * profile * alongFade;
            });
            Vector3 pos = new Vector3(c.x, macro.SampleHeight(c.x, c.y) - depth * 0.5f, c.y);
            Record(TerrainFeatureType.Canyon, pos, new Vector3(dir.x, 0f, dir.y),
                new Vector3(len, depth, halfWidth * 2f), band, bi, rng);
            markers.Add(new EnvironmentMarker
            {
                kind = EnvironmentMarker.Kind.WaterBasin,
                position = pos, facing = Vector3.up, magnitude = depth,
                sourceFeature = TerrainFeatureType.Canyon
            });
        }

        /// <summary>
        /// 悬垂反斜：高度场无法表达真正外凸岩檐（一个xz对应多高度）。
        /// 这里做成比崖壁更陡更窄的近垂直壁 + CliffFace 标记，
        /// 真正的外凸几何留给后续按标记摆放岩檐模型。
        /// </summary>
        void ApplyOverhang(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float len = 40f + (float)rng.NextDouble() * 35f;
            float rise = (50f + (float)rng.NextDouble() * 55f) * band.intensity;
            float ang = (float)rng.NextDouble() * Mathf.PI;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 nrm = new Vector2(-dir.y, dir.x);

            ForEachVertexInRadius(c, len, (gx, gz, wx, wz, dist) =>
            {
                Vector2 rel = new Vector2(wx - c.x, wz - c.y);
                float along = Vector2.Dot(rel, dir);
                float across = Vector2.Dot(rel, nrm);
                if (Mathf.Abs(along) > len * 0.5f) return;
                // 极窄过渡带 → 比崖壁更接近垂直
                float band2 = 6f;
                float tt = Mathf.Clamp01((across + band2) / (band2 * 2f));
                heightDelta[gz * res + gx] += rise * Mathf.SmoothStep(0f, 1f, tt);
            });
            Vector3 pos = new Vector3(c.x, macro.SampleHeight(c.x, c.y), c.y);
            Vector3 facing = new Vector3(nrm.x, 0f, nrm.y);
            Record(TerrainFeatureType.Overhang, pos, facing,
                new Vector3(len, rise, 8f), band, bi, rng);
            markers.Add(new EnvironmentMarker
            {
                kind = EnvironmentMarker.Kind.CliffFace,
                position = pos, facing = facing, magnitude = rise,
                sourceFeature = TerrainFeatureType.Overhang
            });
        }

        /// <summary>阶梯岩台：把一片区域量化成几层错落的台阶（天然楼梯）。</summary>
        void ApplyStepTerrace(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float radius = 34f + (float)rng.NextDouble() * 24f;
            int steps = 3 + rng.Next(0, 3);                 // 3~5 级
            float totalRise = (18f + (float)rng.NextDouble() * 22f) * band.intensity;
            float stepH = totalRise / steps;
            float baseH = macro.SampleHeight(c.x, c.y);

            ForEachVertexInRadius(c, radius, (gx, gz, wx, wz, dist) =>
            {
                float t = Mathf.Clamp01(dist / radius);     // 0=中心 1=边缘
                // 中心最高，向外一级级跌落：量化成整数台阶
                int level = Mathf.Clamp(Mathf.FloorToInt((1f - t) * steps), 0, steps);
                float target = baseH + level * stepH;
                float current = macro.SampleHeight(wx, wz) + heightDelta[gz * res + gx];
                heightDelta[gz * res + gx] += (target - current);
            });
            Vector3 pos = new Vector3(c.x, baseH + totalRise, c.y);
            Record(TerrainFeatureType.StepTerrace, pos, Vector3.up,
                new Vector3(radius * 2f, totalRise, radius * 2f), band, bi, rng);
        }

        /// <summary>
        /// 巨石散布：128 分辨率下单块小石看不出，做成成片的圆顶石堆隆起。
        /// 真实散落圆石靠后续按本记录摆模型。
        /// </summary>
        void ApplyBoulders(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            int count = 3 + rng.Next(0, 4);                 // 3~6 块堆一起
            float fieldR = 26f + (float)rng.NextDouble() * 18f;
            for (int k = 0; k < count; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * fieldR;
                Vector2 bc = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                float br = 6f + (float)rng.NextDouble() * 7f;
                float bh = (5f + (float)rng.NextDouble() * 8f) * band.intensity;
                ForEachVertexInRadius(bc, br, (gx, gz, wx, wz, dist) =>
                {
                    // 半球形隆起（圆滚滚），不尖
                    float t = Mathf.Clamp01(dist / br);
                    heightDelta[gz * res + gx] += bh * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
                });
            }
            Vector3 pos = new Vector3(c.x, macro.SampleHeight(c.x, c.y), c.y);
            Record(TerrainFeatureType.Boulders, pos, Vector3.up,
                new Vector3(fieldR * 2f, 12f, fieldR * 2f), band, bi, rng);
        }

        /// <summary>
        /// 溶洞岩穴：高度场只能表达表面，这里在山面凹一个洞口龛 + 打 CaveMouth 标记。
        /// 内部通道由第5层洞穴系统做。洞口朝向取下坡方向（朝山外）。
        /// </summary>
        void ApplyCave(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float radius = 8f + (float)rng.NextDouble() * 6f;
            float depth = (6f + (float)rng.NextDouble() * 8f) * band.intensity;
            ForEachVertexInRadius(c, radius, (gx, gz, wx, wz, dist) =>
            {
                float t = Mathf.Clamp01(dist / radius);
                // 中央内凹成龛
                heightDelta[gz * res + gx] -= depth * Mathf.SmoothStep(1f, 0f, t);
            });
            Vector3 pos = new Vector3(c.x, macro.SampleHeight(c.x, c.y), c.y);
            Vector2 g2 = HeightGradient(c.x, c.y);          // 上坡方向
            Vector3 facing = (g2.sqrMagnitude > 0.0001f)
                ? new Vector3(-g2.x, 0f, -g2.y).normalized  // 洞口朝下坡（山外）
                : Vector3.up;
            Record(TerrainFeatureType.Cave, pos, facing,
                new Vector3(radius * 2f, depth, radius * 2f), band, bi, rng);
            markers.Add(new EnvironmentMarker
            {
                kind = EnvironmentMarker.Kind.CaveMouth,
                position = pos, facing = facing, magnitude = radius,
                sourceFeature = TerrainFeatureType.Cave
            });
        }

        /// <summary>缓坡草甸：把一片区域整平为平缓步行坡（削掉局部起伏），打休息标记。</summary>
        void ApplyGentleSlope(Vector2 c, TerrainBand band, int bi, System.Random rng)
        {
            float radius = 30f + (float)rng.NextDouble() * 22f;
            // 用中心高度 + 平缓梯度做目标面，向它收敛 → 抹平噪声起伏
            float baseH = macro.SampleHeight(c.x, c.y);
            Vector2 g2 = HeightGradient(c.x, c.y);
            ForEachVertexInRadius(c, radius, (gx, gz, wx, wz, dist) =>
            {
                float t = Mathf.SmoothStep(1f, 0f, dist / radius); // 中心强、边缘渐隐
                // 目标 = 中心高度沿缓坡线性外推（坡度压到原梯度的一半）
                float target = baseH + (g2.x * (wx - c.x) + g2.y * (wz - c.y)) * 0.5f;
                float current = macro.SampleHeight(wx, wz) + heightDelta[gz * res + gx];
                heightDelta[gz * res + gx] += (target - current) * t * band.intensity;
            });
            Vector3 pos = new Vector3(c.x, baseH, c.y);
            Record(TerrainFeatureType.GentleSlope, pos, Vector3.up,
                new Vector3(radius * 2f, 0f, radius * 2f), band, bi, rng);
            markers.Add(new EnvironmentMarker
            {
                kind = EnvironmentMarker.Kind.FlatRest,
                position = pos, facing = Vector3.up, magnitude = radius,
                sourceFeature = TerrainFeatureType.GentleSlope
            });
        }

        /// <summary>第1层毛坯在 (wx,wz) 处的水平梯度（指向上坡），用于洞口朝向/缓坡走向。</summary>
        Vector2 HeightGradient(float wx, float wz)
        {
            float e = step;
            float hx = macro.SampleHeight(wx + e, wz) - macro.SampleHeight(wx - e, wz);
            float hz = macro.SampleHeight(wx, wz + e) - macro.SampleHeight(wx, wz - e);
            return new Vector2(hx / (2f * e), hz / (2f * e));
        }

        void Record(TerrainFeatureType type, Vector3 pos, Vector3 facing, Vector3 size,
            TerrainBand band, int bi, System.Random rng)
        {
            records.Add(new FeatureRecord
            {
                type = type, worldPosition = pos, facing = facing, size = size,
                bandIndex = bi, bandName = band.bandName, featureSeed = rng.Next()
            });
        }

        // ---------------- 遍历半径内顶点 ----------------
        delegate void VertexOp(int gx, int gz, float wx, float wz, float dist);
        void ForEachVertexInRadius(Vector2 center, float radius, VertexOp op)
        {
            int minX = Mathf.Max(0, Mathf.FloorToInt((center.x - radius) / step));
            int maxX = Mathf.Min(res - 1, Mathf.CeilToInt((center.x + radius) / step));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((center.y - radius) / step));
            int maxZ = Mathf.Min(res - 1, Mathf.CeilToInt((center.y + radius) / step));
            for (int z = minZ; z <= maxZ; z++)
                for (int x = minX; x <= maxX; x++)
                {
                    float wx = x * step, wz = z * step;
                    float d = Vector2.Distance(new Vector2(wx, wz), center);
                    if (d <= radius) op(x, z, wx, wz, d);
                }
        }

        // ---------------- 重建网格（分块） ----------------
        void BuildMesh()
        {
            // 隐藏第1层毛坯块（第2层接管显示）
            var macroMesh = transform.Find("_MacroTerrain");
            if (macroMesh != null) macroMesh.gameObject.SetActive(false);

            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", terrainColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", terrainColor);
            mat.color = terrainColor;

            // 采样含地貌的高度 → 分块构建器（与第1层同一套分块逻辑、解析法线）
            var p = new ChunkBuildParams
            {
                sampleHeight = SampleHeightWithFeatures,
                resolution = res,
                terrainSize = macro.terrainSize,
                chunkVerts = macro.chunkVerts,
                material = mat,
                parent = transform,
                rootName = MeshChildName,
                meshNamePrefix = "Feature",
            };
            TerrainChunkBuilder.Build(in p);
        }
    }
}
