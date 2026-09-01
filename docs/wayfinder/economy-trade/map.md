# 守恒型角色—商店经济重构

- Label: `wayfinder:map`
- Status: Open
- Tracker: https://github.com/qinjunw/CozyTown/issues/3

## Destination

形成一份可直接进入 TDD 实施的经济重构决策：角色拥有背包与资金，商店拥有库存与资金，购买和出售以原子交易转移资源；每日刷新是唯一明确允许注入或移除商店资源的外部边界，并可被存档、复现和测试。

## Notes

- 使用 `baseline`、`tdd`、`domain-modeling`、`research` 和 `technical-writing-review`。
- 保持 Unity 项目内模块化单体、Runtime 不依赖 UnityEngine、Unity 适配层只取得窄用例接口。
- 先完成研究和决策，不在决策地图阶段修改业务实现。
- 数据库不是默认目标；只有查询规模、内容维护、迁移或多主体持久化证据超过现有版本化 JSON 能力时才引入。

## Decisions so far

- [审计现有经济、背包、商店与存档边界](tickets/audit-current-economy-and-save-boundaries.md)：现有商店只绑定唯一全局玩家背包与钱包，没有自己的库存、资金或刷新状态；普通购买和出售不满足世界资源与货币守恒。
- [调研成熟角色库存与商店交易模型](tickets/research-inventory-shop-ledger-patterns.md)：成熟实现会分开静态物品定义、角色资产、商店资产和交易用例；同步单机交易应只发布同时满足双方不变量的完整候选状态。
- [调研商店状态持久化与数据库引入阈值](tickets/research-persistence-and-database-threshold.md)：当前规模继续使用内存聚合与版本化 JSON；静态内容可由代码配置或只读 ScriptableObject 提供，动态角色和商店状态进入存档。
- [决定角色身份、背包与资金的所有权](tickets/decide-character-inventory-ownership.md)：玩家和未来 NPC 共享 `CharacterEconomyState`，以稳定 `CharacterId` 标识并拥有 `Backpack` 与 `Wallet`。
- [决定商店聚合与资源守恒边界](tickets/decide-shop-aggregate-and-conservation-boundary.md)：角色与商店保持独立聚合，由经济 Unit of Work 一次提交双方候选状态或全部拒绝。
- [决定每日库存刷新与确定性随机规则](tickets/decide-daily-stock-refresh.md)：每日完整替换六类基础补给的目标库存，使用确定性随机并保持同日幂等；角色售入的非补货物品在下一日清退。

## Not yet specified

- NPC 成为可交易角色后，NPC 背包、资金和自主交易命令如何进入相同交易边界。
- 价格波动、稀缺度、关系折扣和多个商店是否需要建立独立规则。
- 商店列表、玩家出售入口和交易历史在 Unity UI 中如何呈现。
- 世界资源统计是否需要从不变量测试升级为运行时诊断或作品集可视化。

## Out of scope

- 商店向上游采购时支付成本；当前每日刷新被定义为外部补货/清退边界。
- AI 对话直接执行买卖、赠送物品或修改库存。
- NPC 日程、跨地图物流、拍卖行、多人经济和服务器权威交易。
- 为尚未出现的查询或并发需求预先部署关系数据库或网络服务。
