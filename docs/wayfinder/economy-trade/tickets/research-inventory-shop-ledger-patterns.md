# 调研成熟角色库存与商店交易模型

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:research`
- Status: Closed
- Assignee: `economy-pattern-researcher`

## Question

成熟单机 RPG 或模拟经营系统通常如何划分角色库存、商店库存、钱包、报价、原子转移、每日补货和确定性随机边界；哪些模式适合当前 Unity 模块化单体，哪些属于不必要复杂度？

## Resolution

[角色库存与商店交易架构调研](../../../research/economy-trade/inventory-shop-patterns.md)建议：角色和商店分别拥有库存与钱包，静态价格和补货规则与动态状态分离；应用用例同步原子转移双方资产；每日刷新是具名资源源／汇，并使用显式、可复现的随机输入。
