# GitHub tracker

Agent 世界迭代使用 `qinjunw/CozyTown` 的 GitHub Issues。GitHub 保存地图、子任务、领取、阻塞和结论；仓库保存 PRD、ADR、运行证据及指向 Issue 的入口。其他本地地图不随本次迁移改变。

## Wayfinding operations

| 操作 | GitHub 表达 |
| --- | --- |
| 地图 | 一个带 `wayfinder:map` 标签的 Issue；正文按 Destination、Notes、Decisions so far、Not yet specified、Out of scope 组织 |
| 票据 | 地图的原生子任务，带对应 `wayfinder:<type>` 标签；正文记录问题和所需证据 |
| 领取 | 开始前分配给执行开发者；未分配表示尚未领取 |
| 阻塞 | 原生 blocked-by 关系，不用正文清单代替 |
| 前沿 | 开放、未分配且所有阻塞票据均已关闭的子任务 |
| 结论 | 发布 `Resolution` 评论并关联 ADR、验证、提交和 PR；确认结果后关闭票据 |
| 地图索引 | 已关闭票据的一行结论和以标题命名的链接；开放任务由原生子任务关系查询 |

实施票据可以在独立分支验证完成后关闭；Resolution 必须注明 PR 是否已合并。依赖该实现的后续任务从已验证分支继续，或等合并后从主分支开始，不能把“票据关闭”等同于“主分支已交付”。

本次操作使用 GitHub CLI `2.97.0`，已核验下列命令。命令中的编号仅为 API 参数；说明、地图索引和结论链接使用完整任务标题。

```powershell
# 读取地图和按顺序排列的子任务
gh issue view $mapNumber --repo qinjunw/CozyTown --json title,body,state,url
gh api "repos/qinjunw/CozyTown/issues/$mapNumber/sub_issues" --paginate

# 检查每个开放且未分配子任务的实际阻塞状态
gh api "repos/qinjunw/CozyTown/issues/$ticketNumber/dependencies/blocked_by" --paginate

# 领取后再执行；只有所有阻塞票据关闭才进入前沿
gh issue edit $ticketNumber --repo qinjunw/CozyTown --add-assignee '@me'

# 先创建子任务，取得编号后再连接阻塞关系
gh issue create --repo qinjunw/CozyTown --parent $mapNumber --title $ticketTitle --label 'wayfinder:task' --body-file $questionFile
gh issue edit $ticketNumber --repo qinjunw/CozyTown --add-blocked-by $blockingTicketNumber

# 完成后记录结论，关闭票据，更新地图的一行索引
gh issue comment $ticketNumber --repo qinjunw/CozyTown --body-file $resolutionFile
gh issue close $ticketNumber --repo qinjunw/CozyTown --reason completed
gh issue edit $mapNumber --repo qinjunw/CozyTown --body-file $mapBodyFile
```

正文与评论先保存为 UTF-8 文件，再用 `--body-file` 提交，以保留中文和实际换行。更改地图前重新读取远程正文，保留其他任务已写入的内容。重复执行创建前先按标题和父子关系查询，避免生成重复票据。

接口依据：[GitHub 子任务](https://docs.github.com/en/rest/issues/sub-issues)、[GitHub Issue 依赖](https://docs.github.com/en/rest/issues/issue-dependencies)。独立 Projects 看板是可选视图，不承担本地图的规范记录；本次仅使用已有仓库 Issues 权限。
