# Agent 运行对象装配与生命周期核验

- 日期：2026-09-15。
- 任务：[明确 Agent 会话的对象装配边界](https://github.com/qinjunw/CozyTown/issues/85)。
- 冻结代码：`6cbf604c6249f811e75a7b4202325cc1f99575fd`，承接[续谈修复验证](../verification/npc-conversation-turn-2026-09-14.md)。
- 本轮修改架构、ADR、PRD 状态和后续方案；生产代码与测试代码未修改，新增真实模型调用为 0。

## 结论与范围

保留现有场景局部装配，将其限定为 `CozyTownTownLifeController` 的职责：默认组合根创建世界服务，Bootstrap 注入时间和资源窄端口，控制器创建依赖实际居民的世界、会面板与调度器。这里的运行对象图跨越多次会面和模型请求，不是一场对话的临时对象集合。

当前没有第二个运行宿主需要复用整套场景装配。将这些对象移入 Runtime 组合根，会把居民配置、实际到场、可达性、观察回调和场景初始化时机加入默认服务工厂；只增加转发工厂不能解决生命周期问题。因此本轮不引入容器、工厂接口或 NPC 基类，采用[架构第 6 节](../ARCHITECTURE.md#6-组合边界)及 [ADR-0016 补充决策](../adr/0016-bounded-autonomous-decisions.md)明确例外。

这不是对所有权路径的全面通过结论。源码追踪发现两项需独立修复的问题：换绑资源校验失败可能留下部分替换；运行中重配不会继承旧调度器预算及在途计数。后文区分静态发现、已运行验证和未覆盖范围。

## 所有者与引用

下列代码链接固定于本轮审计提交；20 个重点源码、程序集定义及测试文件与工作区、隔离工程的内容比较见[证据清单](../verification/npc-session-composition-2026-09-15/manifest.json)。

| 对象 | 创建与持有 | 借用及释放边界 |
| --- | --- | --- |
| 默认经济状态、时间、生产、存档 | [CozyTownCompositionRoot](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Runtime/Core/CozyTownCompositionRoot.cs#L94)创建；`CozyTownServices` 汇集引用，Bootstrap 私有持有 | `WorldTimeCoordinator`、存档与 `CharacterResourceTrading` 使用同一经济状态；控制器不创建第二份仓库或世界钟 |
| 时间流与角色资源用例 | 默认组合根创建；[Bootstrap.BindTownLife](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Unity/Core/CozyTownBootstrap.cs#L304)同时注入 | 控制器订阅时间并借用资源用例；会面板借用资源用例；都不取得完整服务集合 |
| `NpcAgentWorld` | [Controller.Bind](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Unity/Npc/CozyTownTownLifeController.cs#L61)根据居民日程创建 | 居民身体、会面板、调度器持有同一世界；可达性通过普通回调返回，世界不持有 Unity 身体类型 |
| `NpcMeetingBoard` | [Controller.ConfigureDecisions](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Unity/Npc/CozyTownTownLifeController.cs#L99)在传入计划时创建 | 持有世界、资源用例、到场回调及本人会面经历；重配时旧会面 `CancelAll`，世界代次变化时清空会面与经历 |
| `NpcDecisionScheduler` | 控制器创建、推进和 Dispose | 借用世界、会面板、模型客户端及观察回调；拥有等待队列、预算窗口、任务与取消源。`BindWorld` 保留这些调度状态 |
| 观察与会面表现 | 控制器配置场景观察；存在会面板时创建或复用 `NpcMeetingDialogueView` | 观察从实际居民身体采样。停止会面配置会销毁 View；单独删除控制器不自动等于删除 View |
| `INpcDecisionClient` 与 HTTP 传输 | Bootstrap 或调用方提供客户端；[ProxyNpcDecisionClient](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Unity/Npc/ProxyNpcDecisionClient.cs#L14)默认复用静态 `HttpClient`，也接受外部注入 | 决策接口只有只读请求和取消令牌，没有 Dispose；每次请求、响应与流独立释放。外部注入的传输对象由调用方管理 |

各 NPC 继续以独立配置和状态组织。Runtime 程序集禁止 Unity 引用；模型客户端取得候选请求副本，不能通过这些装配入口取得经济状态写引用。

## 启动、重建与结束

1. **启动与注册**：Bootstrap 可以预先接受客户端配置，初始化后禁止通过自身 `ConfigureDecisions` 改配置。早注册或晚注册都会绑定时间与资源；重复注册同一控制器且决策已启用时，不重复安装调度器。不同控制器的重复注册会被拒绝。
2. **重复绑定**：同一时间流且资源引用未变、已有世界状态时，控制器保留世界、活动及调度器。`resources = null` 表示保留旧资源，不表示清空资源；API 不自动校验时间与资源是否来自同一默认服务图，调用方应成对注入。
3. **成功读档**：[DaytimeClockCoordinator.Load](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Runtime/Application/DaytimeClockCoordinator.cs#L106)在载入成功后发布重建版本。控制器保留世界对象和调度器，世界生成新代次、清空活动与事件，会面板清空旧会面与经历，身体重建位置。下一次调度 Tick 拒绝旧世界请求，旧 Task 未结束时继续占用真实在途限制，现实时间预算不因读档重置。
4. **成功换绑**：控制器创建新居民世界并切换时钟订阅；会面板改绑新世界与显式资源，调度器 `BindWorld` 保留预算与任务。换绑同样不保证网络请求即时停止。此描述仅适用于成功完成的绑定，失败分支见下一节。
5. **运行中重配**：控制器先构造候选会面板和调度器，再 Dispose 旧调度器、取消旧会面、切换引用并刷新活动。旧会面可释放，但新调度器预算从空窗口开始；这不是读档或 `BindWorld`。
6. **停用与销毁**：停用组件会停止自动 `Update`，但不解除显式时间通知。控制器 `OnDestroy` Dispose 调度器并解除订阅；[调度器 Dispose](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Runtime/NpcAgents/NpcDecisionScheduler.cs#L111)要求任务取消，并在任务结束后清理取消源，不等待真实网络调用完成，也不继续应用回复。控制器销毁不负责销毁借用的默认服务或客户端。

## 静态发现与后续验收

### 换绑失败可能发布部分对象图

`Controller.Bind` 第 65 行先替换资源引用，第 83—87 行解除旧订阅、替换世界、重绑居民和设置新时间，然后第 88 行调用 `Apply`。`Apply` 第 202 行调用 `MeetingBoard.BindWorld`；[会面板第 123 行入口](https://github.com/qinjunw/CozyTown/blob/6cbf604c6249f811e75a7b4202325cc1f99575fd/Assets/CozyTown/Runtime/NpcAgents/NpcMeetingBoard.cs#L123)会在新资源缺少计划参与者时抛错，此时会面板尚未替换自己的世界。控制器后续的调度器改绑及新时钟订阅没有执行。

由此可推导，使用缺少会面参与者的新资源对象换绑时，控制器与居民已引用新世界，调度器和会面板仍引用旧世界，旧订阅已解除而新订阅尚未建立。更早的居民配置校验失败也可能发生在资源引用替换之后。本轮结论来自源码顺序，尚未新增失败测试复现。

后续由[确保 Agent 换绑失败时保留原对象图](https://github.com/qinjunw/CozyTown/issues/87)先从公开绑定、时钟、状态、会面和客户端入口验证 RED，再做最小修复。验收要求非法资源或居民配置不改变旧资源、代次、活动、会面、预算及真实在途约束；失败后旧时钟继续有效，合法重试可以切换。合法同钟换资源、换世界换资源、重复绑定和读档作为对照。本轮不以新增装配抽象掩盖该失败路径。

### 决策重配丢失跨实例预算与在途计数

重配会创建新调度器，其预算集合与居民任务状态不继承旧实例。旧 Dispose 使用取消令牌和完成回调清理；若客户端仍在运行，新实例无法把它计入自己的并发槽。已有重配测试采用同步客户端，证明旧会面活动得到释放，不能证明异步请求的跨实例约束。

后续由[约束运行中决策重配的预算与在途请求](https://github.com/qinjunw/CozyTown/issues/88)处理，依赖前述换绑修复。先设置小预算和并发 1，用忽略取消的可控客户端保留旧任务，重配后通过公开世界事件驱动新机会，观察实际调用和诊断；再决定共享计数或拒绝／延迟不安全重配的最小方案。需覆盖旧回复成功或失败后迟到、重复重配、非法配置、会面释放及新等待轮次，不借提高预算消除问题。

### 尚未纳入首版的场景热替换

控制器 `OnDestroy` 没有 `MeetingBoard.CancelAll` 或销毁会面 View。当前名为 `DestroyingTheController_CancelsItsOutstandingDecision` 的测试实际销毁整个世界 GameObject，不能证明只删除控制器组件、保留身体和 View 时活动、预约与气泡都会释放。View 的气泡过期处理依赖 `Present`，其自身 `OnDestroy` 则负责销毁气泡。

当前没有批准跨场景保留 Agent 对象图、运行中更换居民集合或单组件热删除的玩法需求。本轮标记为未验收范围；需要这类能力时，先确定剩余身体、共享服务、预算和表现对象由谁接管，再登记独立验收。

## 本轮验证与证据边界

使用 Unity `6000.5.5f1` 在既有隔离工程顺序运行两批现有测试，PlayMode 使用图形模式，均未传 `-quit`。没有修改测试或运行完整套件。

| 本轮运行 | 结果 | 支持的判断 | 不支持的推论 |
| --- | --- | --- | --- |
| [PlayMode XML](../verification/npc-session-composition-2026-09-15/session-composition-01-play-2026-09-15.xml) | 22 通过，0 失败，0 跳过；Unity 退出 0 | 早／晚与重复注册、实际身体移动、重复绑定、合法换时钟、读档与预算、停用时的显式推进、整对象销毁取消、同步重配释放活动 | 非法换绑原子性、异步重配跨实例预算、单组件热删除 |
| [EditMode XML](../verification/npc-session-composition-2026-09-15/session-composition-02-edit-2026-09-15.xml) | 4 通过，0 失败，0 跳过；Unity 退出 0 | 读档后旧请求保留物理槽与预算、Dispose 后禁止世界写入、会面板换世界使用新资产、旧世界回复排空后新轮次派发 | 控制器及会面板联合换绑失败的回滚、真实服务收到取消后确实停算 |

复测筛选条件、逐项结果、原始 XML／日志摘要和发布 XML 摘要见[证据清单](../verification/npc-session-composition-2026-09-15/manifest.json)。原始日志留在本地，发布 XML 只替换机器路径并统一换行，测试用例属性未改变。[校验清单](../verification/npc-session-composition-2026-09-15/checksums.json)固定发布文件内容。

输入核验覆盖 1,026 个文件、341 个 C# 源文件和 6 份 Bee 源文件清单。与前次冻结输入逐字段比较，只有审计时间、工作区提交、来源提交和隔离路径四项元数据不同；全部输入记录、覆盖范围和两项设置例外一致，见[输入比较](../verification/npc-session-composition-2026-09-15/input-audit-comparison.json)。该比较引用前次完整 JSON，保留其摘要及相同字段的规范化摘要，避免复制相同的 1,026 项记录。

隔离工程继续沿用两项已声明例外：`ProjectSettings.asset` 的测试 PlayerSettings 配置，以及固定内容的 `SceneTemplateSettings.json`。设置内容及差异依据沿用[前次完整输入核验](../verification/npc-conversation-turn-2026-09-14/conversation-turn-compiled-source-audit-frozen.json)，本轮没有把它们描述为与源码提交逐字节一致。输入与 Bee 清单比较也不等于 DLL／PDB 源码校验和证明。

另核验前次预算复现的 19 个发布文件及续谈修复的 29 个发布文件，摘要全部保持不变。前次完整 EditMode 852、PlayMode 196 项与四世界 18 次真实模型调用是历史证据，本轮没有重跑，也不用于证明上述新发现已修复。

## 后续工作入口

本轮确定对象归属和修复范围，不修改默认经济组合、普通活动期限或事实表达默认值。先处理[确保 Agent 换绑失败时保留原对象图](https://github.com/qinjunw/CozyTown/issues/87)，再处理[约束运行中决策重配的预算与在途请求](https://github.com/qinjunw/CozyTown/issues/88)。四人负载和跨日持久化按地图依赖继续；正式表达方式仍需用户决策。领取、交付分支和 PR 合并状态以任务的 Resolution 与 [GitHub 地图](https://github.com/qinjunw/CozyTown/issues/50)为准。
