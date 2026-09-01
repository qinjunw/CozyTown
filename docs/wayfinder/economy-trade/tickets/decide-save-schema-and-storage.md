# 决定经济状态存档迁移与存储方案

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:grilling`
- Status: Blocked
- Blocked by:
  - [审计现有经济、背包、商店与存档边界](audit-current-economy-and-save-boundaries.md)
  - [调研商店状态持久化与数据库引入阈值](research-persistence-and-database-threshold.md)
  - [决定角色身份、背包与资金的所有权](decide-character-inventory-ownership.md)
  - [决定商店聚合与资源守恒边界](decide-shop-aggregate-and-conservation-boundary.md)
  - [决定每日库存刷新与确定性随机规则](decide-daily-stock-refresh.md)

## Question

现有 schema v1 应如何迁移到包含角色和商店状态的新版本，静态目录与运行时状态分别保存在哪里，读取失败和跨模块恢复如何维持原子性？
