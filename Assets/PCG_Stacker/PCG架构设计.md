# PCG 垂直攀爬地图生成系统 — 架构设计文档

> 本文档是整个程序化生成（PCG）系统的设计蓝图，用于：
> 1. 明确每个脚本的职责和它们之间的关系
> 2. 作为后续修改的索引——想改某个功能时，能快速定位到对应模块
> 3. 与 AI 协作时的共享上下文
>
> **维护约定**：每次架构发生变化（增删模块、改职责、改设计决策），都更新本文档对应章节。

---

## 一、项目目标

做一个 **Only Up 风格的第一人称垂直攀爬游戏**，核心特性：

- 地图由**手工制作的片段（Segment）**一段段堆叠生成，不是地形噪声生成
- **每天的地图不同，但当天所有玩家玩同一张**（日期种子）
- 玩家通过跳跃、二段跳、贴墙吸附蹬墙跳往上爬

> 注意：本系统是"模块化片段堆叠"，不是 Sebastian 那种"柏林噪声地形"。两者本质不同。

---

## 二、设计决策记录（重要）

这一节记录关键的设计选择。**想改方向时先看这里**，确认要改的是哪条决策，再去改对应模块。

| 编号 | 决策点 | 当前选择 | 备选方案（以后可能改） |
|------|--------|---------|----------------------|
| D1 | 片段数据存储形式 | **ScriptableObject**（每个片段一个独立资源文件） | 内联 `[System.Serializable]` 类 |
| D2 | 锚点定义方式 | 片段 Prefab 内放两个空物体 `Entry` / `Exit` | 用坐标/数据描述锚点 | 锚点需遵守朝向与水平偏移约定（Entry 朝下、Exit 朝上，XZ 水平错位不超过 ±N 米）|
| D3 | 生成数量 | **固定数量**（生成 N 段就停） | 无限生成 + 远处回收 |
| D4 | 堆叠方向 | **只往上直堆**（片段竖直摞，不旋转） | 支持旋转/拐弯 |
| D5 | 随机源 | `System.Random(seed)`，seed 来自日期 | 加入玩家可选 seed / 多套随机流 |

> 标 **加粗** 的是当前实现。改任何一条，都要回来更新这张表。

---

## 三、模块总览

系统遵循"职责分离"原则（参考 Sebastian Lague 的风格）：每个脚本只干一件事，互相低耦合。

```
SegmentData      ── 描述"一个片段是什么"
      ↓ 被收集进
SegmentLibrary   ── 管理"有哪些片段可选"，提供随机抽取
      ↓ 被使用
SegmentStacker   ── 核心：把片段一段段对齐、堆叠
      ↑ 用到
DailySeed        ── 提供"当天唯一"的随机源
      ↓ 整个流程由
LevelGenerator   ── 总协调：读种子 → 选片段 → 调拼接器
```

---

## 四、各模块详细设计

### 4.1 SegmentData（片段数据）

**职责**：描述一个片段"是什么"，是纯数据。

**形式**：ScriptableObject（决策 D1）——每个片段类型是一个独立的 `.asset` 资源文件，在 Project 窗口里创建和管理。

**包含的数据**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `prefab` | GameObject | 这个片段的预制体（含平台、墙、锚点） |
| `weight` | float | 抽取权重，越大越容易被选中 |
| `difficulty` | int | 难度等级（预留，以后做"越往上越难"用） |
| `segmentName` | string | 片段名称（方便调试识别） |

**锚点说明**（决策 D2）：锚点不写在 SegmentData 里，而是放在 `prefab` 内部——每个 prefab 必须包含两个空物体：
- `Entry`：入口锚点（这段的底部接缝）
- `Exit`：出口锚点（这段的顶部接缝）

拼接时让"上一段的 Exit"对齐"下一段的 Entry"。

**类比**：相当于 Sebastian 的 `TerrainType`，但用 ScriptableObject 升级了。

---

### 4.2 SegmentLibrary（片段库）

**职责**：管理所有可用片段，提供"按权重随机抽一个"的方法。

**包含**：
- 一个 `SegmentData[]` 数组（所有片段）
- 方法 `GetRandomSegment(System.Random rng)`：用传入的随机器，按权重抽一个片段返回

**关键点**：随机器由外部传入（来自 DailySeed），库本身不持有随机状态——保证"同一种子 = 同一抽取序列"。

**形式**：可以是 ScriptableObject，也可以是 LevelGenerator 里的一个字段。**待定**（搭建时决定）。

---

### 4.3 DailySeed（日期种子）

**职责**：根据当前日期算出种子，提供可复现的随机源。

**核心逻辑**：

```
seed = int.Parse(DateTime.UtcNow.ToString("yyyyMMdd"))   // 例如 20260522
System.Random rng = new System.Random(seed)
```

**保证**：同一天，所有玩家算出的 seed 相同 → 抽取序列相同 → 地图相同。第二天日期变了，地图自动不同。

**类比**：就是 Sebastian 在 Noise.cs 里用的 `System.Random(seed)`，只是 seed 来源换成了日期。

**预留**：以后可加"玩家手动输入 seed"功能（决策 D5 的备选）。

---

### 4.4 SegmentStacker（拼接器）★核心

**职责**：把片段一段段实例化、对齐锚点、堆叠成一座塔。这是整个系统的计算核心。

**核心算法**：

```
当前接缝 = 起点位置
循环 N 次：
    从 SegmentLibrary 抽一个片段（用 DailySeed 的随机器）
    实例化该片段的 prefab
    找到它的 Entry 和 Exit 锚点
    移动片段，让 Entry 对齐到"当前接缝"
    更新"当前接缝" = 这段的 Exit 位置
```

**锚点对齐的数学**（决策 D2、D4）：

```
片段要移动到的位置 = 当前接缝 - (Entry 相对片段根物体的偏移)
```

这样能保证 Entry 这个点本身落在接缝上。只往上直堆（D4），暂不处理旋转。

**类比**：相当于 Sebastian 的 `MeshGenerator`——干最核心的计算活。

---

### 4.5 LevelGenerator（总协调）

**职责**：总指挥，把上面所有模块串起来，对外提供"生成关卡"的入口。

**流程**：

```
1. 调用 DailySeed 得到今天的随机器 rng
2. 把 rng、SegmentLibrary、生成数量等交给 SegmentStacker
3. SegmentStacker 生成整座塔
```

**对外接口**：
- `GenerateLevel()`：生成关卡（可由 Start() 自动调用，或编辑器按钮手动触发）

**类比**：相当于 Sebastian 的 `MapGenerator`——总协调，不干具体计算，只负责调度。

**预留**：可加自定义编辑器按钮（像 MapGeneratorEditor 那样），方便编辑器内预览生成结果。

---

## 五、数据流总图

```
游戏开始 / 点击生成按钮
        ↓
LevelGenerator.GenerateLevel()
        ↓
① DailySeed → 算出今天 seed → 创建 System.Random rng
        ↓
② SegmentStacker 开始循环（重复 N 次）：
        │
        ├─ SegmentLibrary.GetRandomSegment(rng) → 抽一个片段
        ├─ 实例化 prefab
        ├─ Entry 对齐到当前接缝
        └─ 更新接缝 = Exit 位置
        ↓
一座"当天唯一"的攀爬塔生成完毕
```

---

## 六、文件清单（计划）

放在 `Assets/Script/PCG/` 下，独立成文件夹便于管理：

| 文件 | 状态 | 说明 |
|------|------|------|
| `SegmentData.cs` | 待创建 | 片段数据（ScriptableObject） |
| `SegmentLibrary.cs` | 待创建 | 片段库 |
| `DailySeed.cs` | 待创建 | 日期种子 |
| `SegmentStacker.cs` | 待创建 | 拼接器（核心） |
| `LevelGenerator.cs` | 待创建 | 总协调 |

> 注：之前在 outputs 里给过一个简化版 `SegmentStacker.cs`（只管位置对齐），正式版会按本架构重写。

---

## 七、开发顺序（建议）

按"先跑通核心，再加复杂度"的原则：

1. **第一步**：手搓 2~3 个简单片段 Prefab（方块平台 + 墙 + Entry/Exit 锚点）
2. **第二步**：`SegmentData` + `SegmentStacker`，先用固定一种片段，跑通"能堆起来"
3. **第三步**：`SegmentLibrary`，加入"随机选不同片段"
4. **第四步**：`DailySeed`，接入日期种子，实现"当天唯一"
5. **第五步**：`LevelGenerator` 串起来 + 编辑器预览按钮
6. **以后**：无限生成、旋转拐弯、难度递增（见决策表备选项）

---

## 八、与玩家系统的关系

玩家控制器（`FirstPersonController.cs`）已完成，包含：第一人称移动、二段跳、右键贴墙吸附（5秒体力）、蹬墙跳、体力条 UI。

PCG 生成的片段需要满足玩家的能力：
- 平台间距不能超过玩家跳跃能到的高度
- 墙面要打 `Wall` 标签，玩家才能吸附
- 这些"可玩性约束"在设计片段 Prefab 时要考虑（属于决策 D3/D4 之后的关卡设计阶段）

---

*文档版本：v1.0 — 初始架构。后续修改请更新对应章节及"设计决策记录"表。*
