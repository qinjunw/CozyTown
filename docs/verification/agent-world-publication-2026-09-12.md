# Agent 世界首轮发布验证

发布分支为 `codex/agent-world-foundation`。本次先在隔离 Git worktree 检出提交 `fa170a7a75d2bacebc6b4e16db5209174b5b1042`，再运行完整 Unity 回归，以核对发布内容不依赖共享工作区中的其他未提交修改。

| 检查 | 结果 |
| --- | --- |
| Unity 版本 | `6000.5.5f1` |
| 完整 EditMode | 510/510 通过，0 跳过 |
| 完整图形 PlayMode | 162/162 通过，0 跳过 |
| 本轮模型调用 | 0 |

[原始运行基础报告](agent-world-runtime-2026-09-12.md) 的 514/514 与 164/164 对应共享工作区，测试文件集合不同。本次发布采用上表的隔离检出结果；没有将共享工作区的美术、农田修改包含在本分支。

校验值、测试提交和源文件 Git blob 见 [发布验证数据](agent-world-publication-2026-09-12.json)。测试后仅增加本报告、验证数据与相关文档链接，运行代码与用例保持相同。

GitHub 是任务管理的规范记录：[迭代世界与人物 Agent 的自主交互](https://github.com/qinjunw/CozyTown/issues/50)。首轮实现通过 Draft PR 交付，合并状态以 GitHub 为准。用户已授权推送后继续下一阶段；后续分支从此已验证基础继续。
