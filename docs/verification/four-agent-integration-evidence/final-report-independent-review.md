# 最终集成报告独立核对

结论：在指定来源范围内，未发现有实质影响的数字、分母、范围夸大或伪称通过问题。该结论是对报告与冻结汇总的一致性核对，不是新的运行验收。

审计对象为 `docs/verification/four-agent-integration-2026-09-20.md`，SHA-256 `ae583e1e33e2f97e80c92a9afb0382a97c6143ad1b2af8512908fa861976e60f`。台词评阅已冻结，本次没有修改或重新判定原评阅。数值复核另由独立只读子任务交叉重加。

## 已核对结果

| 项目 | 核对结果 |
| --- | --- |
| 世界与 Replay | 原分母 16；15 completed、1 incomplete；15 个完成世界 Replay 通过。逐世界尝试数、台词数和终止时间与表格一致。 |
| 派发与上下文 | 244 次提供方尝试、243 个逻辑决策；逐人 D1/D2 派发和首请求上下文表一致，243 个人设上下文均非空，129 个经历数组非空。 |
| 第二天与终态 | 72 个逻辑决策中 50 个观察过期、2 个无效回复、20 个接受；64/64 个终态人物样本 Arrived 且无临时活动。报告明确“获得处理”不等于“完成有效自主行动”。 |
| 候选与用量 | 首候选宿主接受 159/243、最终接受 160/243；F/S 分组分子分母一致。730509 输入、9537 输出、740046 合计 Token；244 次 JSON 对象、239 次协议通过及 5 次拒绝均一致。 |
| 延迟 | 三类延迟的样本分母、P50、P95 和最大值均与摘要一致；单位和统计边界已经区分。 |
| 表达 | 72 条唯一提交台词、245 个评阅单元、107 个事实候选；F/S 为 87/20 个事实，明确问题 33/0、歧义 21/0。台词层面问题数、无事实数和四维度分布均一致。 |
| 当前测试来源 | 当前清单绑定 `19dff35147fbb7f8debc3aa0f98da44a29b5b52d`；EditMode 为 1105 通过、1 跳过、0 失败，Live 协调入口为 1 通过。报告未把入口通过替换成 16 世界全部完成。 |
| 配对与适用边界 | 8 对首个上下文分歧调用和字段与摘要逐项一致；报告保留后续输入不同、不能全部归因于表达格式的限制。 |

报告对固定压力/故障测试与 Live 的分工、旧 EditMode 历史版本未知、当前补测来源、预算等待采样不能换算持续时间、模型字符串不能证明同一后端版本、货币费用未知、盲评不完全、无固定 QA 目标、仅两组人物会面和有限两天样本均有限定。世界 12 未完成、没有 Replay，以及运行中防待机干预也已明列，没有被合并成全通过结果。

## 补充核验与审计边界

正文中的取消请求 **1937 Token** 已补核。按单独授权仅读取指定世界的对应决策记录：`Logs/agent-platform/integration-coordinated-live-20260920-a/04-c6cf99d35d344186a5d03a6d03e66935/proxy.jsonl` 第 2 行，决策 `7e3049e70c844f0d9f42c6371f0a0c07`、`step=1`，记录 `promptTokens=1927`、`completionTokens=10`、`totalTokens=1937`。该文件 SHA-256 为 `d26dd453083b065c6183f8d5fc8458da53a2eda28bf629cbbf8b2a29da1d5ecb`。候选摘要将同一身份关联到 world 4、`providerCall=55`、`proxyRowIndex=1`、`traceCallOrdinal=1`，宿主记录为 `canceled`/`no_response`。原始代理保存了候选文本，与宿主未收到候选并不矛盾；报告已保留这个区别。

补入的旧 EditMode 来源段已按旧清单 selectedCases 与公开新 XML 完整测试名核对：14 个唯一方法、42 个唯一参数化用例全部唯一匹配且 Passed；新 XML 的 SHA-256 与当前运行清单一致。publication-verification.json 记录 archiveReproduction=passed、三类复算对象一致、credentialValueScan=passed、801 个被扫描文件或归档成员及 providerCalls=0。三个公开汇总与 Logs 原件逐一解析相等，原始/公开哈希映射与 derived-artifact-provenance.json 一致。

本次没有重新执行 Unity、提供方请求、测试、Replay 或统计工具的回归用例，也没有逐字节重验所有原始运行与诊断记录。归档复算和凭据值扫描按发布验证记录核对，没有另行执行。原始运行环境、源码树比较及固定测试的细节按覆盖备注和当前测试清单核对来源描述。

独立只读字节核对还确认：4 个 ZIP 的工作区字节、暂存 Git 字节及清单 SHA-256/大小一致；443 个成员的文件集合、哈希和大小符合清单；两份测试清单列出的 17 份 XML 的暂存与工作区字节均符合清单哈希。暂存发布验证记录与工作区一致。未重复运行复算器或凭据扫描。

## 输入身份

以下路径均相对仓库根目录。

| 来源 | SHA-256 |
| --- | --- |
| `Logs/agent-platform/live-final-summary.json` | `0b530739e7dfad50bde1caa29730cd588bc2836f777db960dd31960dba62e8cc` |
| `Logs/agent-platform/live-final-candidate-metrics.json` | `bfb3422e637f88b268cae4a06040e99e5dd3b46a002532d9cbc0583cb6365d5b` |
| `Logs/agent-platform/live-final-speech-summary.json` | `a81eeca44e1eb86154dacdab79a7c60b37810a95e247f8c8c38c844e5fd4684e` |
| `Logs/agent-platform/current-test-export/manifest.json` | `3b41799736cb2f2c3111e35fe6cc8393fc7d8e8492f30df29cad86f63ce6b52a` |
| `Logs/agent-platform/integration-report-coverage-notes.md` | `b08f6807b6f6199799806aa9890606389bd19b23ba5ed7285b50d68707284947` |
