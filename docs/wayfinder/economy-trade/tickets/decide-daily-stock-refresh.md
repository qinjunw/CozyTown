# 决定每日库存刷新与确定性随机规则

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:grilling`
- Status: Claimed
- Assignee: `qinjunw`
- Tracker: https://github.com/qinjunw/CozyTown/issues/9

## Question

每日基础目录、随机缺货、数量区间、玩家售入商品保留/清退、刷新时机和随机种子应如何定义，才能既产生变化又允许存档恢复、测试复现和跨日回滚？

## Evidence

- [调研成熟角色库存与商店交易模型](research-inventory-shop-ledger-patterns.md)
- [调研商店状态持久化与数据库引入阈值](research-persistence-and-database-threshold.md)
