# 四人集成验收证据

本目录保留固定演练、失败过程、真实模型评测和源码绑定回归。方案见[集成验收计划](../four-agent-integration-plan-2026-09-20.md)，结论与适用范围见[完整报告](../four-agent-integration-2026-09-20.md)。下面的固定批次没有真实提供方调用，不计入 Live 样本。

## 批次与原始分母

| 批次 | 代码来源 | 完成并通过 Replay | 宿主失败 / 未开始 | 固定客户端请求 | 已提交台词 / 缺证 |
| --- | --- | --- | --- | --- | --- |
| fixed-a | `9737054c9a0d7c84237439fe13ae36f704c0a43f` | 0/16 | 1 / 15 | 16 | 4 / 0 |
| fixed-b | `7ac7240963f113b54f30229c1ef929e0bbceac5d` | 16/16 | 0 / 0 | 224 | 40 / 0 |

fixed-a 第一世界推进完成后，严格回放在 `origin` 检查点发现玩家位置差异，因此记宿主失败，后 15 世界没有启动。输入锁未阻止玩家动态刚体的接触分离。修复在实验装配时固定玩家位置和旋转；普通游戏输入门禁未改变。

fixed-b 每世界从第 1 天 12:00 推进到第 2 天 16:00，包含连续两次读取第 1 天检查点和跨午夜、晨间结算。每世界使用 12–16 次请求、450.5 受控现实秒，四人均获得第二天派发；各世界最高物理并发为 2。受控时间不代表真实模型延迟。

8/8 配对初态核验与上下文报告齐全。实际后续上下文因台词声明分歧，不能把配对后续输出称为同输入对照。部分第二天候选因观察过期被拒绝；完成实验与回放不要求所有邀约成功。

## 文件与校验

- [fixed-a 摘要](integration-coordinated-fixed-20260920-a-summary-v2.json)、[原始记录 ZIP](integration-coordinated-fixed-20260920-a-v3.zip)、[归档清单](integration-coordinated-fixed-20260920-a-v3.manifest.json)。
- [fixed-b 摘要](integration-coordinated-fixed-20260920-b-summary.json)、[原始记录 ZIP](integration-coordinated-fixed-20260920-b-v2.zip)、[归档清单](integration-coordinated-fixed-20260920-b-v2.manifest.json)。
- [回归与失败过程清单](tests/manifest.json)：包含便携测试 XML、源文件和公开文件 SHA-256、失败名称及 H01–H07 公开测试映射。没有已知精确源码 SHA 的历史过程记录明确使用 `null`。

ZIP 保留 `plan.json`、状态、账本、逐步进度、台词、配对比较和实验包。`package/` 与 `replay/` 内的文件保持原字节；外层文件的本机目录替换为 `<study-root>`。每文件的原始与公开 SHA-256、替换位置均在归档清单中。JSON Pointer 保留原文，不作为文件路径替换。

校验 ZIP 后解压到新目录，使用其中某世界的 `package` 目录作为 Replay 输入。便携路径占位符不用于重新启动协调器；重新实验按操作指南创建新批次。归档可公开与实验已通过分别记录，`publicationReady` 不等同于 `completeForPlannedStudy`。

## 冻结前回归范围

修复后完整 PlayMode 为 299 项：294 通过、5 项显式启用测试跳过、0 失败；协调矩阵入口本轮已启用。物理等待回归先失败，修复后会话与启动器 12 项通过。此前完整 EditMode 为 1106 项：1105 通过、1 项显式启用测试跳过；物理修复仅修改 Unity 实验装配，未改 Runtime 或 EditMode 测试。

H01–H07 映射涵盖资源初态、共享预算、跨日、分阶段存读档、旧响应、重配、迁移、重建失败、活动期限和事实权限。阶段恢复包含闲置、邀约、预约、赴约、交付前后及交谈，验证早档不保留未来经历和重复读档不重复交付。固定故障测试与 Live 的实际阶段覆盖分别计数。

Python 协调器 86 项测试通过；有界清理、截止拒绝和实际请求协议版本均有定向验证。当前目录的 XML 不包含 Python 结果，不能从 XML 推算这项计数。分支交付仍为 Draft PR，未合并。

## Live 与冻结源码回归

Live 来源为 `19dff35147fbb7f8debc3aa0f98da44a29b5b52d`，与 fixed-b 的应用、测试、工具和工程配置 Git 树相同。16 世界完成原定评测流程，15 世界到达第 2 天 16:00 并通过严格 Replay；第 12 世界遇到系统待机后的推进空隙，于原 600 秒上限停止，保留为 incomplete、未执行 Replay。总计 244 次提供方尝试、72 条提交台词，缺证 0，没有替补世界。

新 EditMode 在该冻结提交干净启动，1105 项通过、1 项显式真实入口跳过、0 失败；新 Python 回归 86 项通过。Unity 运行会生成或升级工程设置，退出后的改动有备份和诊断，没有作为应用源码变更提交。

| 文件 | 用途 |
| --- | --- |
| [Live ZIP](integration-coordinated-live-20260920-a.zip)、[清单](integration-coordinated-live-20260920-a.manifest.json) | 312 个原始研究文件；保留未完成世界及 15 个 Replay |
| [世界与人物摘要](live-final-summary.json) | 全部世界、逐人逐日派发、上下文与状态采样 |
| [候选与用量](live-final-candidate-metrics.json) | 243 个逻辑决策、244 次尝试、解析、接受、时延与 Token |
| [表达汇总](live-final-speech-summary.json) | 72 条台词的精确样本并集、断言及分组指标 |
| [评阅 ZIP](live-speech-reviews.zip)、[清单](live-speech-reviews.manifest.json) | 完整盲包、身份映射、两份独立评阅、裁决、九批校验与来源链 |
| [工具 ZIP](integration-analysis-tools-v3.zip)、[清单](integration-analysis-tools-v3.manifest.json) | 最终分析脚本、相关测试及 v3 冻结哈希；不调用提供方 |
| [诊断 ZIP](integration-diagnostics.zip)、[清单](integration-diagnostics.manifest.json) | 待机、取消连接、生成设置、Python 测试、统计缺陷与修复的独立复核、宿主覆盖审计 |
| [当前测试清单](tests/current-manifest.json) | 新 EditMode 与 Live 协调入口 XML，原始/公开 SHA 与启动前源码身份 |

`tests/manifest.json` 是冻结前的历史清单；准确源码未知的旧 EditMode 仍为 null。`tests/current-manifest.json` 补充明确来源，不改写旧记录。Live 协调入口一个测试通过仅表示按照已登记世界依次执行，世界完成情况仍按原 16 个分母报告。

[派生文件来源映射](derived-artifact-provenance.json)记录本地汇总与公开 JSON 的 SHA-256；公开副本统一 JSON 排版和 LF 换行，解析后的值不变。[发布复算记录](publication-verification.json)核对解压后全部世界、候选和表达分组结果，并记录凭据值扫描通过。

## 离线复算

将工具 ZIP 与评阅 ZIP 解压到同一个新目录。所有内含文件保留原字节和换行；先按相邻 manifest 校验 SHA-256。使用 Python 3.10+，在解压目录执行：

```powershell
python -B -m unittest test_candidate_metric_audit test_npc_context_summary test_speech_review_tools
$reviewArguments = @('--final-pack', 'live-final-blind.json', '--final-identity', 'live-final-identity.json')
1..9 | ForEach-Object {
    $batch = '{0:D2}' -f $_
    $reviewArguments += @('--batch', "live-review-batch-$batch.json", "live-review-batch-$batch-identity.json", "live-review-$batch-adjudicated.json")
}
python -B aggregate_speech_reviews.py @reviewArguments --output '<new-summary.json>'
```

重新运行聚合应得到 `valid=true`、72 条样本、107 个事实候选、33 个明确问题及 21 个歧义。时间戳不同，不要求派生 JSON 整文件哈希相等。详细计数定义见工具包内 `SPEECH_REVIEW_TOOLS.md`。

Live ZIP 解压到另一新目录后，可运行：

```powershell
python -B summarize_integration.py --study '<extracted-study>' --output '<new-world-summary.json>'
python -B summarize_candidate_latency.py --study '<extracted-study>' --output '<new-candidate-summary.json>'
```

世界与候选的指标应一致；外层路径脱敏使部分源文件哈希与本地原件不同，逐文件映射见 Live 归档清单。评阅 ZIP 内的原始来源哈希链已保留，应直接复算其聚合，不把重新从脱敏研究生成的盲包当成原字节包。`audit_candidate_first8_v3.py` 是本地前缀审计的历史辅助脚本，不是复算全批结果的入口。

启动新实验或导入 package 进行 Unity Replay 见[操作指南](../../AGENT_EXPERIMENT_GUIDE.md)。归档中的便携路径占位符不能用来续跑原批次或重置预算。
