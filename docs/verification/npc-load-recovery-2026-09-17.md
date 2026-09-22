# 读档提交后的重建失败验证

- 日期：2026-09-17。
- 任务：[明确读档提交后重建失败的处理边界](https://github.com/qinjunw/CozyTown/issues/91)。
- 基线：`ee5f4d7ca387b9130d8e6e3a016095d458043ac9`。
- 执行：Unity `6000.5.5f1`，隔离工作树；EditMode 使用无图形批处理，PlayMode 保留图形设备。没有读取密钥或调用真实模型。

## 问题与实现

schema v3 的五个存档模块恢复后，NPC 重建通知仍会抛异常。首次三个公开用例分别在 NPC 前、后及动画提交中注入故障，均观察到异常逃出 `Load()`，不能得到明确的读档结果。

将全部 Unity 候选准备纳入提交前事务，需要新增跨层协议，仍无法保证最后的对象提交不抛错。本轮按 [ADR-0020](../adr/0020-post-commit-world-recovery.md)保留权威数据提交点，新增 `Ready`、`Publishing`、`RecoveryRequired`。关键 `Changed` 订阅逐个执行并收集错误，全部成功后才发显示通知。灯光、农田和畜牧显示转入 `PresentationChanged`，显示错误不阻止其他显示对象刷新。

读档的关键重建失败返回 `save.loaded_rebuild_required`；显示失败返回 `save.loaded_presentation_failed`。前者暂停 NPC 行动、存档覆盖和时间协调；修复故障后重新读取有效存档才恢复。暂停不撤销已经发生的模块恢复和部分身体更新。界面分别说明“数据已恢复、世界暂停”“显示刷新失败”或“恢复中断、状态待确认”。

调度器保留实际未完成 Task、冷却和真实时间调用窗口。暂停时清除旧逻辑候选、发出取消信号并回收已完成结果，不执行回复或消费新的世界事件。通知及恢复期间拒绝重入；模型和观察回调返回后重新检查运行许可。

原 `GameSaveCoordinator` 返回普通业务失败且补偿成功时，保持原执行代际和候选资格。回滚失败保留 `save.rollback_*` 错误并暂停；任意恢复实现抛异常返回 `save.restore_exception`，记录异常类型并暂停，不声称这些模块已完整回滚。

## 公开边界与故障证据

全部场景通过 `GameSave`、`DaytimeClock`、`WorldTime`、时间事件、NPC 状态／活动入口、组件配置及受控决策客户端执行。恢复模块故障通过公开 `IWorldSeedState` 包装器注入；恢复回调使用公开存档协调器替身。没有反射私有字段。

| 路径 | RED 或控制结果 | 修复后的观察 |
| --- | --- | --- |
| NPC 前、后及身体提交中抛错 | [初次 RED](npc-load-recovery-2026-09-17/load-recovery-red.xml)，3 项异常逃出 | 返回已提交但待重建；时间与钱包恢复，代际更新、会面清除，身体允许部分更新，NPC 行动暂停；修复后重读恢复 |
| 通知中换绑／重配 | [重入 RED](npc-load-recovery-2026-09-17/load-recovery-reentry-red.xml)，2 项允许替换 | 发布期间拒绝生命周期变更、再次读档、保存、时间推进及 NPC 行动，保留调度器和在途占用 |
| 模型客户端同步触发失败操作 | [客户端 RED](npc-load-recovery-2026-09-17/load-recovery-client2-red.xml)：未立即取消旧逻辑，走时故障后多派发一次 | 同一 Tick 检查故障状态并停止；Task 未完成前仍占用并发位 |
| 模块变更后恢复／回滚失败 | [恢复 RED](npc-load-recovery-2026-09-17/load-recovery-restore-red.xml)：回滚失败仍为 Ready，恢复异常逃出 | 保留失败类别并暂停，不发布完整快照；有效重读后产生新代际 |
| 新公开观察委托关闭运行许可 | [观察 RED](npc-load-recovery-2026-09-17/load-recovery-observe-red.xml)：继续派发及接受 Visit | [13 项 EditMode 目标结果](npc-load-recovery-2026-09-17/load-recovery-observe-green.xml)：派发前关闭许可不扣额度，执行观察关闭许可不提交活动 |
| 存档界面反馈 | [反馈 RED](npc-load-recovery-2026-09-17/load-recovery-feedback-red.xml)：四类结果均显示普通失败 | 分别显示已恢复数据、待重建、显示错误及恢复状态不确定 |
| 暂停期间收到旧交付回复 | PlayMode 控制用例 | 不扣 Sora 的 25 金币；旧会面保持清除；第三次请求真实结束后才回收，30 秒仍受原额度限制，60 秒可发新代际请求 |
| 普通走时、直接推进与睡眠 | [11 项 EditMode 控制](npc-load-recovery-2026-09-17/load-recovery-controls-edit.xml) | 关键异常暂停，显示异常保持 Ready；时刻与小数余量反映已经提交的推进；不把返回失败当成未推进 |
| 恢复中读缺档或坏档 | 同上 | 保留暂停；模块成功回滚也不能解除更早的重建故障；有效重读才恢复 |
| 提交前失败、有效／重复读档、换绑 | 既有换绑测试及完整回归 | 普通失败保留原请求资格；合法重读拒绝旧代际，不重置实际任务与额度 |

独立复核指出观察回调后的许可遗漏；两个公开调度器用例先复现，再加入派发前、执行前及取事件前的检查。这里测试的是新增委托边界，默认 Unity `CaptureObservation` 没有故障注入回调。复核确认该发现已修复；交付路径由共同检查保护，暂停期交付另由 PlayMode 用例验证。

中间运行保留全部失败记录：[首轮修复验证](npc-load-recovery-2026-09-17/load-recovery-green.xml)错误地在会面清除后查询会面资源，随后改为读取权威角色钱包；恢复 RED 中另有一个错误位置预期，原闲置角色并未移动。最终场景先通过活动入口让 Sora 实际行进，再注入首位居民的身体故障，验证 Sora 仍停留在旧位置，重读后才归位。两项测试修正没有改变生产规则。

## 最终验证

| 执行 | 通过 | 失败 | 忽略 | Unity 退出码 |
| --- | ---: | ---: | ---: | ---: |
| [完整 EditMode](npc-load-recovery-2026-09-17/load-recovery-final-edit.xml) | 865 | 0 | 1 | 0 |
| [完整 PlayMode](npc-load-recovery-2026-09-17/load-recovery-final-play.xml) | 225 | 0 | 5 | 0 |

新故障和控制用例包含在完整回归中，不能与回归计数相加。忽略项为需要显式启用的真实模型实验入口；本轮使用受控客户端验证恢复顺序，不评价模型生成的内容或真实提供方的取消响应。

## 输入与证据边界

[输入核验](npc-load-recovery-2026-09-17/input-audit.json)比对隔离工程的 1,034 个文件，其中 345 个 C# 文件，允许的叠加仅为本票据源码与测试。共享工作区未提交的美术、农田和场景变更没有进入测试或交付。Unity 自动更新的 ProjectSettings 及生成的 SceneTemplateSettings 作为显式例外记录，保留[脱敏设置差异](npc-load-recovery-2026-09-17/isolated-settings.diff)。

[运行清单](npc-load-recovery-2026-09-17/manifest.json)记录筛选条件、退出码、测试计数、最终源码及原始 XML／日志摘要。发布 XML 替换本机路径、统一换行并去除行尾空白，核验各 test-case 属性未改变；[校验和清单](npc-load-recovery-2026-09-17/checksums.json)固定发布文件。中间 RED 属于有序工作阶段，未冻结每阶段二进制；最终输入摘要也不构成 DLL／PDB 源码来源证明。

## 限制与后继

当前仍是 schema v3，重读清会面、按日程归位并清小数分钟。没有有效存档的走时故障需修复后重建会话；本轮没有恢复部分重建现场的就地续跑接口。底层模块的 Restore／Commit 仍是直接状态接口，暂停范围是时间／存档协调器和 NPC 行为入口。

任意恢复实现抛错后的补偿细化与四人准备，列入[完整逻辑快照实施](https://github.com/qinjunw/CozyTown/issues/94)的后继验收；不把本轮保守暂停当成完整事务回滚。正常 Ready 状态的跨调度器重配预算仍由[约束运行中决策重配的预算与在途请求](https://github.com/qinjunw/CozyTown/issues/88)处理。
