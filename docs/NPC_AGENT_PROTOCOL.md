# NPC 自主决策代理协议 v1

本协议服务于[有界自主决策](adr/0016-bounded-autonomous-decisions.md)。玩家对话继续使用既有对话协议；两个端点分别配置。代理负责将结构化请求交给模型，并返回一个操作候选。Unity 宿主负责触发、预算、披露和执行检查。

## 请求与披露

每次调用使用 HTTP POST、UTF-8 `application/json`，请求上限 32 KiB，响应上限 16 KiB。请求只包含当前居民的人设、状态、已知地点和本次决策相关信息。

| 字段 | 含义 |
| --- | --- |
| `schemaVersion` | 固定为 `1` |
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

响应必须是 JSON 对象，且包含 `schemaVersion: 1`。支持以下三种候选：

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

默认每个调度器每现实分钟最多 8 次客户端调用，同时最多 2 次实际在途请求；每人最多 1 个当前决策，每次决策最多 2 次调用。居民冷却 30 秒，单次请求超时 8 秒，总决策超时 12 秒，排队与执行机会有效期 30 游戏分钟。这些值由 `NpcDecisionSettings` 配置，尚未经过真实模型体验调优。

失败和续调消耗预算。超时或取消结束逻辑决策，但未结束的客户端任务继续占用实际并发槽。迟到响应被丢弃。读档或绑定另一世界不会重置同一调度器的现实时间预算。显式重新配置决策客户端会创建新的调度器会话。

游戏时间暂停时，现实时间的请求期限仍继续推进，已产生的决策可以结束或接受候选；身体位移仍需要世界时间推进。关闭人物呈现不删除 Runtime 状态；销毁控制器则取消决策会话。

## 诊断与边界

默认内容启动器在非批量运行时读取 `COZYTOWN_AGENT_PROXY_ENDPOINT`。未配置时不启用自主模型调用；配置值应是实现本协议的完整 HTTP(S) 端点。批量测试不自动读取该端点，不会因机器环境配置而启动后台模型调用。该变量独立于玩家对话代理配置。

自定义服务或测试可在 `CozyTownBootstrap.Initialize` 前调用 `ConfigureDecisions(client, profiles, settings)`，或在已绑定世界时间的 `CozyTownTownLifeController` 上显式配置。启动器支持人物控制器早注册和晚注册，重复注册不创建新调度器。角色配置 ID 必须与控制器注册的居民一致。

`CozyTownTownLifeController` 提供请求累计数、当前滚动窗口次数、实际在途数和等待居民数；`GetDecisionOutcome(npcId)` 返回本人最近一次决策的上下文、最终候选、结果码、调用次数和现实时间。失败或取消时可能没有候选。该记录保留在内存中，不是跨日存档或模型用量报告。

协议解析失败报告 `agent.response_invalid`，请求失败报告 `agent.client_failure`；超时、世界失效、修订失效和机会过期分别使用对应的 `agent.*` 结果码。默认日程在请求期间和失败后继续运行。客户端应异步返回任务并响应取消；同步阻塞客户端会阻塞宿主帧，不能靠调度器自身解除。

测试使用固定客户端和内存 HTTP 响应验证协议与宿主行为。仓库没有本协议的模型代理服务端；真实提供者延迟、输出质量、Token 用量和费用尚未测量。Ren 与 Sora 的邀请、赴约和交谈协议属于后继[自主会面任务](https://github.com/qinjunw/CozyTown/issues/53)。
