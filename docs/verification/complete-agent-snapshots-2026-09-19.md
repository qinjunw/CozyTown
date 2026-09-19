# 世界与人物完整逻辑快照验证

- 任务：[实现世界与人物的完整逻辑快照](https://github.com/qinjunw/CozyTown/issues/94)。
- 契约：[完整逻辑快照 1.1](../AGENT_SNAPSHOT_SPEC.md)、[ADR-0019](../adr/0019-complete-logical-snapshots-and-agent-continuation.md)、[ADR-0020](../adr/0020-post-commit-world-recovery.md)。
- 输入基线：`927d83e0e897e59a594a1a9fd4234f3662d6990a`。实施计划见[公开验收计划](complete-agent-snapshot-plan-2026-09-19.md)。
- 状态：实施与隔离回归完成；交付分支为 `codex/complete-agent-snapshots`，GitHub 票据记录提交与 Draft PR 状态。

## 实现边界

默认 Unity 游戏使用 schema v4。保存包含统一整数／小数时刻、经济与生产、四名居民的活动和未消费事件、实际路线、会面与交付凭据、经历、决策逻辑进度及玩家位置。`WorldSnapshotBinding` 在默认游戏装配时要求完整模式；四人、玩家或必要适配缺失时返回失败。独立 Runtime 经济／生产用例仍可明确使用 schema v3；这些旧格式用例不代表完整世界快照。

`NpcAgentWorld` 和 `NpcMeetingBoard` 导出独立数据并准备新对象。活动身份和 MeetingId 保留，WorldRunId 与续接请求身份重新生成。活动保存是否归属会面，恢复时双向核对会面与活动；会面已进入交谈时，两人的候选身体必须已经到场。身体从保存的路径、游标和受阻标记恢复，不通过重新寻路代替原行程。完整格式必需字段缺失、角色不全、配置不匹配或引用矛盾均拒绝加载。

加载先校验资源候选和全部身体／Agent 候选，再提交资源与人物。默认资源服务沿用现有 `PrepareRestore`；完整模式不接受缺少候选准备能力的自定义资源服务。只读准备失败保持原世界、合法请求和模态；提交后的核心异常进入 `RecoveryRequired`，可选显示失败沿既有通知规则报告。

决策恢复保留已耗 Calls、下一 Step、一次纠正记录、已披露地点、原机会及日程窗口、剩余总时限、逻辑冷却和轮询位置。捕获时已经超过单次请求期限的决策不再续接。上下文在恢复世界中重新采样，气泡随加载后的台词刷新。当前进程的实际调用次数、滚动窗口和未完成物理任务独立保留；取消旧结果权限不会释放尚未完成的并发槽。

## 配置与旧档

内容、日程、地图拓扑与静态障碍、身体参数、局部观察规则、人设、会面计划、表达模式和调度设置都参与兼容检查。内容与身体配置使用带字段长度的确定顺序文本，保存实际值；其余模块保存具名数据，不只保存不可解析的散列。

固定测试客户端通过 `INpcDecisionConfiguration` 声明可定位的夹具版本。HTTP 代理从 `COZYTOWN_DECISION_SNAPSHOT_CONFIGURATION` 接收宿主声明，规范化并只导出以下字段：

```json
{
  "model": "deepseek-v4-flash",
  "promptVersion": "Tools/agent_proxy/decision_proxy.py@<commit>",
  "protocolVersion": 4,
  "maxTokens": 512,
  "thinking": "disabled",
  "responseFormat": "json_object",
  "stream": false
}
```

应将 `<commit>` 替换为实际运行代理的版本；调用地址仍使用现有代理配置。未声明模型配置可继续普通运行，但完整保存拒绝缺少该声明的决策客户端。存档不记录密钥、地址凭据或 HTTP 对象。当前检查证明宿主声明一致，不提供远程代理配置握手，也不保证真实模型再次输出同样的内容。

v1／v2 先沿用原经济与结算迁移，再与 v3 一样初始化缺失人物状态：小数为零、按日程安置、无旧活动／会面／经历／待办，玩家使用显式初始位置。读取不覆盖原文件；后续正常保存写 v4，并记录真实来源版本与采用的配置。旧文件夹带新格式的来源字段不能改写其迁移身份。

## 公开验收映射

测试使用公共存读档、时间、居民、会面和固定客户端接口。领域快照用例检查特定关联，完整 PlayMode 用例检查最终装配；不反射私有字段或调用真实模型。

| 契约 | 观察结果与测试入口 |
| --- | --- |
| SC-01 | `CompleteWorldSnapshotPlayModeTests`：重复捕获不移动、不消费事件、不新增模型调用；`CompleteSnapshotStorageTests`：文件往返与活对象分离 |
| SC-02 | `BodySnapshotPlayModeTests`、完整世界矩阵：路途中、到达、返家、Blocked 与 NoLegalPosition；原路径游标、有效截止和显式重试边界 |
| SC-03 | `NpcMeetingBoardSnapshotTests`：邀约、预约、赴约、到场及原时段；完整资源会话用例检查活动身份与实际身体 |
| SC-04 | 会面模块和完整资源会话：交付前后资产、凭据恢复，重复 Deliver 不再次转移 |
| SC-05 | `NpcDecisionSnapshotTests`、完整资源会话：已交付待预算／下一发言人／在途轮次保持，续接不冒充已发言 |
| SC-06 | 决策快照用例：第一／第二次调用在途、inspect、纠正、剩余时限、原机会与窗口，新身份拒绝旧结果 |
| SC-07 | 决策与会面用例：每日机会消费、剩余冷却、Pending、轮询顺序；真实调用窗口独立保留 |
| SC-08 | 会面早档对照与完整资源会话：清除未来经历／台词，重复恢复使上次执行结果失效 |
| SC-09 | 世界／会面／存储坏数据矩阵、完整世界最后身体失败：配置、角色、活动归属、路线与新格式必需字段 |
| SC-10 | 完整世界准备失败、核心／显示发布异常及现有恢复故障回归；资源预校验在写时钟前完成 |
| SC-11 | v1／v2 文件迁移、v3 完整适配迁移、未来版本与缺区段；记录来源且读取不重写原文件 |
| SC-12 | 玩家身体与完整世界：成功关闭旧模态、归零速度、恢复初始／保存位置、丢弃旧帧；准备失败保留旧会话 |
| SC-13 | 完整世界重复恢复，用相同时间与固定模型响应比较四人的请求、观察、允许动作、身体、活动和资产；新执行身份分别核对 |

测试入口位于 [EditMode 快照用例](../../Assets/CozyTown/Tests/EditMode/NpcAgents/NpcDecisionSnapshotTests.cs)、[存储用例](../../Assets/CozyTown/Tests/EditMode/Save/CompleteSnapshotStorageTests.cs)、[完整世界用例](../../Assets/CozyTown/Tests/PlayMode/CompleteWorldSnapshotPlayModeTests.cs)、[世界矩阵](../../Assets/CozyTown/Tests/PlayMode/CompleteWorldSnapshotMatrixPlayModeTests.cs)与[完整资源会话](../../Assets/CozyTown/Tests/PlayMode/CompleteWorldConversationSnapshotPlayModeTests.cs)。

## 失败复现与审查

Unity 使用基线创建的独立工程，只覆盖本票据文件。共享工作区的美术、农田与场景改动不进入运行输入；已有经济 UI 测试仅叠加“读档已关闭旧菜单”的断言变化。新公共行为先保存预期失败的 RED 证据，再实施和复测。

| 复现场景 | RED 证据 | 修复后的行为 |
| --- | --- | --- |
| 原资产校验在写时间之后才失败 | [资源预校验](complete-agent-snapshots-2026-09-19/snapshot-required-green-resource-preflight-red.xml) | 时间写入次数为零，原世界保持可用 |
| 迁移来源被旧档夹带字段改写；准备异常进入恢复暂停 | [边界矩阵](complete-agent-snapshots-2026-09-19/snapshot-final-boundaries-red.xml) | 来源由真实旧版本确定；准备失败不提交、不暂停原运行 |
| 早期资源档被未来资产拒绝；小数剩余量超过配置上界 | [资源准备](complete-agent-snapshots-2026-09-19/snapshot-decision-regressions-resource-red.xml)、[精度与初始化](complete-agent-snapshots-2026-09-19/snapshot-resource-green-precision-initialization-red.xml) | 准备使用候选关系；剩余量限制在配置范围；旧档初始化逻辑冷却但不退还实际调用 |
| 单次请求已超时，捕获却保留续接资格 | [请求期限](complete-agent-snapshots-2026-09-19/snapshot-boundary-green-request-timeout-red.xml) | 保存不取消活请求，恢复后也不重新派发已失效决策 |
| 未来台词仍显示；Talking 会面接受途中身体 | [会话与到场](complete-agent-snapshots-2026-09-19/snapshot-talking-presence-bubble-red.xml) | 气泡来自恢复台词；矛盾身体在准备阶段拒绝 |
| 删除会面后仍恢复双方赴约活动 | [活动归属](complete-agent-snapshots-2026-09-19/snapshot-request-timeout-green-meeting-owner-red.xml) | 未出发及已出发的会面活动均须有所有者；同地点普通活动仍可恢复 |

两名独立审查者分别核对契约与实现，发现的两项 P2 为 Talking／身体矛盾和会面活动缺少反向归属。这两项均先复现失败，再修复并纳入最终回归。模块审查还补充了表中的资源、期限及初始化控制用例。

首轮完整 PlayMode 有两项旧 UI 测试在加载后再次切换菜单，打开了新合同已关闭的界面；现改为直接断言菜单关闭和输入解除。资源会话最初在准备阶段失败，修复后才到达旧气泡断言；中间失败和诊断运行均保留，没有将它们计为通过。

## 最终回归与复测

| 执行 | 通过 | 失败 | 忽略 | 退出码 |
| --- | ---: | ---: | ---: | ---: |
| [完整 EditMode](complete-agent-snapshots-2026-09-19/snapshot-final-edit.xml) | 966 | 0 | 1 | 0 |
| [完整图形 PlayMode](complete-agent-snapshots-2026-09-19/snapshot-final-play.xml) | 275 | 0 | 5 | 0 |

新增 94 个用例包含在上述完整回归中，不能与定向运行重复相加。6 个忽略项均为需要显式启用的真实模型实验入口。运行版本为 Unity `6000.5.5f1`；真实模型调用次数为 0，没有读取密钥。本轮未改 Python 代理实现，也未重复运行其单元测试。

在包含本分支的干净检出运行以下命令；PlayMode 去掉 `-nographics` 并替换测试平台和输出文件名：

```powershell
& "<unity-editor>" -batchmode -projectPath "<clean-project>" -runTests -testPlatform EditMode -nographics -testResults "<results>/edit.xml" -logFile "<results>/edit.log"
```

[输入核验](complete-agent-snapshots-2026-09-19/input-audit.json)比对 1,080 个文件，其中 368 个 C# 文件；本票据叠加 77 个源码、测试及 meta 文件，其中经济 UI 测试仅包含本次断言。Unity 自动迁移的 PlayerSettings 与生成的 SceneTemplateSettings 作为明确例外记录，见[设置差异](complete-agent-snapshots-2026-09-19/isolated-settings.diff)。

[运行清单](complete-agent-snapshots-2026-09-19/manifest.json)记录 35 次 Unity 运行的过滤条件、退出码、失败用例、原始 XML／日志摘要及最终输入摘要；[校验和](complete-agent-snapshots-2026-09-19/checksums.json)固定发布文件。发布 XML 只替换本机路径、统一换行和去除行尾空白，逐项核对 test-case 属性不变。中间运行对应有序修改阶段，没有冻结各阶段二进制；输入摘要不代表 DLL／PDB 来源证明。

SC-13 使用相同时间和固定客户端响应验证恢复等价。面向使用者的四人实验入口、轨迹导出和分歧停止回放由[建立四人实验入口与角色观察视图](https://github.com/qinjunw/CozyTown/issues/95)继续实施；真实模型跨日表现由最终评测处理。本报告不作为人工视觉验收或真实模型输出确定性的证据。
