# NPC 行走算法：常见组合与当前小镇的适用条件

研究日期：2026-09-06。范围：二维经营小镇的日常移动；依据公开一手文档、算法实现与实现前仓库 `3a50bb4` 的静态代码。下文建议是研究判断；本研究阶段未运行性能基准或验证游戏内行走效果。后续已批准的实现与实测证据独立记录在 [T1 实现记录](../../TOWN_LIFE_IMPLEMENTATION.md)，不回写本研究阶段的结论。

## 证据：NPC 行走由哪些部分组成

“NPC 会走路”通常需要组合几层功能：日程或行为逻辑选目的地；地图数据表示哪里能走；寻路算法输出路线；移动逻辑逐帧沿路线前进；需要时再加入避障。Unity 官方也明确区分全局寻路与局部运动，并公开其导航系统使用 A* 和 RVO。[Unity 导航内部机制](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavInnerWorkings.html)

因此，“路图、格子还是 NavMesh”和“BFS、Dijkstra 还是 A*”是两组选择。前三者描述空间，后三者搜索连接关系；NavMesh 同样可以使用 A*。[Godot 2D 导航概览](https://docs.godotengine.org/en/4.5/tutorials/navigation/navigation_introduction_2d.html)

| 部分 | 已核验的机制 | 适用条件与限制（研究判断） |
| --- | --- | --- |
| 路点图 / 道路图 | 地点是节点，道路是边；节点可携带位置，边可携带成本。 | 固定街道、门口、工作点；需要保证每条边确实可通行。 |
| 栅格 | 可行走单元按邻接关系连接；Godot 提供四向、斜向及墙角通行规则。 | 玩家按格建造、耕地、放家具时，障碍可直接对应占地格。[AStarGrid2D](https://docs.godotengine.org/en/4.5/classes/class_astargrid2d.html) |
| BFS | 求无权图的最少边数路径。 | 每一步等价，或只要求找一条连接路线；道路长短不一时，最少路段不等于最短路程。 |
| Dijkstra | 求非负边权下的最小总成本路径。 | 边权取长度、通行时间或地面代价；适合当前小路图增加“选近路”语义。 |
| A* | 用已付成本与剩余成本估计引导搜索；估计过高可能失去最短路保证。 | 单目标寻路且有合适估计；四向等单位代价格网可用曼哈顿距离。加权地形需让估计与最低移动成本匹配。 |
| NavMesh + funnel | 在可走多边形之间找通道，再用 funnel/string pulling 提取通道内拐点；Detour 将寻路与 `findStraightPath` 分开。 | 自由走动、不规则开阔区域；还要处理角色体积、网格更新和路径跟随。[Detour v1.6.0 实现](https://github.com/recastnavigation/recastnavigation/blob/v1.6.0/Detour/Source/DetourNavMeshQuery.cpp) |
| RVO / ORCA | 根据邻居预测碰撞并调整局部速度；ORCA 让一对角色各承担一半避让责任。 | 确有互相避让需求时评估。它们不负责找整张地图的路线，也不单独解决封死道路或单人窄口的通行安排。[ORCA 作者页面](https://gamma-web.iacs.umd.edu/ORCA/) |
| Flow field（流场） | 把到目标的成本与方向保存为场，让多个单位共享。 | 大批单位共享目标或局部通道时评估；不同目标和地图变化会增加更新与缓存管理。 |

BFS、Dijkstra 的适用条件见 [NetworkX 最短路径文档](https://networkx.org/documentation/stable/reference/algorithms/shortest_paths.html)；A* 启发函数限制见 [NetworkX A* 文档](https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.shortest_paths.astar.astar_path.html)。这些是算法语义依据，不表示项目需要引入该库。

可确认的商业案例是《Supreme Commander 2》：开发者 Elijah Emerson 描述了门区图上的 A* 与分块成本场、积分场、流场组合，面向数百至数千单位。该案例说明流场可与 A* 共存，其性能描述不能外推为本项目实测结果。[作者技术章节](https://www.gameaipro.com/GameAIPro/GameAIPro_Chapter23_Crowd_Pathfinding_and_Steering_Using_Flow_Field_Tiles.pdf)

日程、有限状态机（FSM）和行为树（BT）处理“何时去哪里、被打断后做什么”。固定作息可以由时间表驱动“前往→到达→工作/休息”；当条件分支和复用子行为增多，再评估行为树。Unity Behavior 的官方范围是 NPC 行为和场景交互控制。[Unity Behavior](https://docs.unity3d.com/Packages/com.unity.behavior@1.0/manual/index.html)

## 开罗游戏的证据边界

开罗官网对《箱庭タウンズ》的介绍包括建住宅迎来居民、居民工作赚钱后到商店消费；《創造タウンズ島》则介绍居民使用商店或设施后，可能将纪念品带回家。这些是一手玩法描述。[《箱庭タウンズ》官网](https://kairosoft.net/game/appli/miniature.html)、[《創造タウンズ島》官网](https://kairosoft.net/game/appli/towns.html)

本轮日文、英文检索未找到能够确认这些作品采用 A*、BFS、NavMesh 等内部算法的一手技术资料；这不代表开罗从未公开过相关资料。角色沿路绕障的画面也不足以判断底层算法。“由目的地和活动驱动居民移动”是本文从玩法提炼的设计判断，不能据此断定开罗内部采用 FSM 或行为树。

对当前项目，建议按“日程选地点 → 寻路返回路线 → 跟随路线并按碰撞规则移动 → 确认到达后开始活动”分工。首次确定目标时查询路线，后续只在目标、通行地图变化或路线失效时重新规划；每帧更新路径跟随，无需每帧重做全局寻路。这是后续实现建议：现有路由查询仅提供路线前置能力，本轮没有实现实际移动或开展性能测试。

## 当前项目事实与建议

仓库的 `CozyTownTownLayout` 定义 32×22 地面和四户 NPC 住宅；`TownMap2D.TryFindRoute` 使用队列遍历显式双向道路，并按前驱回溯路点。它是 BFS，未按道路长度计费，也未从建筑碰撞体生成通行数据。道路长度并不均匀，因此不能把当前结果称为“最短步行距离”。参见 [TownMap2D](../../../Assets/CozyTown/Unity/Town/TownMap2D.cs) 与 [CozyTownTownLayout](../../../Assets/CozyTown/Unity/Editor/CozyTownTownLayout.cs)。

研究建议以“当前只去固定住宅、工作点、休息点”为前提：保持道路图和路点跟随。若规则只要求沿连接道路到达，BFS 可以继续承担查询；若要按距离或通勤时间选路，先给边定义对应成本，再采用 Dijkstra。A* 也可搜索同一张图，但是否减少本项目耗时需要测量。四个 NPC 的数量本身不足以支持加入流场或 RVO；当前角色互不阻挡的规则也不要求增加强碰撞避让。

当玩法变为玩家自由摆放建筑、改变通路，或 NPC 要到任意耕地和格子时，再评估带地形通行成本的占地栅格 + A*；首版可只允许四向移动，避免斜穿墙角。这里的成本加在移动边上，不是放大启发函数的 Weighted A*。若场景主要是不规则区域中的任意连续位置，则评估 NavMesh。迁移触发条件是通行规则和目的地表达发生变化，而非画面是否像开罗。

## 后续实现可采用的验收案例

以下仅为候选 TDD 案例，尚未批准为新增需求或实现：

- 有两条路线且路段数、总长度排序相反时，结果符合所选成本语义；断路查询明确返回无路并结束。
- 中途改目标从角色实际位置续行；到达只触发一次，失败不误报到达。
- 若采用格网和阻挡规则：按脚底碰撞范围保留空间，测试墙角斜穿、窄门、建筑占地变化和绕路。
- 若要求角色互挡：验证对向窄口的优先级、等待及退出条件，不能只检查“没有重叠”。
- 在项目既定游戏时间契约下，验证暂停、变速、跨日时移动和工作状态一致。

## 资料版本与核验限制

访问日期均为 2026-09-06。Unity AI Navigation 页面标示 2.0.14；Unity Behavior 为 1.0.16；Godot 引用固定为 4.5；NetworkX 页面标示 3.6.1；Detour 源码固定为 v1.6.0。ORCA 采用作者项目页与其列出的 2011 年论文信息；流场采用 2013 年《Game AI Pro》第 23 章。引擎文档版本仅记录所查资料，不代表仓库安装了相应包。
