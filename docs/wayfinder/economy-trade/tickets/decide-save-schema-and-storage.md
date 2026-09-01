# 决定经济状态存档迁移与存储方案

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:grilling`
- Status: Claimed
- Assignee: `qinjunw`
- Tracker: https://github.com/qinjunw/CozyTown/issues/10

## Question

现有 schema v1 应如何迁移到包含角色和商店状态的新版本，静态目录与运行时状态分别保存在哪里，读取失败和跨模块恢复如何维持原子性？

## Options

1. **版本化 JSON schema v2 与显式 v1 迁移（推荐）**：静态物品、价格和补货规则留在只读内容定义；动态角色资产、商店资产、实际库存与刷新状态进入完整快照。v1 的顶层背包和钱包迁入默认玩家，缺失的商店状态按确定规则建立。
2. **schema v2 但不迁移 v1**：实现较少，但现有存档无法继续读取，并违反已接受的存档版本化规则。
3. **改用 SQLite**：能提供索引和增量事务，但当前数据量、查询与并发需求没有达到引入阈值。
