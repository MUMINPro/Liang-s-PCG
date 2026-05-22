using UnityEngine;

namespace PCG
{
    /// <summary>
    /// 落脚点数据（ScriptableObject）。描述"一种落脚点是什么"，是纯数据。
    ///
    /// 与旧 SegmentData 的区别：不再有 Entry/Exit 锚点概念。
    /// 落脚点的位置由撒点算法用坐标决定，不靠锚点对齐。
    ///
    /// 创建方式：Project 窗口右键 → Create → PCG → Foothold Data。
    /// </summary>
    [CreateAssetMenu(fileName = "NewFoothold", menuName = "PCG/Foothold Data")]
    public class FootholdData : ScriptableObject
    {
        [Tooltip("这个落脚点的预制体（一个小平台/岩点，需自带 Collider，墙面记得打 Wall 标签）")]
        public GameObject prefab;

        [Tooltip("抽取权重，越大越容易被选中")]
        [Min(0f)]
        public float weight = 1f;

        [Tooltip("难度等级（预留，配合高度做\"越往上越难\"）")]
        public int difficulty = 0;

        [Tooltip("落脚点名称（方便调试识别）")]
        public string footholdName = "Unnamed Foothold";
    }
}
