# 候选交付文档与证据独立复核

审阅日期：2026-09-20。审阅对象：`codex/agent-platform-candidate` 的候选交付文档与公开证据；本次读取时 HEAD 为 `bc59db8de897261c3385286baf2e6cedf6b9677b`。审阅者：`/root/speech_review_a`；证据数字与字节复核由其子审阅者 `/root/speech_review_a/report_numeric_check` 独立执行。

## 结论

未发现阻塞发布的实质问题。未发现会改变验收结论的数字、分母、来源、链接或范围错误。无须在本次发布前处理的文字建议。

本结论限于下列 SHA 对应的文档与证据，不代替 Unity 运行验收，也不证明候选分支已经推送、PR 已创建或合并。

## 已核对的事实与范围

- 公开 XML 实际含 12 个用例，全部为 Passed；结果计数、起止时间及公开 XML SHA 与 manifest 相符。没有把这 12 项固定、回放或 HTTP 替身检查说成本轮真实提供方实验。
- 公开 ZIP 为 8 个包、29 个文件、371633 字节。归档 SHA、逐成员 SHA 与字节数均与登记值相符。模拟 Live 包采用固定决策客户端及 HTTP 替身；其中的模拟调用统计不被计作真实提供方调用。篡改负样本的 checkpoint 余额与测试源码的加一篡改一致，文档没有将其作为成功回放证据。
- 候选提交、Live 冻结提交 `19dff35147fbb7f8debc3aa0f98da44a29b5b52d`、固定回归提交 `7ac7240963f113b54f30229c1ef929e0bbceac5d` 的 Assets、Tools、Packages、ProjectSettings 四棵 Git 树实际相同。场景 SHA、登记的 8 份基线文档 SHA 均相符；登记的 28 个依赖 head 均为候选提交祖先。同树回归复用没有写成本轮重新执行全部回归。
- 候选报告将历史 Live 的 16 个原始世界、15 个完成并通过严格 Replay 的世界、1 个期限未完成世界分开，并保留历史 244 次提供方尝试。本轮新增真实提供方调用为 0 的表述与固定/替身测试来源一致，没有宣称 16/16 完成或所有表达正确。
- 完整快照限定为已实现的逻辑状态；真实 HTTP 任务、已发生费用、进程限流及物理在途占位不因读档回退。Replay 使用记录和配置核对宿主行为，未承诺再次请求真实模型得到相同台词。手动检查点读档仅覆盖当前实验会话，跨会话使用导出包 Replay。
- 运行前干净检出与 Unity 启动后生成或改写的设置分开登记，文档说明生成副本和差异已登记哈希、随后恢复测试来源。没有声称测试期间 ProjectSettings 文件逐字节始终未变；新增加的证据目录属性将 XML 固定为 LF。
- README、PRD 顶部、指南及 milestone 的候选来源、未合并状态、验收结果和范围与候选报告一致。所检查的文档本地 Markdown 文件链接目标存在；最终指南与 milestone SHA 未在复核末尾发生变化。
- 对指南新增操作说明做了代码对照：Replay 的场景/组别/计划来自包，来源版本输入是操作者元数据；窗口检查点仅来自当前 Session；遥测两个响应共享 8 MiB 上限；未完成导出保存当前快照并标为未完成，默认 Replay 读取拒绝该包；场景恢复仅恢复已有保存路径。这些说明与实际入口实现相符。

## 复核边界

本次只读审阅，没有启动 Unity、重新运行 Replay、请求模型或写入 GitHub。公开 XML、包成员和登记来源树已经直接复算；原始 XML、Unity 原始日志、LFS 日志和 Unity 生成设置副本没有收入本次候选公开目录，因此本次仅核对其登记及用途，不声称能仅凭该目录复算这些原始文件的字节。证据 README 和测试 manifest 已说明原始记录在本地按 SHA 保留。

远端 PR 状态及发布后的 Resolution 链接由发布流程最终登记；本复核不将本地依赖记录当作发布后远端状态证明。

## 实际审阅文件 SHA-256

以下为复核结束时工作树文件的字节 SHA-256；不包含随后复制本审阅记录自身的操作。

| 文件 | SHA-256 |
| --- | --- |
| `README.md` | `3f444a37baa5f02186226821b8f9834fca7dd9bf9ff3088c12c1b5bf41e90cb4` |
| `docs/AGENT_WORLD_PRD.md` | `39413940d2e9a4a6b89fb2c331eeaa9fd669ac917d2d7fb322cf2bd572bacd7c` |
| `docs/AGENT_EXPERIMENT_GUIDE.md` | `79b454a1798006cf71cbb0659e7671cf8c014192822f95caa6e4f70743cf1688` |
| `docs/wayfinder/agent-world/platform-milestone.md` | `188c0d19d79e9a1cbdc88bb7eade714d497af94537b3dc321aa35f50204fe1d7` |
| `docs/verification/agent-platform-candidate-2026-09-20.md` | `f2b2eae4c653f1966734964f4219e6a1ce1c9be4b4330a0a2583c87f0b9913b1` |
| `docs/verification/agent-platform-candidate-evidence/.gitattributes` | `0b57c2733fa9cda55662bda98ea5d28b097102ac610181bad060db0ef71a11e0` |
| `docs/verification/agent-platform-candidate-evidence/candidate-dependency-audit.json` | `60a58b98f5c3eb4f05635c9f0ad2953e5e2adda9946cc0512c94f814a664d6bb` |
| `docs/verification/agent-platform-candidate-evidence/candidate-entry-packages.manifest.json` | `da8aeecb863781bc90ed5ff94cb554d0107be66da82cbc21305a98467a84437d` |
| `docs/verification/agent-platform-candidate-evidence/candidate-entry-packages.zip` | `f5064c81d1ec928a91877e00669499e047cd1e549b824fa2cacdc5a9813e66d3` |
| `docs/verification/agent-platform-candidate-evidence/README.md` | `262b16aed29b76cb8b29b25899ead89dcafee29cea9b3068c8b0ad69153bc5e0` |
| `docs/verification/agent-platform-candidate-evidence/source-and-settings.json` | `90c66376b97f289f939abcdb97fc9d554072bf81c29d46b9d423ddacd84a8d90` |
| `docs/verification/agent-platform-candidate-evidence/tests/candidate-entry-preflight.json` | `e1452d73b40acc40f4847517548cc7ffda7b0ba0dce9730701bc53f33938a5b0` |
| `docs/verification/agent-platform-candidate-evidence/tests/manifest.json` | `b28583bf946f2e742143dbdb0dc609737fbc50453cdd47ef9ca2c26ffe57f87e` |
| `docs/verification/agent-platform-candidate-evidence/tests/xml/candidate-entry-play.xml` | `b046059a6c8c9fed9ea8b029351e725360fd7caabc30c7519dc58ddbb7314632` |

公开 XML 在 Git 暂存区的字节 SHA 也等于 manifest 的 `publicXmlSha256`，且 `text` / `eol` 属性的实际结果为 `text: set`、`eol: lf`。本次未修改任何 tracked 文件；本记录原始输出位于忽略目录 `Logs/agent-platform/candidate-final-review.md`。
