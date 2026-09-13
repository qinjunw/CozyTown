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

第一步只有地点 ID。返回 `inspect_location` 后，宿主检查地点属于本人已知范围，查询当前路线是否可达，并在下一次请求披露该地点的详情。原请求对象不变。每次续调都重新消耗请求预算；最后一次调用继续要求查询会结束为 `agent.decision_step_limit`。

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

`candidate.schema_mismatch`、`candidate.operation_unavailable`、`candidate.plan_id_mismatch`、`candidate.meeting_id_mismatch` 和 `candidate.location_unknown` 表示请求绑定或可用集合不符，不触发纠正。普通 JSON 解析失败、过大正文、未知错误包和提供方故障也不触发纠正。Unity 与代理均不把字符串期限或浮点版本强制转换为有效参数。

宿主最多纠正一次：保留原 `decisionId`、世界代次、修订、机会时间及允许集合，`step` 增加 1。默认每决策总共仍是两次调用，与地点查询共用；纠正没有额外预算，也不重置总决策、机会或会面期限。HTTP 客户端和代理自身不重试。所有响应依然经过宿主实时状态检查。

查询成功后的续调清除本次 `candidateErrorCode` 反馈，但仍保留“本决策已经纠正过”的限制；即使配置更多调用次数，也不能再纠正第二次字段错误。

结果 `CandidateErrorCodes` 按发生顺序保留候选诊断，即使最终候选成功也不删除首次失败。场景记录的 `candidateErrorCode` 标明该次候选失败，`decisionOutcomeCode` 则记录整个决策的终态；不能把终态成功当成每次请求均已执行。
