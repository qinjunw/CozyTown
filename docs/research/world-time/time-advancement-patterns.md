# 连续世界时间、固定结算边界与睡眠快进：实现模式调研

## 1. 范围、版本与结论

资料核验日期：2026-09-05。外部证据限于 Unity 官方文档和 OpenMW、Exult 官方开源仓库；未运行这些引擎，也未修改 CozyTown 产品代码。本文研究时间推进机制，不推断原版 Morrowind、Ultima 或其他商业游戏的内部架构。

源码固定到以下版本，避免以移动中的默认分支作为证据：

- OpenMW `openmw-0.49.0`，提交 `675146bd8bce6245d78889f543b5c02a1e3936fe`。[标签引用](https://api.github.com/repos/OpenMW/openmw/git/ref/tags/openmw-0.49.0)
- Exult `v1.12.1`，提交 `cacb4f584901dab59ff288c750d3622d3378435b`。[版本源码](https://github.com/exult/exult/tree/cacb4f584901dab59ff288c750d3622d3378435b)
- Unity 6.0 / `6000.0` 版本化手册；这里只引用时间采样和固定更新的文档契约，不把它当作本项目运行结果。
- CozyTown 当前实现核对点：`5a0d68c8b56b29b0cbcd20f7aab73c6c01d29941`。当时 [ADR-0013](../../adr/0013-deterministic-town-life-and-derived-npc-presence.md) 仍规定 `23:59` 封顶、睡眠进入次日；连续跨午夜属于待讨论的行为变更。

可借鉴的模式是：一个权威世界时刻、小粒度正常推进、显式跨越规则边界，以及各 NPC 独立日程数据。快进不必重演所有渲染帧，但必须区分“有副作用、必须结算的边界”和“只依赖终点时间、可以重建的状态”。下文第 2 节是源码事实，第 3—4 节是 CozyTown 候选方案，不代表需求已经批准。

## 2. 可复核的实现先例

### 2.1 OpenMW：日历时刻可以小量推进，不受时钟文字显示粒度限制

OpenMW 正常运行时，在未暂停分支中把帧时长乘游戏时间倍率、换算为小时，然后调用 `World::advanceTime(hours, true)`。`DateTimeManager` 保存带小数的游戏小时和整数天数，并提供总游戏秒数；因此这里的“小量连续推进”指允许小数时间增量，不是数学上的无限精度连续值。[正常运行入口](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/engine.cpp#L262-L266)、[日期与时刻字段](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/mwworld/datetimemanager.hpp#L23-L69)

日期管理器负责把超出一天的小时归入日期，并同步相关全局时间字段。这提供了“统一日历运算入口”的先例；它本身并不证明农作物、商店等业务都拥有事务提交或重复执行保护。[`setHour` 与 `advanceTime`](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/mwworld/datetimemanager.cpp#L66-L76)、[全局时间同步](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/mwworld/datetimemanager.cpp#L146-L159)

### 2.2 OpenMW：等待/休息执行规则，再推进世界时间

`WaitDialog` 使用 `TimeAdvancer` 驱动等待进度。每完成一个游戏小时，回调先执行 `rest(1, mSleeping)`，再执行 `advanceTime(1)`，并检查玩家死亡；等待结束和被打断也有各自处理。`TimeAdvancer` 按累计帧时长产生小时进度，可以在一帧处理多个进度步。[等待进度回调](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/mwgui/waitdialog.cpp#L234-L263)、[进度累积循环](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/mwgui/timeadvancer.cpp#L28-L54)

`World::advanceTime` 还区分正常增量和快进：两种方式都推进天气和日期；快进额外处理已加载区域中的物品充能、清理投射物并使旧移动失效。它不是只设置一个钟表数值。[世界推进实现](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/mwworld/worldimp.cpp#L903-L925)

证据边界：这些文件展示的是逐小时快进及专门的世界处理，不是按任意事件时间排序的通用调度器；调用链也没有证明失败后可原子回滚或重复提交幂等。CozyTown 可以借鉴“快进经过领域规则”，但不能据此宣称已经获得完整的事件或事务架构。

### 2.3 Exult：每 NPC 的日程数据与当前执行行为分开

Exult 的 `Schedule_change` 包含生效时间、活动类型和地点；该版本时间以三小时为单位。NPC 自己持有日程条目集合。查找当前适用日程时，代码可以选择一天循环中最近已经生效的条目，不要求每个时段都有单独条目。字段中的星期位在注释中明确尚未使用，不能把它描述为已实现的周计划。[日程数据结构](https://github.com/exult/exult/blob/cacb4f584901dab59ff288c750d3622d3378435b/schedule.h#L800-L837)、[每 NPC 保存日程](https://github.com/exult/exult/blob/cacb4f584901dab59ff288c750d3622d3378435b/actors.cc#L5118-L5122)、[当前时段选择与执行切换](https://github.com/exult/exult/blob/cacb4f584901dab59ff288c750d3622d3378435b/actors.cc#L5270-L5314)

正常时钟更新保留不足一分钟的 tick；每跨过一个小时，就执行恢复、饥饿检查和 NPC 日程更新。相反，另一个 `increment` 入口直接改变时间后，仅在最终小时不同于起始小时时重算日程，没有重演正常循环中的恢复和饥饿检查。这是“推进入口不同，副作用可能不同”的源码例子，不能当作快进与正常游玩天然等价的证明。[正常推进与跨小时处理](https://github.com/exult/exult/blob/cacb4f584901dab59ff288c750d3622d3378435b/gameclk.cc#L278-L310)、[另一时间增量入口](https://github.com/exult/exult/blob/cacb4f584901dab59ff288c750d3622d3378435b/gameclk.cc#L253-L271)

### 2.4 Unity：固定物理步长、世界日历与界面刷新是不同约束

Unity 固定更新采用固定的模拟步长，但调用仍依附渲染帧：一帧可以没有或包含多个固定更新。文档中的固定步机制不能直接证明一个项目的世界规则或物理结果具备跨平台确定性。[Fixed updates](https://docs.unity3d.com/6000.0/Documentation/Manual/fixed-updates.html)

`Time.deltaTime` 受 `timeScale` 和 `maximumDeltaTime` 影响；`unscaledDeltaTime` 不受这两项限制。物理补步也受最大允许时长约束。因此，“现实过去了多久”“这次允许运行多少模拟时间”“世界日历推进多少”需要由项目明确映射，不能简单把物理补步视为离线等待或长时间睡眠。[Handling variation in time](https://docs.unity3d.com/6000.0/Documentation/Manual/time-handling-variations.html)

## 3. CozyTown 的最小候选边界

以下建议假设仍为离线单玩家、少量 NPC、串行提交；日程本身不生产物品、不交易、不调用模型。倍率、日刷新时刻、可等待范围及失败语义需要需求方确认。

### 3.1 连续权威时刻，离散显示与结算

建议只保留一个可比较的权威世界时刻。当前规则可先用整数游戏分钟；若需分钟内的定时规则，再考虑整数秒或更细定点 tick。日期、分钟与累计时间应能互相确定，不同时维护可各自修改的多份真值。精度须能表达已批准的日程边界；从现实秒转换时保留余量，并定义舍入和溢出规则。不要逐帧四舍五入后丢弃余量，否则相同总时长的不同分帧可能得出不同世界时间。

连续时刻不要求 UI 每秒重绘。界面可以每游戏分钟或每十分钟变化一次，但农田刷新和 NPC 日程必须比较权威时刻，不能比较格式化后的钟表文字。例如，若某条日程在 `06:03` 生效，就不应因为界面仍显示 `06:00` 而延迟到 `06:10`。

当前 [DaytimeClockCoordinator](../../../Assets/CozyTown/Runtime/Application/DaytimeClockCoordinator.cs) 每现实五秒才修改十个游戏分钟；[GameClockSnapshot](../../../Assets/CozyTown/Runtime/Time/GameClockSnapshot.cs) 只有日和分钟。这是已实现的离散时钟，不是已存在的小数世界时刻。采用本建议需要明确替换该行为及相应测试，不能只修改显示代码后声称时间已经连续。

### 3.2 固定刷新边界与幂等结算

建议将刷新日定义为固定日历锚点，而不是“每按一次睡觉就刷新”。具体锚点可以是午夜或某个清晨时刻，二者是玩法选项。正常运行与等待都检查 `(起点, 终点]` 内跨过的锚点；恰好到达边界时结算，从该边界继续推进时不再次结算。

幂等需要业务状态保护，不能只靠一个临时 `lastTick`：刷新结果与最后已处理的边界标识一起提交、保存和恢复。同一边界重复到达不得重置当天交易或再增加成长；推进命令的两个合法增量则不是重复边界，不能被错误去重。

项目已有可保留的基础：[商店补货策略](../../../Assets/CozyTown/Runtime/Economy/DeterministicShopStockReplacementPolicy.cs) 对同一 `LastRestockedDay` 返回当前状态副本；[农田](../../../Assets/CozyTown/Runtime/Farming/InMemoryFarmService.cs) 和 [畜牧](../../../Assets/CozyTown/Runtime/Livestock/InMemoryLivestockService.cs) 拒绝重复日、跳日。后两者的重复调用是失败，不是成功 no-op；上层要先识别已完成边界，不能把重复日错误当作“还需再结一次”。

现有 [DayTransitionCoordinator](../../../Assets/CozyTown/Runtime/Application/DayTransitionCoordinator.cs) 将下一次结算绑定到 `SleepToNextDay`，并要求模块的已处理日等于时钟日。若选择非午夜刷新和自然跨日，需要重定义这里的“结算日”及对齐校验；仅取消时钟封顶会让时钟先跨日、经济状态留在旧日。

### 3.3 等待/睡眠推进区间，不裸改时钟

候选实现可以在应用层直接处理少数已知边界，无需事件总线或优先队列框架：

1. 将正常帧增量或用户批准的等待目标转成世界时间区间。
2. 按时间顺序找出区间内必须执行副作用的边界；同一时刻的处理顺序固定，例如先完成当日刷新，再发布该时刻的只读状态。
3. 经过各边界的领域结算后，发布终点时刻。正常游玩继续真实移动；快进期间的纯日程目标可以按终点重建，不必播放所有中间动画。

只依赖当前时间的“NPC 当前应去哪里”与会累计资源的“每天成长一次”不能使用相同跳过策略。若未来日程本身会消费或生成物品，它也成为必须逐边界处理的领域规则，不能继续仅重建终点。

跨多个边界失败时，必须在“整段原子提交”和“已成功边界保留、返回已完成时刻”之间明确选择。当前日切协调器有失败回滚分支，不等于已经拥有任意多日区间的原子事务。超大合法输入也必须有等待范围或工作量预算，不能用每渲染帧逐步模拟数百天。

读档是恢复状态，不是从加载前时间快进到存档时间。恢复必须把权威时刻和已结算标识作为一致状态处理；若继续使用只有分钟精度的旧存档，应明确余量丢弃和边界恢复语义，不能宣称精确恢复连续时刻。

### 3.4 每 NPC 配置，统一解释；时间模块采用组合

建议每个 NPC 通过稳定 `NpcId` 关联有序日程条目，最小数据为 `StartTime + ActivityId + DestinationId`。由同一个纯规则解释器按时刻选择条目；配置应校验重复起点、无效地点，并明确一天起始覆盖或跨日回绕规则。活动目标与实际到达状态仍分开，日程改为“工作”不等于已经到达工作地点。

统一时间结算模块适合作为协调者，不适合作为所有时间相关系统的基类。它负责权威时刻、跨界检测、确定顺序和提交结果；农田、畜牧、商店与 NPC 保留各自领域规则，通过组合及窄接口参与。它们不是一种“时钟”，也不需要继承一个带 `OnTick/OnSleep/OnLoad` 大量虚方法的共同父类。

OpenMW 世界入口组合调用天气和日期管理器，可作为这种职责分配的源码参照。[世界推进实现](https://github.com/OpenMW/openmw/blob/675146bd8bce6245d78889f543b5c02a1e3936fe/apps/openmw/mwworld/worldimp.cpp#L903-L925) CozyTown 的接口大小仍应由实际用例决定：可以先复用具体的日切用例，待确实需要自然跨日时提取“处理指定结算边界”的窄入口。继承只在多个实现真正共享同一领域契约时再评估；“都受时间影响”本身不足以建立继承关系。

Unity 驱动器仍只提供有效 elapsed 与暂停状态；系统菜单暂停普通帧推进不应自动禁止玩家明确发起的睡眠/等待命令。等待期间是否允许取消、恢复帧是否丢弃旧采样需单独定义，避免两条推进路径同时消费同一段现实时间。

## 4. 用公开行为判定方案是否满足要求

以下是候选验收，不是本轮已经运行的测试：

| 场景 | 应明确并验证的结果 |
| --- | --- |
| 相同起点、相同总有效 elapsed，不同分帧 | 世界时刻与结算结果相同；包含 `4.9 + 0.1` 和多次 `0.1` 的累积边界 |
| 日程在非 UI 刷新时刻生效 | 以权威时刻切换目标；修改文字显示频率不改变日程结果 |
| 边界前一点、恰好边界、边界后一点 | 边界执行一次；从边界继续推进和重复检查均不重结 |
| 自然运行与等待跨越同一区间 | 必须执行的领域副作用一致；允许不同的中间动画与终点归位策略须写明 |
| 同日交易后重复检查刷新边界 | 当天售出、买入后的库存保持，不被重新生成的日库存覆盖 |
| 等待跨两个刷新边界，第二个失败 | 结果符合选定的整段回滚或部分完成契约，不出现“报告无变化但已结算一天” |
| 在刷新前后保存并加载，包括同一时刻加载 | 恢复正确时刻与结算标识；不把读档当快进，不重复结算 |
| 非法 delta、极大 delta、暂停与恢复 | 非法输入无副作用；工作量有界；暂停时间不补算，显式等待不与普通推进重复累计 |
| 两名 NPC 拥有不同起点/地点的配置 | 同一解释器得出不同目标；新增配置无需复制一个 NPC 专用时钟或派生类 |

确定性声明应限定为相同初始状态、相同规则版本和相同规范化输入序列产生相同领域结果；本调研没有验证 Unity 物理路径、动画或不同平台浮点执行的完全一致性。
