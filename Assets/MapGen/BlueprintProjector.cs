using System.Collections.Generic;
using UnityEngine;

// ============================================================
// 路线图蓝图投射器 v0.1
// 把抽象路线图"投射"到 3D 场景里，作为手工搭建地形时的参照层。
//
// 用法：
//   1. 场景里建一个空物体，挂上这个组件
//   2. 在 Inspector 指定配置资产 + 种子，点「投射蓝图」
//   3. 场景视口里出现一串占位标记和连线，照着它们手工搭地形
//   4. 标记只在 Scene 视图用 Gizmos 画，不进 Game 视图、不污染层级
//
// 这不是几何生成器——它只画参照线，地形由你手工建。
// 这正是 PEAK 式的工作流：工具给蓝图，人来雕。
// ============================================================

namespace MapGen
{
    [ExecuteAlways]
    public class BlueprintProjector : MonoBehaviour
    {
        [Header("数据来源")]
        public MapDesignConfig config;
        public int seed = 12345;

        [Header("空间映射")]
        [Tooltip("路线图的 1 单位 = 场景里多少米。路线图坐标本就是米，通常填 1")]
        public float worldScale = 1f;

        [Header("显示开关")]
        public bool showNodes = true;
        public bool showEdges = true;
        public bool showLabels = true;
        [Tooltip("只显示主路径，隐藏分支（搭建时减少干扰）")]
        public bool mainPathOnly = false;
        [Tooltip("标记球的基准大小")]
        public float markerScale = 1f;

        // 运行时缓存的图（不序列化，靠按钮重建）
        [System.NonSerialized] public ClimbGraph graph;

        public void Project()
        {
            if (config == null)
            {
                Debug.LogWarning("[蓝图投射器] 未指定配置资产。");
                return;
            }
            graph = ClimbGraphGenerator.Generate(config, seed);
            Debug.Log($"[蓝图投射器] 已投射种子 {seed}：" +
                      $"{graph.nodes.Count} 节点 / {graph.edges.Count} 边 / {graph.SegmentCount} 营火段。" +
                      "切到 Scene 视图查看。");
        }

        public void Clear()
        {
            graph = null;
        }

        /// <summary>把路线图节点的 3D 坐标转成世界坐标（以本物体为原点）。</summary>
        public Vector3 ToWorld(Vector3 p)
        {
            return transform.position + p * worldScale;
        }
    }
}
