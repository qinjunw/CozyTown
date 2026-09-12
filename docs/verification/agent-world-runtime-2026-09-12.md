# Agent 世界首轮运行基础验证

- 日期：2026-09-12。
- 票据：[实现世界事件与人物活动仲裁](../wayfinder/agent-world/tickets/world-events-and-activity-arbitration.md)。
- 决策：[ADR-0015](../adr/0015-resident-activity-arbitration-and-world-epochs.md)。
- 环境：Unity `6000.5.5f1`，Test Framework `1.7.0`；当前共享工作区，参照提交 `207e42c4f5962154417d7315d179bde130504a12`。
- 证据：[验证数据与文件校验值](agent-world-runtime-2026-09-12.json)。工作区包含其他未提交修改，参照提交不等于本次完整测试快照。

## 已实现行为

`Runtime/NpcAgents` 按 NPC ID 分别保存日程、当前临时活动、状态修订和语义事件。现有 `CozyTownTownLifeController` 绑定居民时建立此运行模块，`NpcWorldResident2D` 从中取得目标，并使用现有路线推进实际身体。

有期限的活动可以改变一名居民的目的地，不会瞬移、被日程覆盖或重置其他居民的行走动画。到期和取消都恢复当前时刻的日程。世界重建清除临时活动并创建新运行代次，以拒绝旧世界请求。

事件通知按角色隔离，同类未消费通知仅保留最新一次。跳时后的日程事件反映推进前后的目标差异，不补发中间已经失效的活动机会；实际移动仍在日程和活动边界分段。

## 测试结果

| 检查 | 结果 | 本地原始报告 |
| --- | --- | --- |
| 修改前既有日程基线 | 69/69 通过，0 跳过 | `Logs/agent-world-baseline-editmode.xml` |
| 最终完整 EditMode | 514/514 通过，0 跳过 | `Logs/agent-world-full-editmode.xml` |
| 最终完整图形 PlayMode | 164/164 通过，0 跳过 | `Logs/agent-world-full-playmode.xml` |
| 新增 Runtime 用例，已含在完整 EditMode 中 | 19/19 通过 | `NpcAgentWorldTests` |
| 新增 Unity 活动用例，已含在完整 PlayMode 中 | 13/13 通过 | `NpcAgentActivityPlayModeTests` |

测试从公开入口观察行为：角色快照、通知、拒绝码、目的地、实际位置、路线状态和动画精灵。覆盖分数分钟、到期分段、取消、重复与忙碌请求、不可达目标、无效期限、同一世界重绑、更换世界、成功及失败读档、禁用表现组件时继续走时。

首次完整回归即通过。没有使用此前其他任务的全量结果替代本轮证据，也未调用真实模型。

## 先失败后修复的证据

| 行为 | 实际失败 | 修复后的目标回归 |
| --- | --- | --- |
| 时间变化产生独立角色事件 | 首个用例因入口尚未实现而失败 | `agent-world-events-green`：1/1 |
| 接受临时活动 | `SubmitActivity` 未接受有效请求 | `agent-world-activity-green`：2/2 |
| 活动驱动 Unity 目的地 | 活动已接受，但身体仍以 `work` 为目标 | `agent-world-movement-green`：26/26，包含既有居民生活用例 |
| 分数分钟后睡眠 | 预期 Y=59.5，实际 Y=58.5；首个小段错误恢复旧日程 | `agent-world-sleep-green`：3/3 |
| 角色间动画隔离 | Mina 的活动将 Ren 的 `Walk 5` 重置 | `agent-world-isolation-green`：4/4 |

动画隔离用例第一次运行遇到测试文件 `using` 位置造成的编译错误，修正后在 `agent-world-isolation-red2` 中确认行为失败。编译失败不计作行为 RED。`agent-world-fraction-red` 的两个用例实际均通过，仅用于验证直接推进时间的既有行为。

## 接入方式与约束

已有场景的居民生活控制器在 `Bind(IWorldTimeFlow)` 时自动建立运行模块，无需新增场景对象。可信游戏调用方使用下列入口：

| 入口 | 用途 |
| --- | --- |
| `GetAgentState(npcId)` | 取得独立快照、当前目标、活动、世界运行代次和修订号 |
| `TakeAgentEvents(npcId)` | 消费该居民待处理通知；再次读取为空，直至出现新通知 |
| `SubmitActivity(request)` | 使用快照代次与修订号提交目的地、工作／休息类型和绝对截止分钟 |
| `CancelActivity(npcId, worldRunId, expectedRevision)` | 使用最新快照取消活动，恢复当前日程 |

截止分钟使用 `IWorldTimeFlow.Current.TotalMinutes` 的累计时间轴，允许持续时间大于零且不超过 1440 游戏分钟。每人最多一个临时活动；改变现有安排需要先取消，再基于新快照提交。

这些入口在绑定世界的 Unity 主线程串行调用。后续异步模型结果需要回到该线程，再通过代次、修订和目标检查提交；本模块不提供并发线程同步。移动后的到达或受阻由 `NpcWorldResident2D.Status` 观察，接受请求本身不表示到达或完成生产。

运行测试时，将本地 Unity 路径和仓库路径作为环境配置传入：

```powershell
& $env:UNITY_EDITOR_PATH -batchmode -nographics -projectPath $env:COZYTOWN_PROJECT_PATH -runTests -testPlatform EditMode -testResults Logs/agent-world-full-editmode.xml -logFile Logs/agent-world-full-editmode.log
& $env:UNITY_EDITOR_PATH -batchmode -projectPath $env:COZYTOWN_PROJECT_PATH -runTests -testPlatform PlayMode -testResults Logs/agent-world-full-playmode.xml -logFile Logs/agent-world-full-playmode.log
```

两次运行依次执行。PlayMode 保留图形设备；两者均不加 `-quit`。原始 XML／日志保存在本地 `Logs`，纳入版本管理的 JSON 保存汇总、校验值和新增用例结果。

## 后续工作

本轮覆盖 PRD 中世界与角色状态、活动仲裁及重建隔离的基础部分，尚不构成任一完整自主社交验收。临时活动尚不持久化，成功读档按既有日程重建位置。

下一票据实现事件触发、模型预算、渐进式上下文和受检查决策；随后实现用户选定的 Ren 与 Sora 自主邀约、见面交谈和恢复日程。到达事件、社交会话、地点预约、资源交付、个人记忆和真实模型效果分别按后续票据验证。
