using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 分层地图生成 · 模型摆放器 v0.1
// 读取第 2 层生成的占位块(FeatureProxy)，按地貌类型从模型库挑一个
// 实物模型(你的商城资源/精细模型)，对齐到占位块的位置、尺寸、朝向摆上去。
//
// 美术与布局解耦：换模型只改模型库，布局逻辑不动。
//
// 用法：
//   1. 同物体上挂本组件（需要先有第2层 TerrainFeatureGenerator 且已生成占位块）
//   2. 在 modelLibrary 里给每种地貌类型配 1~N 个 prefab
//   3. 点「摆放模型」——占位块隐藏，实物模型就位
// ============================================================

namespace MapGen
{
    /// <summary>一种地貌类型对应的模型库条目（可放多个 prefab，摆放时随机选）。</summary>
    [System.Serializable]
    public class FeatureModelEntry
    {
        public TerrainFeatureType type;
        [Tooltip("该地貌的候选模型（随机挑一个）。每个 prefab 的尺寸应大致接近 1 个单位，" +
                 "摆放时会按占位块尺寸缩放")]
        public List<GameObject> prefabs = new List<GameObject>();

        [Tooltip("是否按占位块尺寸缩放模型。关掉则用模型原始尺寸")]
        public bool scaleToProxy = true;

        [Tooltip("额外缩放系数（微调）")]
        public float scaleMultiplier = 1f;

        [Tooltip("是否随机绕竖直轴旋转（增加变化）")]
        public bool randomYaw = true;
    }

    [RequireComponent(typeof(TerrainFeatureGenerator))]
    public class FeatureModelPlacer : MonoBehaviour
    {
        [Header("模型库（每种地貌配模型）")]
        public List<FeatureModelEntry> modelLibrary = new List<FeatureModelEntry>();

        const string RootName = "_PlacedModels";

        public void Place()
        {
            Clear();
            var gen = GetComponent<TerrainFeatureGenerator>();
            var recs = gen.records;
            if (recs == null || recs.Count == 0)
            {
                Debug.LogWarning("[摆放] 没有地貌记录。先在第2层点「生成地貌」。");
                return;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(transform, false);

            int placed = 0, missing = 0;
            foreach (var fp in recs)
            {
                var entry = FindEntry(fp.type);
                if (entry == null || entry.prefabs.Count == 0) { missing++; continue; }

                var rng = new System.Random(fp.featureSeed);
                var prefab = entry.prefabs[rng.Next(entry.prefabs.Count)];
                if (prefab == null) { missing++; continue; }

                var inst = Instantiate(prefab, root.transform);
                inst.name = $"{fp.type}_{prefab.name}";
                inst.transform.position = fp.worldPosition;

                if (fp.facing != Vector3.up && fp.facing.sqrMagnitude > 0.001f)
                    inst.transform.rotation = Quaternion.LookRotation(fp.facing, Vector3.up);
                if (entry.randomYaw)
                    inst.transform.Rotate(0f, (float)rng.NextDouble() * 360f, 0f, Space.World);

                if (entry.scaleToProxy)
                {
                    var b = GetPrefabBounds(prefab);
                    if (b.size != Vector3.zero)
                    {
                        float sx = fp.size.x / Mathf.Max(0.01f, b.size.x);
                        float sy = fp.size.y / Mathf.Max(0.01f, b.size.y);
                        float sz = fp.size.z / Mathf.Max(0.01f, b.size.z);
                        inst.transform.localScale = new Vector3(sx, sy, sz) * entry.scaleMultiplier;
                    }
                }
                else
                {
                    inst.transform.localScale = prefab.transform.localScale * entry.scaleMultiplier;
                }
                placed++;
            }

            Debug.Log($"[摆放] 已摆放 {placed} 个模型，{missing} 个地貌无对应模型。");
        }

        public void Clear()
        {
            var t = transform.Find(RootName);
            if (t != null)
            {
                if (Application.isPlaying) Destroy(t.gameObject);
                else DestroyImmediate(t.gameObject);
            }
        }

        FeatureModelEntry FindEntry(TerrainFeatureType type)
        {
            foreach (var e in modelLibrary) if (e.type == type) return e;
            return null;
        }

        static Bounds GetPrefabBounds(GameObject prefab)
        {
            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
