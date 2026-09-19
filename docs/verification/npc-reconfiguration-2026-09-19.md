# 决策重配的调用预算与在途请求验证

- 日期：2026-09-19。
- 任务：[约束运行中决策重配的预算与在途请求](https://github.com/qinjunw/CozyTown/issues/88)。
- 基线：`43bc3854f43a98c6b9653bae83a6560e8f14a917`。
- 执行：Unity `6000.5.5f1` 隔离工作树；EditMode 无图形批处理，PlayMode 保留图形设备。真实模型调用为 0，没有读取密钥。

## 策略与公开验收

基线重配释放旧调度器，并创建空调用账本的新调度器。第一个公开用例观察到重配后累计调用从 1 变为 0；第二组确认未结束及已超时但未结束的 Task 均不能阻止重配。

按 [ADR-0016](../adr/0016-bounded-autonomous-decisions.md)采用：真实请求未完成时拒绝替换并保留原决策；无实际在途时允许替换，保留累计调用、60 秒窗口、单调现实时间和按角色记录的冷却。有效普通 Pending 保留原时间与触发事件，用新配置构造请求；旧 Current、社交等待和未消费回复丢弃。候选验证成功后才退休旧调度器、释放旧会面活动并发布新配置。

参数规则：首次调用前允许调整有效配置；已有调用后，只允许保持或收紧限制。RPM、并发、单次决策调用次数、请求／决策期限和机会期限不能增加，冷却不能缩短。省略 settings 沿用当前配置。默认数值与表达模式未改变。

测试使用公开 `ConfigureDecisions`／`CreateReplacement`／`Tick`、请求计数、活动、会面、资源与时间入口。受控异步客户端返回任务和取消令牌，不访问私有字段；Runtime 和控制器分别验证重入边界。

## 失败复现与验证结果

| 行为 | RED 证据 | 最终结果 |
| --- | --- | --- |
| 重配清空调用窗口 | [window-red](npc-reconfiguration-2026-09-19/reconfiguration-window-red.xml)，1 项失败 | 原 1 次调用继续占用窗口，59.9 秒不能派发，60 秒允许下一次 |
| 未结束 Task 的替换 | [inflight-red](npc-reconfiguration-2026-09-19/reconfiguration-inflight-red.xml)，2 项失败 | 拒绝替换，保持真实在途计数与原令牌；健康旧候选可继续落地，超时回复仍被丢弃 |
| 运行后放宽参数 | [limits-red](npc-reconfiguration-2026-09-19/reconfiguration-limits-red.xml)，7 项失败 | 七类放宽均被拒绝；收紧后再省略配置继续受更严格值约束 |
| 控制器枚举／派发重入 | [reentry-red](npc-reconfiguration-2026-09-19/reconfiguration-reentry-red.xml)，3 项失败 | 禁止嵌套重配和派发；枚举回调导致读档恢复失败时保留原调度器，修复重读后继续运行 |
| 普通等待、冷却、时钟与 Runtime 重入 | [state-red](npc-reconfiguration-2026-09-19/reconfiguration-state-red.xml)，6 项失败 | 另一角色的有效待办不丢失；重复重配不延长期限，移除再加入不绕过冷却；时间不能倒退 |
| 许可回调销毁源实例 | [retirement-red](npc-reconfiguration-2026-09-19/reconfiguration-retirement-red.xml)，1 项失败 | 回调返回后复核已销毁状态，不返回新的可运行实例 |
| 非法客户端、人设、会面计划及表达模式 | 完整 PlayMode 的 4 个控制用例 | 会面、双方活动、资产和已完成交付候选保持；旧候选仍能执行一次合法交付 |
| 已完成交付及旧交付候选重放 | 完整 PlayMode 的 2 个控制用例 | 未消费旧候选转移 0 次，已交付状态转移 1 次；新客户端重放旧 meetingId 被操作检查拒绝 |
| 同步会面重配、早晚 Bootstrap、读档、换绑 | 既有测试及完整回归 | 原成功路径通过；同步重配释放旧会面活动，读档与换绑继续保留真实调用约束 |

六轮 RED 均保留对应测试源码和 XML。独立审查补充了 Runtime 重入与许可回调销毁用例；最后一项补齐后重新执行完整 EditMode。中间的 `final-edit` 为 875 项通过，最终结果以下表为准。

## 最终回归

| 执行 | 通过 | 失败 | 忽略 | 退出码 |
| --- | ---: | ---: | ---: | ---: |
| [完整 EditMode](npc-reconfiguration-2026-09-19/reconfiguration-final2-edit.xml) | 876 | 0 | 1 | 0 |
| [完整 PlayMode](npc-reconfiguration-2026-09-19/reconfiguration-final-play.xml) | 246 | 0 | 5 | 0 |

新增 32 个用例包含在完整回归中，不能重复相加。6 个忽略项是需要显式启用的外部实验入口；固定客户端验证宿主调度和资产结果，不评价真实模型生成或提供方取消能力。

隔离检出本分支后，可使用本机 Unity Editor 执行以下命令；完整 PlayMode 去掉 `-nographics`，将 `EditMode` 改为 `PlayMode` 并换用独立输出文件：

```powershell
& "<unity-editor>" -batchmode -projectPath "<clean-project>" -runTests -testPlatform EditMode -nographics -testResults "<results>/edit.xml" -logFile "<results>/edit.log"
```

## 输入与适用范围

[输入核验](npc-reconfiguration-2026-09-19/input-audit.json)比对 1,038 个文件，其中 347 个 C# 文件，仅叠加本票据的 7 个源码／测试文件。共享工作区中的美术、农田和场景修改未进入隔离测试或本次交付。Unity 自动迁移的 ProjectSettings 和生成的 SceneTemplateSettings 记录为明确例外，保留[脱敏设置差异](npc-reconfiguration-2026-09-19/isolated-settings.diff)。

[运行清单](npc-reconfiguration-2026-09-19/manifest.json)记录 14 次运行、过滤条件、退出码、原始 XML／日志摘要及最终源码摘要。发布 XML 替换本机路径、统一换行并去除行尾空白，逐项检查 test-case 属性不变；[校验和](npc-reconfiguration-2026-09-19/checksums.json)固定发布文件。中间证据记录有序修改阶段，没有冻结各阶段二进制；输入摘要不等于 DLL／PDB 的源码来源证明。

重配仍会结束旧会面并重建会面板，不继承旧会面的经历、每日机会与逻辑进度；已提交资产保留。它不承担快照续接，schema v3 现状未变。未完成 Task 持续挂起时，本次重配也持续不可用；没有自动强制切换或跨控制器／进程限流。后继[完整逻辑快照实施](https://github.com/qinjunw/CozyTown/issues/94)应保留本轮现实调用历史，并通过快照合同另行恢复逻辑待办。
