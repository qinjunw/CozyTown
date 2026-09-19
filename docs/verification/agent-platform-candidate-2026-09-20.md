# 四人 AI Agent 实验平台：候选验收

第一版实验平台已具备四人自主运行、完整逻辑快照、角色观察、记录导出和严格回放。候选入口的 12 项 PlayMode 检查通过；已有同代码、同配置回归及 16 世界真实矩阵作为集成证据继续保留。本次冻结只更新交付文档和证据，新增真实模型调用为 0。

## 候选来源与检出

- 候选分支：`codex/agent-platform-candidate`。
- 本轮实际运行来源：`bc59db8de897261c3385286baf2e6cedf6b9677b`，启动前工作区干净，`git lfs fsck` 通过。
- 直接依赖：[验证四人跨日读档与有界实验协调流程](https://github.com/qinjunw/CozyTown/pull/106)，head 为上述提交；前置交付及范围 PR 的 28 个 head 均包含于其 Git 祖先关系中，见[依赖审计](agent-platform-candidate-evidence/candidate-dependency-audit.json)。
- 交付任务：[冻结 AI Agent 实验平台候选版本](https://github.com/qinjunw/CozyTown/issues/96)。最终文档提交的完整 SHA、候选 Draft PR 和远端核验记录在该票据的 Resolution；PR 栈保持未合并，关闭票据不表示 main 已交付。

本轮使用独立工作树的干净提交，不依赖原共享工程的未提交美术、场景或农田文件。`Assets`、`Tools`、`Packages`、`ProjectSettings` 四棵 Git 树与冻结实测来源 `19dff35147fbb7f8debc3aa0f98da44a29b5b52d`、固定回归来源 `7ac7240963f113b54f30229c1ef929e0bbceac5d` 逐项相同。[树与场景身份](agent-platform-candidate-evidence/source-and-settings.json)记录完整树 ID 和场景 SHA256；不是仅凭祖先关系复用测试。

使用 Unity `6000.5.5f1`、Git LFS；需要 Live 时另用 Python 3.10+ 和仓库外私有凭据。按[操作指南](../AGENT_EXPERIMENT_GUIDE.md)检出候选分支，再对照 Resolution 的完整 SHA；需固定版本时执行 `git checkout --detach '<candidate-commit>'`。依次运行 Fixed、保存／读取、导出、Replay，可在不调用提供方的情况下检查主要流程。真实模型代理、配置模板、调用上限及停机步骤均在同一指南。

## 本轮入口验收

Unity `6000.5.5f1 (d16e074b49fd)` 于 UTC `2026-09-19 20:11:38–20:11:43` 执行两个测试类：`AgentExperimentSessionPlayModeTests` 与 `AgentExperimentLauncherPlayModeTests`。12 项通过、0 失败、0 跳过。启动进程的 `COZYTOWN*` 环境变量已清除；所有客户端使用固定输出、录制响应或 HTTP 替身。

| 检查 | 可观察结果 |
| --- | --- |
| 初始化及启动顺序 | 正式开发场景中的四人身体与资产正确；显式选择前没有请求；启动器先于默认 Bootstrap 初始化实验服务 |
| 四人活动与观察 | 固定输出完成 Mina/Eli 和 Ren/Sora 两对会面，四人均有结果及经历；导出包含局部观察、执行观察和资产交付凭据 |
| 保存与连续读取 | 保存早期状态后读档恢复身体及资产、清除未来交付凭据，真实请求计数不退回；等待物理帧后玩家位置仍稳定 |
| 跨日 | 睡眠跨越日期与晨间结算，录制输入在新场景产生匹配终态 |
| 导出与回放 | F 时间线可导出；S 的保存、读取、交付在独立新场景严格回放；可再次导出 Replay 包 |
| 失败与篡改 | 初始快照失败阻止派发；非法时间输入不污染记录；修改检查点资产导致回放停止 |
| 度量 | 模拟 HTTP 指标能按请求关联，测量注释不改变权威回放结果；该用例虽标 `live`，不是真实提供方样本 |

[测试清单](agent-platform-candidate-evidence/tests/manifest.json)包含 XML、日志原始 SHA256、来源及计数；[运行前记录](agent-platform-candidate-evidence/tests/candidate-entry-preflight.json)保留干净检出与关闭提供方的声明。复现命令使用新的输出路径，并保持提供方环境关闭：

```powershell
& '<unity-editor>' -batchmode -projectPath '<project-root>' -runTests -testPlatform PlayMode -testFilter 'CozyTown.Tests.PlayMode.AgentExperimentSessionPlayModeTests;CozyTown.Tests.PlayMode.AgentExperimentLauncherPlayModeTests' -testResults '<new-xml>' -logFile '<new-log>'
```

不要添加 `-quit`，由 Unity Test Runner 完成并退出。该验收加载正式场景、调用公开 Session 和 Launcher 入口，**不是本轮新增的人工窗口或画面验收**；此前图形入口证据仍保留原运行身份。

Unity 退出时重写了 PlayerSettings 序列化版本及平台默认项，并生成 SceneTemplateSettings。已保留生成副本及差异 SHA256，再仅恢复本轮生成的设置变化。本报告的“干净”指运行前源码，不声称 Unity 导入过程中的设置字节全程未变。

## 已有回归与真实结果

完整数据、失败样本、评阅方法及重算工具见[集成验证报告](four-agent-integration-2026-09-20.md)。本次未无故重复同树的全量回归或真实调用。

| 证据 | 结果与适用范围 |
| --- | --- |
| 全量 EditMode | 1105 通过、1 项真实入口跳过、0 失败；干净 `19dff35…` 来源 |
| 全量 PlayMode | 294 通过、5 项显式入口跳过、0 失败；`7ac7240…` 来源，代码配置树相同 |
| Python 代理及协调器 | 86/86 通过；`19dff35…` 来源 |
| 固定跨日矩阵 | 修复后 16/16 完成并严格回放；首次玩家物理位移导致的失败批次仍保留 |
| 真实跨日矩阵 | 原计划 16 世界全部计入，15 完成并严格回放，1 个因 600 秒期限未完成；244 次调用、740046 Token，金额未知 |
| 表达评阅 | 72 条提交台词全部经双评阅及裁决：F 的 87 个事实断言中 33 个明确无依据、21 个歧义；S 的 20 个断言在本样本均有支持。信息量、重复、角色表现分别报告，不归纳为 S 已全面优于 F |

请求型号为用户选择的 `deepseek-v4-flash`，提供方返回 `deepseek-flash`；没有证据证明别名后的模型构建身份。未完成世界遇到 166 秒 Tick 间隔，与 Windows Modern Standby 时间重合；后续临时防系统休眠措施及解除时间均已登记，没有删除或替换该世界。

## 第一版完成条件核对

| 阶段要求 | 本候选证据 |
| --- | --- |
| 四人自主运行 | 显式实验配置为四人开放已有活动和会面；固定两对会面完成，真实运行保留接受、拒绝、失效及终态 |
| 世界与人物连续性 | schema v4 完整快照[实现验证](complete-agent-snapshots-2026-09-19.md)及本轮保存／读取／跨日／回放；HTTP 物理任务和真实预算不当作可回退的存档状态 |
| 有界运行 | 集成 H01–H07 约束映射、完整回归和当前入口失败路径；不重复交付、不让旧回复改写新世界、不退回预算 |
| 可观测 | 四人角色卡、局部事实及审计；包内上下文、候选、宿主结果、会面和资源记录可关联 |
| 可重复实验 | Fixed／Live／Replay 分开，F/S 显式选择及同初态独立配对；真实批次保留全部 16 世界和首次上下文分歧 |
| 可交付版本 | 干净候选入口测试、完整依赖祖先审计、操作指南与私有模板、可下载证据；最终提交及未合并 PR 由 Resolution 登记 |

依赖审计读取时，地图的 28 个原生子任务中前 27 项均关闭且有正式 Resolution，41 条阻塞边均已解除，剩余为本交付任务。旧复现票据中记录的预算等待停滞、换绑／重配以及读档重建问题均有已包含的后继实现。最终票据和地图仅在候选推送、证据及 Resolution 齐全后关闭。

## 使用边界与下一轮研究

- 普通游戏配置与四人实验配置分开；当前覆盖两对互动，不代表全部六种人物组合或长期自治质量。
- 第二天仍有大量 `agent.observation_stale` 拒绝，Mina 的第二天决定在本批全部因此失效。宿主拒绝陈旧信息保护状态，但不证明持续自主行动有效；下一轮可研究移动时观察有效期和重新采样策略。
- F 仍会编造环境或资源；S 限定的事实片段有支持，不代表整句信息量、角色感或交流效果充分。合同没有统一表达质量通过线；这些结果保留为改进依据。
- 初始配置一致不等于后续提示词一致：8 对实验均出现后续上下文分歧。配对结果不能直接解释为表达模式的因果优劣。
- 完整逻辑快照覆盖已实现状态；没有增加职业生产闭环、长期关系、第三人传闻、大规模社会模拟或新地图。检查点手动读取限于当前 Session；跨 Session 复现使用导出的包与 Replay。
- 构建版本及生成配置由操作者声明，工具不自动证明源码身份或与远端代理握手；应保留实际提交、dirty 差异和代理日志。当前测试结果来自本地执行，不能称为 GitHub CI 通过。

这些限制不改变本轮已确认的实验平台范围。若后续目标转为提高持续行动率、表达质量或完整生活玩法，应另建问题、冻结新的样本与判定规则。
