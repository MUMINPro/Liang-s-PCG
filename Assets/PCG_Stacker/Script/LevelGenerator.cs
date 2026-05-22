using System.Collections.Generic;
using UnityEngine;

namespace PCG
{
    /// <summary>
    /// 总协调。把所有模块串起来，对外提供"生成关卡"的入口。
    /// 不干具体计算，只负责调度。
    /// </summary>
    public class LevelGenerator : MonoBehaviour
    {
        [Header("落脚点库")]
        [Tooltip("可用落脚点的库（ScriptableObject 资源）")]
        public FootholdLibrary library;

        [Header("撒点参数")]
        public ScatterSettings scatterSettings = new ScatterSettings();

        [Header("起点")]
        [Tooltip("塔底中心位置（世界坐标）")]
        public Vector3 startPosition = Vector3.zero;

        [Header("随机源")]
        [Tooltip("勾选则用日期种子；取消则用下方手动种子")]
        public bool useDailySeed = true;

        [Tooltip("手动种子（仅当不使用日期种子时生效）")]
        public int manualSeed = 0;

        [Header("自动生成")]
        [Tooltip("勾选则游戏开始时自动生成一次")]
        public bool generateOnStart = true;

        private Transform _container;
        private readonly LayeredScatterGenerator _generator = new LayeredScatterGenerator();

        private void Start()
        {
            if (generateOnStart)
                GenerateLevel();
        }

        /// <summary>
        /// 生成关卡。可由 Start() 自动调用，或编辑器按钮手动触发。
        /// </summary>
        public void GenerateLevel()
        {
            if (library == null)
            {
                Debug.LogError("[LevelGenerator] 未指定 library，无法生成。");
                return;
            }

            ClearLevel();

            System.Random rng = useDailySeed
                ? DailySeed.CreateTodayRng()
                : DailySeed.CreateRng(manualSeed);

            int usedSeed = useDailySeed ? DailySeed.GetTodaySeed() : manualSeed;

            var containerGo = new GameObject("GeneratedLevel");
            containerGo.transform.SetParent(transform, false);
            _container = containerGo.transform;

            List<LayeredScatterGenerator.PlacedFoothold> placed =
                _generator.Generate(library, rng, scatterSettings, startPosition, _container);

            Debug.Log($"[LevelGenerator] 生成完毕：seed={usedSeed}，落脚点数={placed.Count}，层数={scatterSettings.layerCount}");
        }

        /// <summary>清除上一次生成的关卡。</summary>
        public void ClearLevel()
        {
            if (_container != null)
            {
                DestroyContainer(_container.gameObject);
                _container = null;
            }

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name == "GeneratedLevel")
                    DestroyContainer(child.gameObject);
            }
        }

        private static void DestroyContainer(GameObject go)
        {
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }
    }
}
