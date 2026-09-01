# 确认公开 TDD 缝隙与纵向迁移顺序

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:grilling`
- Status: Blocked
- Blocked by:
  - [决定角色身份、背包与资金的所有权](decide-character-inventory-ownership.md)
  - [决定商店聚合与资源守恒边界](decide-shop-aggregate-and-conservation-boundary.md)
  - [决定每日库存刷新与确定性随机规则](decide-daily-stock-refresh.md)
  - [决定经济状态存档迁移与存储方案](decide-save-schema-and-storage.md)

## Question

哪些公开应用用例、查询投影、跨日入口和存档接口构成本次 TDD 缝隙；如何按一个行为一个 RED→GREEN 切片迁移，同时持续保留现有生产经济闭环？
