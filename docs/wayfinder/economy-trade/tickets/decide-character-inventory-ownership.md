# 决定角色身份、背包与资金的所有权

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:grilling`
- Status: Open

## Question

项目应把“人物”规范为哪一种领域主体，玩家和未来 NPC 是否共享同一角色身份、背包与钱包模型；当前玩家专用服务如何迁移而不把 Unity Actor 或表现对象带入领域？

## Evidence

- [审计现有经济、背包、商店与存档边界](audit-current-economy-and-save-boundaries.md)
- [调研成熟角色库存与商店交易模型](research-inventory-shop-ledger-patterns.md)

## Options

1. **角色经济状态（推荐）**：Runtime `CharacterEconomyState` 以稳定 `CharacterId` 标识，拥有 `Backpack` 和 `Wallet`；玩家与未来 NPC 使用同一模型。Unity 的 Player/NPC GameObject 只映射 ID，不持有权威资产。
2. **独立经济账户**：角色只引用单独的 `EconomicAccountId`，账户拥有库存和资金。它支持组织、共享仓库等更广场景，但当前没有相应需求。
3. **保留玩家全局服务，NPC 以后另建**：改动较少，但会形成玩家与 NPC 两套所有权模型，并延续全局资产的双重真相风险。
