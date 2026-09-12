# NPC 自主决策代理协议

本协议服务于[有界自主决策](adr/0016-bounded-autonomous-decisions.md)。玩家对话继续使用既有对话协议；两个端点分别配置。代理负责将结构化请求交给模型，并返回一个操作候选。Unity 宿主负责触发、预算、披露和执行检查。

## 请求与披露

每次调用使用 HTTP POST、UTF-8 `application/json`，请求上限 32 KiB，响应上限 16 KiB。请求只包含当前居民的人设、状态、已知地点和本次决策相关信息。

| 字段 | 含义 |
| --- | --- |
| `schemaVersion` | 普通活动为 `1`，会面上下文为 `2` |
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

自定义服务或测试可在 `CozyTownBootstrap.Initialize` 前调用 `ConfigureDecisions(client, profiles, settings, meetingPlans)`，或在已绑定世界时间的 `CozyTownTownLifeController` 上显式配置。省略会面计划时保留普通活动模式。默认内容通过环境变量启用代理时同时加载 Ren 与 Sora 的会面计划。启动器支持人物控制器早注册和晚注册，重复注册不创建新调度器。角色配置 ID 必须与控制器注册的居民一致。

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

`GetMeeting(npcId)` 提供当前或最近结束的不可变会面快照，`GetMeetingMemories(npcId)` 提供最多 16 条本人实际事件。这里只记录邀请、接受、到场、发言和结束等宿主事件；对话里提到食谱或鱼不产生物品、金币或任务完成记录。资源协作与跨存档记忆分别属于后续迭代。
