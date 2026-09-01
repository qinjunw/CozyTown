# 调研商店状态持久化与数据库引入阈值

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:research`
- Status: Closed
- Assignee: `economy-persistence-researcher`

## Question

在单机 Unity、单槽本地存档、少量物品和未来多个角色背包的条件下，版本化 JSON、ScriptableObject 静态定义、内存聚合与嵌入式数据库各自适合保存什么；什么可观察条件才足以支持引入 SQLite 或其他数据库？

## Resolution

[经济交易状态的持久化与数据库引入阈值](../../../research/economy-trade/persistence-database-threshold.md)建议：当前保留内存聚合、`ISaveStorage` 与版本化 JSON；动态角色和商店状态进入新 schema。只有实测全量存档瓶颈、无界历史与复杂查询或多进程本地访问等条件出现时，才原型验证 SQLite。
