# NPC 自主决策代理协议

本协议服务于[有界自主决策](adr/0016-bounded-autonomous-decisions.md)。玩家对话继续使用既有对话协议；两个端点分别配置。代理负责将结构化请求交给模型，并返回一个操作候选。Unity 宿主负责触发、预算、披露和执行检查。

## 请求与披露

每次调用使用 HTTP POST、UTF-8 `application/json`，请求上限 32 KiB，响应上限 16 KiB。请求只包含当前居民的人设、状态、已知地点和本次决策相关信息。

| 字段 | 含义 |
| --- | --- |
| `schemaVersion` | 普通活动为 `1`，普通会面为 `2`，资源会面为 `4`；响应解析兼容旧资源版本 `3` |
| `decisionId` | 宿主生成的决策标识，同一次按需查询续调保持不变 |
| `npcId`、`displayName`、`persona` | 当前居民的身份与人设 |
| `worldRunId`、`revision` | 生成上下文时的世界代次及居民修订；宿主保留原值作执行检查 |
| `gameTotalMinutes` | 机会产生时的绝对游戏分钟 |
| `targetLocationId`、`activity` | 当时的目标与活动 |
| `triggers` | 合并后的相关事件，包含 `kind` 和 `gameTotalMinutes` |
| `knownLocationIds` | 本人日程涉及的地点 ID；不是全世界地点或其他居民资料 |
| `step`、`remainingCalls` | 当前调用序号和本次调用后剩余调用次数 |
| `allowedOperations`、`allowedActivities`、`maxActivityDurationGameMinutes` | 支持的操作、活动类型和临时活动期限上限 |
| `hasLocationDetails`、`locationDetails` | 为 `true` 时才读取详情对象的 `locationId`、`isReachable`；为 `false` 时忽略该对象 |
| `previousResultCode` | 同一世界内本人的上次决策结果码；没有时为空，不包含历史上下文链 |
| `candidateErrorCode` | 非空时表示本次决策的前一个候选因字段问题未执行；本次为唯一一次纠正机会，与上次已结束决策的结果分开 |
| `hasObservation`、`observation` | 为 `true` 时携带下述独立版本的角色观察；为 `false` 或字段缺省时沿用旧请求行为 |

地点可达性在返回 `inspect_location` 后披露：宿主检查地点属于本人已知范围，查询当前路线是否可达，并在下一次请求提供详情。局部观察在首个请求主动提供，独立于地点查询。原请求对象不变。每次续调都重新消耗请求预算；最后一次调用继续要求查询会结束为 `agent.decision_step_limit`。

## 局部观察 v1

外层行动版本保持 `1/2/4`，`observation.schemaVersion` 为 `1`。Unity 在首次实际外调前读取同一轮已提交的身体和世界状态。排队请求保留原 `gameTotalMinutes`、决策标识和期限，同时重新读取当前社交条件；需求已满足的未发送机会被移出队列。观察刷新本身不产生模型调用，仍由既有日程和会面事件触发决策。

| 观察字段 | 合同 |
| --- | --- |
| `observerId`、`worldRunId`、`listenerId` | 当前观察者、世界代次和本次社交对象；没有听众时为空字符串 |
| `observedAtGameTotalMinutes` | 实际采样时刻，可能晚于机会时刻；首次采样和执行前复核必须对应当时世界时刻 |
| `x`、`y`、`spaceId` | 实际身体坐标与室内／室外空间；目标和朝向不参与区域观察 |
| `hasRegion`、`regionId`、`radius` | 是否有匹配区域、区域稳定 ID 和距离阈值；未覆盖区域的 ID 为空 |
| `nearbyEntityIds`、`nearbyComplete` | 授权且同区域、距离内的已登记实体，最多 8 个，按 ID 排序；裁剪或未知区域时不完整 |
| `coverageDomain` | 固定为 `registered_entities_same_region_within_radius`；完整仅指这个有限目录和范围 |
| `facts` | 必要事实与附近实体事实；前者不受附近清单的条数裁剪影响，代理接受最多 64 条 |

每条事实包含 `factId`、`entityId`、`predicate`、`value`、`valueType`、`unit`、`knowledge`、`source`、`observerId`、`observedAtGameTotalMinutes`、`scope`、`speakerId` 和 `canExpress`。数值以不受区域设置影响的字符串传输，`valueType` 区分 `number/text/boolean/unknown`。未知值编码为空字符串，不能按零处理。

`knowledge` 区分 `observed`（采样事实）、`authored`（内容或条款）、`statement`（说过的原文）、`receipt`（已发生的交付）和 `unknown`。旧请求仍保留原采样时刻；当前请求不自动混入过去环境观察。参与者记录最多提供最近 4 条发言和最近一次仍在内存记录中的成功交付；发言不改写资产。当前会面的交付数量与价款只能在匹配该会面凭据时附带，旧凭据仅保留结果与时间。

本人相关物品量和余额来自实时经济读取，对方私有资产不进入投影。本次交易的本人相关物品量可标为向该参与者陈述；完整钱包余额仅用于私下决策，`canExpress=false`。没有当前听众时，私人记录和资源不标为可陈述。`terms_quantity/terms_price` 表示计划的交易条款，是否收到邀请、交付或交谈由阶段和凭据分别说明。附近居民的 `present` 来自身体现状，装饰的 `interaction=none` 来自显式内容，池塘的已登记鱼种数量为未知。目录交互名不增加 `allowedOperations` 中没有的行动。

首次采样失败、返回另一角色／世界／听众的数据，或没有取得当前时刻的观察时，宿主结束为 `agent.observation_unavailable`，不调用模型。执行候选或发出续调前复核同一授权视图；当前事实变化时结束为 `agent.observation_stale`，原候选与原观察保留在结果中。比较包括身体坐标、区域、可见清单、值、来源、权限和历史记录时刻；仅采样时间推进且事实不变不会失败。

只有 Invitation 阶段的 `accept_invite`／`decline_invite` 在已知同区域、同空间且其余观察相同时允许 X/Y 改变；两个未知区域仍严格比较坐标。跨区域、对象集合／能力、本人资产或其他已知事实变化继续拒绝。答复仍绑定原会面，并通过原角色修订、期限、库存、日程及占用检查；接受不表示实际到场或已经交付。`invite`、发言、地点行动及续调继续完整比较，身体移动后的旧台词仍会被拒绝。

`NpcDecisionOutcome.ExecutionObservation` 保存处理该候选时实际重读的快照；`Context.Observation` 始终保留模型看到的原快照。世界／修订／期限等提前门禁拦截时未执行重读，该字段为空；观察源失败时可为空或保留被拒绝的源数据。它仅供宿主诊断，不加入模型请求或其他居民记忆。

默认开发地图的配置位于 [TownLocalObservation2D](../Assets/CozyTown/Unity/Npc/TownLocalObservation2D.cs)：6 世界单位半径、住宅几何优先、4 个户外矩形和少量池塘／路灯对象。默认目录读取时复核对象是否仍启用并处于登记位置；自定义地图通过 `Configure(scene, interiorSpaces)` 提供不可变区域与对象。`CozyTownTownLifeController.GetObservation(npcId)` 是公开读取入口。

观察字段计入原 32 KiB 请求上限。代理只验证结构与身份绑定、传输事实，无法验证场景真假、当前时效或自然台词含义。后续表达对照见[专项规格](AGENT_LOCAL_OBSERVATION_SPEC.md)，本版仍返回自由 `say.text`。

## 响应

响应必须是 JSON 对象，版本与请求一致。普通活动使用 `schemaVersion: 1`，支持以下三种候选：

```json
{"schemaVersion":1,"operation":"wait"}
```

```json
{"schemaVersion":1,"operation":"inspect_location","locationId":"<known-location-id>"}
```

```json
{"schemaVersion":1,"operation":"visit","locationId":"<known-location-id>","activity":"resting","durationGameMinutes":20}
```

`visit` 仅支持 `working` 或 `resting`，期限必须是大于零且不超过 1440 的有限游戏分钟，从宿主接受时刻起计算。宿主再次检查世界代次、修订、机会有效期、忙碌状态、已知地点与路线可达性。查询结果不是预留或执行授权。接受候选后由现有身体和路线控制器移动；接受不表示已经到达或完成工作。

额外字段没有执行效果。响应不能选择执行者、修改金币或物品、推进时间或写入存档。未知操作、版本或缺少必要候选字段会失败；无效期限由活动仲裁拒绝。

## 触发与运行期限

居民处于默认休息窗口且没有临时活动时，世界重建、日期或日程变化、活动到期或取消可以产生一次机会。普通帧更新与活动接受不单独产生机会。工作与居家沿用日程。

默认每个调度器每现实分钟最多 8 次客户端调用，同时最多 2 次实际在途请求；每人最多 1 个当前决策，每次决策最多 2 次调用。居民冷却 30 秒，单次请求超时 8 秒，总决策超时 12 秒，排队与执行机会有效期 30 游戏分钟。这些值由 `NpcDecisionSettings` 配置。

失败和续调消耗预算。超时或取消结束逻辑决策，但未结束的客户端任务继续占用实际并发槽。迟到响应被丢弃。读档或绑定另一世界不会重置同一调度器的现实时间预算。显式重新配置决策客户端会创建新的调度器会话。

游戏时间暂停时，现实时间的请求期限仍继续推进，已产生的决策可以结束或接受候选；身体位移仍需要世界时间推进。关闭人物呈现不删除 Runtime 状态；销毁控制器则取消决策会话。

## 诊断与边界

默认内容启动器在非批量运行时读取 `COZYTOWN_AGENT_PROXY_ENDPOINT`。未配置时不启用自主模型调用；配置值应是实现本协议的完整 HTTP(S) 端点。批量测试不自动读取该端点，不会因机器环境配置而启动后台模型调用。该变量独立于玩家对话代理配置。

自定义服务或测试可在 `CozyTownBootstrap.Initialize` 前调用 `ConfigureDecisions(client, profiles, settings, meetingPlans)`，或在已绑定世界时间的 `CozyTownTownLifeController` 上显式配置。省略会面计划时保留普通活动模式。默认内容通过环境变量启用代理时加载 Sora 缺鱼联系 Ren 的资源会面计划；普通散步计划仍可显式配置。启动器同时绑定角色经济服务，支持人物控制器早注册和晚注册，重复注册不创建新调度器。角色配置 ID 必须与控制器注册的居民一致。

`CozyTownTownLifeController` 提供请求累计数、当前滚动窗口次数、实际在途数和等待居民数；`GetDecisionOutcome(npcId)` 返回本人最近一次决策的上下文、最终候选、结果码、调用次数和现实时间。失败或取消时可能没有候选。该记录保留在内存中，不是跨日存档或模型用量报告。

协议解析失败报告 `agent.response_invalid`，请求失败报告 `agent.client_failure`；超时、世界失效、修订失效和机会过期分别使用对应的 `agent.*` 结果码。默认日程在请求期间和失败后继续运行。客户端应异步返回任务并响应取消；同步阻塞客户端会阻塞宿主帧，不能靠调度器自身解除。

本地模型代理位于 [Tools/agent_proxy](../Tools/agent_proxy/README.md)。固定客户端与 HTTP 边界测试验证协议规则；真实模型联调单独启用和记录，不能据一次演示推断长期成功率或平均费用。

## 会面协议 v2

会面计划是宿主配置的机会模板，包含参与者、两处站位、邀请窗口、约定开始时刻、持续上限与对话轮数。每个计划每天最多自动提出一次机会，普通帧不会重复触发。机会产生不等于已经邀请；双方仍需分别提交邀约和答复候选。

请求增加 `social` 对象，其中 `kind` 为 `opportunity`、`invitation` 或 `conversation`；`planId`、`partnerId`、`placeId`、`locationId` 说明本次机会与本人的站位。宿主生成的 `meetingId` 从邀请阶段开始披露。`startsAtTotalMinutes`、`deadlineTotalMinutes` 和 `maxTurns` 给出时间与轮数限制。`transcript` 只含本次会面已经接受的发言；`memories` 最多披露本人最近 4 条实际会面事件，不包含对方私人记忆、人设或资产。会面阶段不披露普通地点目录，`knownLocationIds` 和 `allowedActivities` 为空。

| 阶段 | `allowedOperations` | 候选参数 |
| --- | --- | --- |
| 邀约机会 | `invite`、`wait` | `invite` 携带当前 `planId` |
| 收到邀请 | `accept_invite`、`decline_invite` | 当前 `meetingId` |
| 轮到本人发言 | `say` | 当前 `meetingId` 和非空 `text`，最多 240 字符 |
| 至少已有两句实际发言，且轮到本人 | `say`、`end_conversation` | 结束携带当前 `meetingId` |

```json
{"schemaVersion":2,"operation":"invite","planId":"ren-sora-pond-walk"}
```

```json
{"schemaVersion":2,"operation":"say","meetingId":"<host-meeting-id>","text":"The pond is quiet today."}
```

宿主从原请求绑定执行者、世界代次和修订，并验证候选操作属于当时披露的集合。模型不能替对方接受，不能安排未披露的会面，也不能声明已经到场。每次实际会面事件提升双方修订，旧轮次的回复不能在下一轮重放。

接受邀请时，宿主先验证双方未来日程、忙碌状态、路线和地点预约，再原子提交两项未来活动。现有工作一直持续到约定开始。默认 Ren 在 12:15 获得机会，邀请 Sora 于 13:00 开始沿池塘散步；邀请截止 13:15。接受后的承诺最长持续 150 游戏分钟，双方实际到场才开放交谈，最多 4 轮。场景中的会面对话框显示宿主已经接受的台词。

邀请答复与对话轮次不受普通居民 30 秒冷却限制，但仍消耗同一全局速率预算与并发槽，并保留请求、决策、上下文及会面期限。等待预算、请求失败或模型输出无效不会自动无限重试；未完成会面由游戏时间期限释放。

每个 NPC 同时最多参与一项会面，同一逻辑地点在承诺期间独占。结束、拒绝、到期、路径失败、玩家交互打断或活动被替换时，宿主释放属于该会面的活动与地点，恢复当前时刻的日程，保留外部新活动。读档或换世界清空会面与记忆，不把旧请求结果写入新世界；同一调度器的现实时间预算保持不变。

`GetMeeting(npcId)` 提供当前或最近结束的不可变会面快照，`GetMeetingMemories(npcId)` 提供最多 16 条本人实际事件。对话里提到食谱或鱼不产生物品、金币或任务完成记录。资源会面另行记录宿主交付结果；会面记忆的持久化属于后续迭代。

## 资源会面（当前版本 4，兼容版本 3）

资源会面沿用版本 2 的身份、时间、地点、邀请和轮次字段，增加 `social.resources`：`sellerId`、`buyerId`、`itemId`、`quantity`、`totalPrice` 为宿主固定条款；`ownedQuantity` 和 `balance` 仅表示当前居民的相关物品数量和余额；`deliveryResultCode` 是宿主最近确认的本次交付结果。对方完整资产不披露。资源机会由缺料条件与配置时间窗口共同触发，可在工作期间提出未来会面，赴约仍等待约定时刻。

买方发起邀请、卖方接受后形成双边承诺。实际到场后，买方收到 `social.kind: delivery`，可用操作仅为 `deliver`、`cancel_exchange`：

```json
{"schemaVersion":4,"operation":"deliver","meetingId":"<accepted-meeting-id>"}
```

```json
{"schemaVersion":4,"operation":"cancel_exchange","meetingId":"<accepted-meeting-id>"}
```

这些候选不接受替换资产或价款的字段。宿主重新验证当前资产、期限和到达状态后，一次提交双方库存与钱包，并记录 `resource.delivered`，之后才开放交谈。失败时无部分转移，释放会面活动并回到日程。同一当前或最近会面的已交付编号重试只确认已有结果；旧世界编号被拒绝。`GetMeetingResources(npcId)` 可查看本人在当前或最近资源会面中的相关资产，`GetMeeting(npcId).DeliveryResultCode` 区分实际交付与会面结束。

## 自方资源条件（版本 4）

版本 4 保留版本 3 的资源条款和候选参数，增加顶层 `hasSelfAssessment` 与 `selfAssessment`。仅在 `hasSelfAssessment` 为 true 时读取该对象；普通活动、纯社交及已交付对话忽略它。对象字段如下：

| 字段 | 含义 |
| --- | --- |
| `role` | 本人为 `buyer` 或 `seller` |
| `canMeetKnownTerms` | 本人当前付款或供货是否足够；不等同于双方可以完成交易 |
| `missingCoins`、`missingQuantity` | 买方付款缺口或卖方供货缺口，无关义务为 0 |
| `reasonCode` | 不足时为 `wallet.insufficient_funds` 或 `inventory.insufficient_quantity` |
| `scope` | 只检查本人付款或库存，对方条件、容量等仍未检查 |

例如买方只有 10 金币，需要支付 25 时，缺口为 15，机会阶段 `allowedOperations` 只有 `wait`。卖方缺货时邀请阶段只有 `decline_invite`，交付前买方缺钱时只有 `cancel_exchange`。足够时保留原阶段的两种操作。候选必须使用原请求中的允许集合和标识，不可改写自己或对方的资产。

邀约和接受提交时重新读取本人资产；资产在模型请求期间减少不会因角色修订未变而绕过检查。交付继续验证实时双方资产、容量及到场，不依赖此前快照。交易完成后 `hasSelfAssessment` 为 false，不能因余额或库存下降而否定已交付事实。

模型输出仍返回与请求一致的 `schemaVersion: 4`。字段问题的有界纠正见下节。等待放弃本次日内机会，稍后补足资源不会自动唤醒；被拒绝的非法接受也不等于角色主动拒绝，已有邀请由原期限释放。

## 候选字段校验与一次纠正

2026-09-13 的后续实施为 v1／v2／v4 请求增加可选反馈字段 `candidateErrorCode`。非空时，前一个候选未执行，模型应返回一份新的合法候选。版本和操作必须匹配原请求；计划 ID 精确匹配，非零会面 GUID 按值匹配（支持宿主 N 格式及标准 D 格式），地点必须属于本人已知集合。必填参数位于顶层，宿主和代理均不自动代填或移动嵌套字段。

代理校验失败时返回 HTTP 422，例如：

```json
{"error":"provider.candidate_invalid","candidateErrorCode":"candidate.plan_id_required"}
```

| 可纠正错误码 | 字段要求 |
| --- | --- |
| `candidate.plan_id_required` | `planId` 是非空字符串 |
| `candidate.location_id_required` | `locationId` 是非空字符串 |
| `candidate.meeting_id_required` | `meetingId` 是非空字符串 |
| `candidate.meeting_id_invalid` | `meetingId` 能解析为非零 GUID |
| `candidate.activity_invalid` | `activity` 为允许的工作或休息类型 |
| `candidate.duration_invalid` | `durationGameMinutes` 为大于零、有限且不超过请求上限的 JSON 数值 |
| `candidate.text_invalid` | `text` 是非空字符串，最多 240 个 UTF-16 代码单元 |
| `candidate.speech_frame_invalid` | 结构化发言字段齐全，意图和语气属于有限集合，没有额外字段 |

`candidate.schema_mismatch`、`candidate.operation_unavailable`、`candidate.plan_id_mismatch`、`candidate.meeting_id_mismatch` 和 `candidate.location_unknown` 表示请求绑定或可用集合不符，不触发纠正。普通 JSON 解析失败、过大正文、未知错误包和提供方故障也不触发纠正。Unity 与代理均不把字符串期限或浮点版本强制转换为有效参数。

宿主最多纠正一次：保留原 `decisionId`、世界代次、修订、机会时间及允许集合，`step` 增加 1。默认每决策总共仍是两次调用，与地点查询共用；纠正没有额外预算，也不重置总决策、机会或会面期限。HTTP 客户端和代理自身不重试。所有响应依然经过宿主实时状态检查。

查询成功后的续调清除本次 `candidateErrorCode` 反馈，但仍保留“本决策已经纠正过”的限制；即使配置更多调用次数，也不能再纠正第二次字段错误。

结果 `CandidateErrorCodes` 按发生顺序保留候选诊断，即使最终候选成功也不删除首次失败。场景记录的 `candidateErrorCode` 标明该次候选失败，`decisionOutcomeCode` 则记录整个决策的终态；不能把终态成功当成每次请求均已执行。

## 表达实验协议 v1

请求明确携带 `expression: {schemaVersion: 1, mode: "free_text"}` 或 `mode: "structured_facts"`；行动协议仍为原 v1／v2／v4。代理兼容缺少 `expression` 的旧自由表达请求。普通游戏默认自由表达；结构化模式由宿主配置，实验结果不自动改变默认策略。

候选顶层 `schemaVersion` 必须复制请求的最外层版本，不取 `expression.schemaVersion` 或 `observation.schemaVersion`。例如资源请求最外层为 `4`、表达小节为 `1`，结构化 `say` 仍返回最外层 `4`。S 提示词明确这一区别；代理与宿主继续拒绝不匹配版本，不自动改写原候选。

F 模式继续使用 `say.text`，禁止同时携带结构化帧字段。S 模式的 `say` 恰好包含以下六个顶层字段：

```json
{"schemaVersion":4,"operation":"say","meetingId":"<host-meeting-id>","speechIntent":"report_observation","factId":"npc.cook_sora:region_name","tone":"neutral"}
```

`speechIntent` 为 `report_observation`、`report_receipt`、`recall_statement`、`acknowledge_unknown`、`ask_about` 或 `express_wish`；`tone` 为 `neutral`、`warm` 或 `brief`。模型选择单个 `factId`，数值、主体、单位、时间和实体名称均由宿主事实确定。S 没有自由尾句，生成后的文本不再交给模型润色。

| 引用 | 合法用途 |
| --- | --- |
| `observation.facts` 中的真实 ID | 按事实知识类型、来源、时间、表达许可及支持的谓词选择意图 |
| `@partner_assets` | 在未获知当前听众资产时承认未知，或询问该信息 |
| `@nearby_coverage` | 表达当前观察范围；仅在区域未知或列表不完整时承认范围不足 |
| `@talk`、`@learn_cooking` | 表达聊天或学习烹饪的愿望，不暗示已发生的经历 |

`NpcFactSpeech.TryRender` 生成最长 180 字符的整句；它可以拒绝未知引用、私有事实、错误知识类型、未来时间、单位不匹配或过长的声明引用。对当前已知数量选择“承认未知”会被拒绝。声明引用保留原话、说话者和时间，成功收据只表达已记录的过去交付。范围描述不把局部列表扩大为全镇真相。

模式混用返回不可纠正的 `candidate.expression_mode_mismatch`；直接 Runtime 客户端混用由宿主以 `speech.mode_mismatch` 拒绝。S 字段形状错误可在原预算中纠正一次；事实语义错误以 `speech.*` 结束本次决策，不增加调用。最终执行仍先复核世界、轮次、期限与当前观察；过期事实返回 `agent.observation_stale`。

候选中的 `SpeechFrame` 与实际会面文本分开保存。只有宿主接受的句子进入会面对话框和参与者经历。单独解析 JSON 不构成行动授权；游戏客户端解析时传入原请求绑定模式、身份与操作，最终由调度器和会面入口执行。
