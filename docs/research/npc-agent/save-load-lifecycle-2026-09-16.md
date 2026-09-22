# NPC Agent 与存读档：运行生命周期和失败恢复

- 核查日期：2026-09-16。
- 关联任务：[调研存读档与 Agent 运行状态恢复的一致性边界](https://github.com/qinjunw/CozyTown/issues/90)；源码核查基准：`85e243edabcead1cfcf8da4d4b90a1633888c49b`。
- 文档类型：官方来源研究、当前代码核验与后续实施依据。项目是单进程 Unity 小镇，采用版本化 JSON 存档；外部模型异步返回候选，由宿主检查和执行。
- 本轮未修改运行或测试代码，没有新增 Unity 测试运行或真实模型调用。测试章节列出已有源码覆盖及后续验收，不把源码阅读计为本轮测试通过。

## 1. 结论与资料边界

**应结合存读档检查一致性，但普通读档不等于更换依赖对象。** 当前读档保留原有服务引用，恢复其数据，再通知 NPC 重建。已有资料加本轮核查，足够指导下一项换绑失败修复及读档对照测试；关键重建失败的处置还需复现和选定实现，完整 NPC 经历、邀约及交谈的存档语义也尚未确定。后者是玩法规则，不能从引擎文档推导出来。

这里需要分别检查三个结果：磁盘上的存档是否完整；加载失败时当前游戏是否仍可使用；加载成功后是否只有新状态可以接受后续行动。三者需要分别验收。官方来源提供了数据保存、对象重建和异步执行机制，没有承诺把游戏中的全部对象、事件订阅及外部请求作为一个事务自动恢复。

### 1.1 当前读档实际做了什么

| 层次 | 已有实现与代码入口 | 当前边界 |
| --- | --- | --- |
| 持久化数据 | [GameSaveSnapshot](../../../Assets/CozyTown/Runtime/Save/GameSaveSnapshot.cs) 第 12 行写入 schema v3；保存世界种子、时间、角色／商店资产、农田和畜牧 | 没有保存 Agent 任务、会面承诺、对话轮次、会面经历或运行代次 |
| 文件提交 | [JsonFileSaveStorage](../../../Assets/CozyTown/Runtime/Save/JsonFileSaveStorage.cs) 第 49 行起写同目录临时文件、`Flush(true)`、复读校验，再 Replace／首次 Move；读取支持旧版迁移 | 保护文件与恢复内存是不同职责；当前未保留备份，不能把“可选备份”写成已实现 |
| 权威状态恢复 | [GameSaveCoordinator.Load](../../../Assets/CozyTown/Runtime/Application/GameSaveCoordinator.cs) 第 63 行起读取并校验，捕获调用前快照，再恢复五个模块；返回失败时执行回滚 | 当前是顺序恢复及补偿，不是全对象引用一次交换；回滚本身失败有独立错误码，不能承诺任意失败均恢复原状 |
| 对外读档与通知 | [组合根](../../../Assets/CozyTown/Runtime/Core/CozyTownCompositionRoot.cs) 第 160 行将存读档入口接到 `DaytimeClockCoordinator`；其 [Load](../../../Assets/CozyTown/Runtime/Application/DaytimeClockCoordinator.cs) 第 106 行在恢复成功后清零时间余量并发布重建版本 | 直接构造裸 `GameSaveCoordinator` 的测试只覆盖数据恢复；默认 `services.GameSave.Load()` 才包括外层时间通知 |
| NPC 重建 | [Controller.Apply](../../../Assets/CozyTown/Unity/Npc/CozyTownTownLifeController.cs) 第 195 行更新代次、会面并重建身体位置；[NpcAgentWorld.Observe](../../../Assets/CozyTown/Runtime/NpcAgents/NpcAgentWorld.cs) 第 49 行清理活动和事件；[MeetingBoard.Observe](../../../Assets/CozyTown/Runtime/NpcAgents/NpcMeetingBoard.cs) 第 282 行清理会面和经历 | 当前按日程合法归位，取消临时活动／会面，清空会面经历；不精确续走或恢复交谈，这是已有首版边界 |
| 迟到请求 | [NpcDecisionScheduler](../../../Assets/CozyTown/Runtime/NpcAgents/NpcDecisionScheduler.cs) 检查运行代次与状态版本，同一实例保留现实时间预算和未完成任务 | 下一次 Tick 处理旧请求；取消不等于 Task 完成。运行中换调度器的跨实例计数由独立任务处理 |

简化后的现状是：

```text
默认 GameSave.Load
  → 存档解析、迁移、校验
  → 捕获旧数据，顺序恢复五个模块
      返回失败 → 尝试恢复旧数据，返回失败／回滚失败
      成功     → 清零时间余量，WorldTimeFlow 更新 Current
               → 同步通知 NPC、灯光等订阅者
               → NPC 生成新运行代次并重建
               → 后续 Tick 拒绝旧请求
```

更换依赖的 `Controller.Bind` 是另一条入口：它连接新的时钟／资源对象，可能创建新的 NPC 世界。该入口也应遵守失败不破坏原状态的规则，但正常读档不需要靠重新 Bind 或重新创建调度器完成。

### 1.2 信息缺口与具体风险

**资料版本需要纠正。** ADR-0003 的“当前实现”仍描述 v2；代码和 ADR-0014 已采用 v3。本轮只修正该现状描述。早期[持久化研究](../economy-trade/persistence-database-threshold.md)中的 v1 是当时输入，不用来判断当前字段。

**提交后重建失败没有完成语义。** [WorldTimeFlow.Publish](../../../Assets/CozyTown/Runtime/Time/WorldTimeFlow.cs) 第 33—42 行先更新 `Current`，再 `Changed?.Invoke`；外层 Load 没有处理订阅者异常。若通知链中一项抛错，后续项不会被调用，而权威时间和资产已经恢复。Controller 的重建也先更新代次与会面，再准备全部身体位置。由源码可推导存在部分重建的失败边界；本轮没有注入异常复现，不声称默认场景正常读档已发生故障。委托异常传播语义见 [C# 规范 §21.6](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/delegates#216-delegate-invocation)。

该边界已登记[明确读档提交后重建失败的处理边界](https://github.com/qinjunw/CozyTown/issues/91)。它与原有[换绑失败修复](https://github.com/qinjunw/CozyTown/issues/87)、[决策重配预算](https://github.com/qinjunw/CozyTown/issues/88)分别实施。现有回滚针对五个权威模块的失败返回，不涵盖随后发生的场景通知异常。

## 2. 七项官方来源

以下页面均在 2026-09-16 浏览核查。版本固定的引擎页面用于说明该版本的机制；活动页面只代表访问时的文档，不作为已安装 SDK 的版本承诺。

| 来源与版本 | 页面直接支持的事实 | 对本项目的证据边界 |
| --- | --- | --- |
| [S1：Unity JSON Serialization，6000.5](https://docs.unity3d.com/6000.5/Documentation/Manual/json-serialization.html) | JSON 通过明确的类和字段表达数据。`FromJson` 创建对象；`FromJsonOverwrite` 修改现有对象，缺失字段保持旧值。后台序列化时不能同时修改同一对象 | 序列化 API 不负责业务校验或恢复事务。部分字段覆盖不能直接当作完整存档恢复 |
| [S2：Epic Saving and Loading Your Game，UE 5.6](https://dev.epicgames.com/documentation/en-us/unreal-engine/saving-and-loading-your-game-in-unreal-engine?application_version=5.6) | 游戏先把选定数据复制到 `SaveGame`，读档后再把数据应用到角色等对象。提供同步、异步读写和成功结果 | 文件加载成功与游戏对象恢复完成是不同步骤；异步写盘不自动解决跨对象快照一致性 |
| [S3：Godot Saving games，4.5](https://docs.godotengine.org/en/4.5/tutorials/io/saving_games.html) | 显式选择持久对象和字段；读档时实例化场景并恢复数据。嵌套对象要考虑父子创建顺序和引用重连 | 官方教程明确把完整状态恢复留给项目；示例先删除旧对象再逐项恢复，不能用来证明失败时保留原状态 |
| [S4：Microsoft File.Replace，.NET Framework 4.8.1 API 视图](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=netframework-4.8.1) | 以另一个文件替换目标，并可备份原文件；源与目标跨卷会抛异常 | 页面说明文件操作契约，不承诺进程内游戏状态回滚或所有平台的断电持久性；Unity 目标平台还需独立验证 |
| [S5：Microsoft Cancellation in Managed Threads，.NET 通用文档](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads) | 取消是合作协议：请求方通知，执行方负责响应。已取消 token 不能复用；取消源完成使用后释放 | 发出取消请求不代表旧任务已结束，更不能据此断言远端模型已停止计算或计费 |
| [S6：LangGraph Functional API overview，访问日活动文档](https://docs.langchain.com/oss/python/langgraph/functional-api) | 恢复会从边界重放，复用已持久化的任务结果；未完成任务可能再次运行。副作用需要幂等键或结果检查 | 检查点不能自动保证任意外部副作用只执行一次；此处借鉴恢复机制，不建议 Unity 项目据此引入 Python 框架 |
| [S7：C# 语言规范 §21.6，访问日文档](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/delegates#216-delegate-invocation) | 多播委托按序同步调用；未捕获异常向调用者传播并停止后续调用 | `Changed` 通知不能自行保证所有订阅者重建成功；异常隔离与关键恢复步骤须另定 |

Unity 页面显示版本为 Unity 6.5（6000.5），与项目所用编辑器版本线一致，但本文没有据此声称所有目标平台都经过运行验证。UE 5.6、Godot 4.5 是明确选择的版本，未称为最新版。S4 使用 .NET Framework API 视图说明替换契约，未据此确认项目具体后端的实现细节。S5、S6 没有在本文固定到某个包发布版本。

## 3. 数据保存与运行对象恢复

S2、S3 都要求项目选择需要保存的数据，并在恢复时重新应用或重建对象；这是可借鉴的分离点。[Epic SaveGame](https://dev.epicgames.com/documentation/en-us/unreal-engine/saving-and-loading-your-game-in-unreal-engine?application_version=5.6)、[Godot Saving games](https://docs.godotengine.org/en/4.5/tutorials/io/saving_games.html)

**CozyTown 设计推论：** 存档应该描述可恢复的游戏事实；运行时句柄由宿主重新创建。以下是需要逐项确认的分类候选，不能视为当前已保存的字段清单。

| 内容 | 候选归属 | 需要明确的恢复语义 |
| --- | --- | --- |
| 时间、库存、交易结果、任务进度 | 持久游戏事实 | 同一份快照中的关联数值必须一致；成功恢复后统一可见 |
| NPC 已确认经历、亲历的声明、关系变化 | 可持久化的角色历史 | 保留来源、发生时间和可见参与者；历史声明不能升级为当前库存事实 |
| 尚未完成的邀约、约定、交谈轮次 | 待决定的玩法状态 | 保存并恢复，或读档时明确中止；两种方案都需要可观察结果 |
| 当前位置、行走路径、观察缓存、场景对象引用 | 分别决定保存或重算 | 精确位置是否属于存档承诺；路径和观察应以恢复后的场景重新校验 |
| `Task`、HTTP 请求、取消源、事件订阅 | 当前运行过程中的对象 | 不把旧句柄写进存档；重建订阅和请求生命周期 |
| 实际请求占位、限流窗口、已消耗调用额度 | 运行会话或账户约束 | 读档不应倒退外部已发生的调用；不能随游戏时间回滚后重新获得额度 |

NPC 记忆如果会影响后续行为，就是潜在的游戏状态。是否持久化、保留多久、如何跨存档回退，需要先定语义；单纯保存提示词或整段模型上下文，无法替代带身份和时间的游戏事实记录。该结论是项目设计推论，不是上述引擎的现成 NPC 存档功能。

## 4. 磁盘提交与内存恢复要分别设计

**磁盘层候选：** 在游戏认可的采样边界生成独立快照，再序列化到临时文件；写入成功后用目标平台支持的替换机制发布，按需要保留备份。`File.Replace` 提供替换和备份契约，但这不足以承诺断电后一定得到最新存档。目标平台、刷新策略、首次写入和替换失败恢复都要另验。[Microsoft File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=netframework-4.8.1)

快照与实时对象分开还使后台 I/O 不必读取正在变化的游戏字段。这里“同一时点的快照”是项目必须建立的约束，Unity 仅说明序列化期间并发修改对象不安全，并未自动提供该约束。[Unity JSON Serialization](https://docs.unity3d.com/6000.5/Documentation/Manual/json-serialization.html)

**内存层候选：** 采用“准备、校验、发布、通知”的恢复顺序。先解析存档并检查版本、内容 ID、数量约束和跨对象引用；再准备可恢复的候选状态；在一个明确边界发布新状态，最后通知视图和调度器重新读取。可预期的输入错误应发生在发布前；准备期间不应改变现有活动或解除现有订阅。该顺序是本项目的设计候选，不能说成某个引擎自动提供的事务。

发布后仍可能遇到视图或事件订阅者异常，因此还需明确权威状态与表现层的失败边界。不能把“数据库式回滚整个 Unity 场景”作为默认实现要求，也不能仅因文件解析成功就报告整个读档成功。应先审计现有恢复入口能否做到先校验后提交，再选择最小改动。

## 5. Agent 请求跨越读档边界

取消仅负责通知旧操作停止；结果是否还能改变当前状态，需要宿主自己判定。[Microsoft 协作取消](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)

**CozyTown 设计推论：** 每次成功建立新的运行状态时生成新的运行代号。请求携带派发时的代号；响应应用前再次检查代号、仍有效的待处理请求和相关状态版本。读取同一份存档两次也应获得不同运行代号，避免第一次读取后派发的请求在第二次读取后被误接纳。存档内的游戏时间和运行代号承担不同职责。

读档成功后，请求取消旧任务，并剥夺旧结果修改新状态的资格；在本地任务真正完成前，继续按已定义策略统计在途占位。观察并清理旧任务的成功、失败和取消结果。若第三方任务长期不响应，需要明确有界的超时或隔离策略，而不能以重建调度器来隐式清零在途数量。以上是由合作取消边界推导出的宿主规则，不代表 .NET 能确认模型供应商的远端执行状态。[Microsoft 协作取消](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)

准备或校验失败的读档应保持原有运行状态及其请求资格；仅当新状态被成功发布后，才让旧代号失效。否则一次无效存档也可能无故终止正在进行的交谈。这里要与项目真实提交顺序核对，避免取消太早或旧结果接纳太晚。

## 6. 重放不等于重做交易

LangGraph 把恢复描述为从检查点边界重放，而不是恢复某一行代码上的线程；已完成结果可以复用，未完成任务仍可能重试。因此有副作用的步骤需要去重或幂等处理。[LangGraph Functional API](https://docs.langchain.com/oss/python/langgraph/functional-api)

**CozyTown 设计推论：** 可恢复的是“谁与谁达成了什么、执行到哪一步、已确认什么结果”。库存交换、任务奖励和经历追加等动作，应该有能识别重复提交的业务身份，并在提交时检查当前条件。恢复一个已完成的交易不能再扣一次物品；恢复未完成的交易也不能只凭旧模型文本认定已完成。

跨读档的去重范围必须与玩法一致。读取交易发生前的旧存档后，玩家通常可能重新进行这笔交易；这与同一恢复过程中的重复回调是两种情况。候选做法是在当前运行或存档谱系内判定重复，而不是用全进程永久黑名单禁止旧存档再次发生同类行为。外部收费调用已发生的成本则不能靠游戏存档回滚。这些规则需由产品规格选择，S6 不替本项目作决定。

## 7. 实施前应补齐的产品约定与验收

| 待决定或核对的约定 | 至少一个可观察的验收场景 |
| --- | --- |
| 邀约和交谈是中止还是恢复 | 在邀请发出、被接受、到达、交谈中分别保存；读取后没有双方状态冲突或无限等待 |
| 角色经历是否随存档恢复 | 读取较早存档后，NPC 不继续使用尚未发生的经历；保存时已发生且约定保留的经历仍可读取 |
| 核对已有按日程归位规则；精确恢复仅在未来改需求时讨论 | 当前按 [ADR-0014](../../adr/0014-continuous-world-time-and-morning-settlement.md) 保留合法归位而非精确续走；局部观察来自实际恢复后的位置 |
| 一次读档的成功边界 | 无效 ID、缺失角色或不合法数量在发布前被拒绝；时钟、资源、活动和订阅仍可继续使用 |
| 旧请求与额度的归属 | 旧任务忽略取消并晚到成功或失败时不能写新状态；反复读档不能重置真实调用约束 |
| 一次交换或经历追加的提交身份 | 恢复通知重复送达时不重复扣物品或追加经历；读取更早存档后的合法重做按玩法处理 |
| 磁盘写入的失败恢复 | 临时写入、替换或备份步骤失败后仍可找到可读存档；首次保存和已有存档分别验证 |

目前足够为已经明确的“无效换绑不能破坏现有运行状态”补失败用例。完整 NPC 存档扩展应等待上述持久化语义确定；研究恢复机制不等于决定保存所有 Agent 内部对象或精确复现模型随机输出。

## 8. 实施顺序与测试矩阵

沿用已约定的公开入口：存读档与世界时间、`IWorldTimeFlow.Changed`、居民控制器状态／资源查询和 `INpcDecisionClient`。固定客户端可控制旧 Task 的完成时机；这些一致性验收无需付费模型。每项新缺口先复现 RED，再补最小实现，不能用重复真实模型对话替代故障注入。

| 场景 | 当前证据或缺口 | 后续可观察结果及归属 |
| --- | --- | --- |
| v3 往返、v1/v2 迁移、坏 JSON、未知版本、临时写入／替换失败 | [JsonFileSaveStorageTests](../../../Assets/CozyTown/Tests/EditMode/Save/JsonFileSaveStorageTests.cs) 与 [MorningSettlementSaveTests](../../../Assets/CozyTown/Tests/EditMode/Save/MorningSettlementSaveTests.cs) 已有覆盖源码；本轮未重跑 | 保留原存档和原状态；按改动范围选回归，不重写存储 |
| 未知物品、重复主体、缺失 NPC、末模块修改后返回失败 | [GameSaveCoordinatorTests](../../../Assets/CozyTown/Tests/EditMode/Application/GameSaveCoordinatorTests.cs)、[ResidentEconomySaveTests](../../../Assets/CozyTown/Tests/EditMode/Application/ResidentEconomySaveTests.cs) 已有恢复／回滚用例 | 失败后时间、全部资产和 NPC 活动／代次保持；补外层通知与当前请求资格对照 |
| 读档成功、失败及连续两次读取同一存档 | [NpcAgentActivityPlayModeTests](../../../Assets/CozyTown/Tests/PlayMode/NpcAgentActivityPlayModeTests.cs) 已覆盖成功清活动、旧请求拒绝和失败保持；重复读档仍需专项对照 | 只有成功恢复推进代次；失败不使原合法请求失效；首次读取后派发的回复不能作用于第二次读取；纳入换绑修复对照及后续读档验收 |
| 换绑到缺失参与者的新资源对象 | 前次静态审计，无新增失败复现 | 原服务、活动、会面、订阅、预算与在途状态均保留；有效重试成功；由[确保 Agent 换绑失败时保留原对象图](https://github.com/qinjunw/CozyTown/issues/87)实施 |
| 旧 Task 忽略取消，稍后成功或失败 | [NpcDecisionPlayModeTests](../../../Assets/CozyTown/Tests/PlayMode/NpcDecisionPlayModeTests.cs) 第 119 行已有读档／换绑成功晚到及预算对照；不能据此推定所有异常分支通过 | 旧结果无世界写入，真实在途占位直到本地 Task 完成；读档不重置额度；跨实例重配由[预算与在途请求任务](https://github.com/qinjunw/CozyTown/issues/88)实施 |
| 状态已恢复，重建通知抛错或关键重建失败 | 本轮静态发现；已有通过结果不覆盖 | 区分已提交数据与未完成重建；阻止不一致会话继续行动，结果可观测且能恢复。覆盖订阅顺序、重试、正常走时和睡眠；由[重建失败边界任务](https://github.com/qinjunw/CozyTown/issues/91)实施 |
| 保存时位于邀约、赴约、交付前后、交谈中 | 当前资产可保存，会面和经历未保存 | 确定恢复／中止规则后验证双方一致、无重复交换、无未来经历泄漏，交由[验证四人跨日与读档恢复](https://github.com/qinjunw/CozyTown/issues/55) |

本轮只阅读测试源码并核对入口。前次装配核验的 26 项复测见[历史报告](../../audits/npc-session-composition-2026-09-15.md)，不能替代本表尚未实施的失败用例。

## 9. 推荐实现约束

1. **先修独立入口，再验完整读档。** 换绑修复优先把可失败校验、资源参与者核对和候选准备移到引用替换前；提交阶段只做已准备的切换，避免调用外部回调或再次校验。现有 [WorldTimeCoordinator](../../../Assets/CozyTown/Runtime/Application/WorldTimeCoordinator.cs) 第 103—138 行已经使用准备后提交的局部模式，可参考其约束，不必先设计通用事务框架。
2. **保留当前数据边界。** JSON v3、五个模块快照、稳定 ID 和窄服务入口继续使用。已有业务失败回滚不因 Agent 研究被整体替换；实际修改到的恢复路径必须补失败与合法重试测试。
3. **明确提交前与提交后。** 输入错误及候选准备失败应保留原状态；数据已提交后的关键重建失败不能伪装为“旧状态未变”。推荐把关键 NPC 恢复与可选显示通知区分；重建不完整时阻止该会话继续行动，诊断可见并允许受控恢复。具体是提前准备关键状态，还是增加明确的恢复状态，由重建失败任务在复现后选择；不能仅吞异常并报告成功。
4. **运行限额独立于游戏存档。** 本进程的请求额度和物理在途槽不能通过读档或换调度器清零；运行代次每次成功恢复更新，旧候选检查保留。这里没有承诺重启进程后持久保存本地限流窗口，也不能回滚供应商计费。
5. **NPC 持久化另作玩法决定。** 现有“恢复资产、按日程归位、清理临时会面和经历”作为当前行为对照；未来保存经历、承诺或交付凭据时，先定义稳定身份、容量、版本迁移、恢复阶段和同一次恢复内的去重，再变更 schema。与当前资产快照有关联的记录应来自同一保存时点。

本轮建议足以收紧下一项修复的实施范围；完整 Agent 存档仍有明确的产品与失败恢复问题待完成，不能仅凭七项来源宣称设计已经全部确定。
