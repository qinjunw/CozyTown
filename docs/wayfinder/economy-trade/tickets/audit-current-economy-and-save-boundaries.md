# 审计现有经济、背包、商店与存档边界

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:research`
- Status: Closed
- Assignee: `economy-code-auditor`

## Question

当前代码中谁拥有钱包、背包、商店报价和商品数量，购买、出售、生产产出、跨日与存档分别通过哪些公开接口改变状态；哪些现有测试和 ADR 会约束本次迁移？

## Resolution

[当前经济、交易与物品产出代码审计](../../../research/economy-trade/current-economy-code-audit.md)确认：当前唯一全局背包与钱包代表玩家；商店没有独立库存、资金或刷新状态；买卖只保证玩家两份状态的原子性；跨日和 schema v1 均未覆盖商店或角色身份。
