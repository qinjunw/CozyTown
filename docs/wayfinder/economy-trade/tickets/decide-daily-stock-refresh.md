# 决定每日库存刷新与确定性随机规则

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:grilling`
- Status: Closed
- Assignee: `qinjunw`
- Tracker: https://github.com/qinjunw/CozyTown/issues/9

## Question

每日基础目录、随机缺货、数量区间、玩家售入商品保留/清退、刷新时机和随机种子应如何定义，才能既产生变化又允许存档恢复、测试复现和跨日回滚？

## Evidence

- [调研成熟角色库存与商店交易模型](research-inventory-shop-ledger-patterns.md)
- [调研商店状态持久化与数据库引入阈值](research-persistence-and-database-threshold.md)

## Resolution

用户确认按日完整替换目标库存：自动补货只覆盖三种种子、鸡饲料、盐和面粉；每日保证至少四类基础补给有货。鸡饲料出现率为 100%、数量 6–12；每种种子出现率为 70%、数量 3–6；盐和面粉出现率均为 75%、数量 3–8。

角色售入的鱼、作物、鸡蛋和料理进入当天商店库存，并在下一次刷新时清退。刷新使用 `WorldSeed`、`ShopId`、日期和算法版本生成确定结果；同日重复刷新不改变库存，刷新不改变商店资金。存档必须恢复刷新及后续交易形成的实际库存，日切失败不能发布任何参与模块的新状态。
