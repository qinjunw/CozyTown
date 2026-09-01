# 确认公开 TDD 缝隙与纵向迁移顺序

- Parent: [守恒型角色—商店经济重构](../map.md)
- Label: `wayfinder:grilling`
- Status: Closed
- Assignee: `qinjunw`
- Tracker: https://github.com/qinjunw/CozyTown/issues/11

## Question

哪些公开应用用例、查询投影、跨日入口和存档接口构成本次 TDD 缝隙；如何按一个行为一个 RED→GREEN 切片迁移，同时持续保留现有生产经济闭环？

## Options

1. **按公开用例与状态投影测试（推荐）**：测试通过稳定 ID 调用经济状态存储、交易用例、商店投影、跨日用例和存档用例；只在持久化适配器契约测试中读取 schema 夹具。
2. **直接测试领域对象和私有容器**：单元测试更细，但会把测试绑定到字典、DTO 字段和内部拆分类，重构成本较高。
3. **主要通过 Unity 场景和 UI 测试**：接近人工体验，但故障定位慢，难以可靠注入提交失败、旧存档和固定随机输入。

## Confirmed public seams

- `IEconomyStateStore`：按稳定角色或商店 ID 返回不可变状态快照，并且只通过一次原子提交发布角色与商店候选状态。
- `ICharacterShopTradingCoordinator`：`Buy`、`Sell` 和 `GetCurrentState` 都显式接收 `shopId` 与 `characterId`；命令返回操作结果或交易收据，查询返回只读 UI 投影。
- `IDayTransitionCoordinator`：继续作为唯一跨日入口；测试通过经济状态投影观察确定性刷新、同日幂等与失败不发布，不直接调用刷新实现细节。
- `IGameSaveCoordinator` 与 `ISaveStorage`：验证 v2 往返、v1 迁移和无效加载不改变当前状态；JSON 字段只属于存储适配器契约测试。
- Unity presenter 只依赖上述用例和投影；PlayMode 测试不读取 Runtime 私有字典或存档 DTO。

## Confirmed vertical order

1. 建立按稳定 ID 读取的角色与商店状态，以及原子候选提交。
2. 迁移购买用例并验证物品、金币守恒和失败零变化。
3. 迁移出售用例并验证物品、金币守恒和失败零变化。
4. 迁移商店只读投影，使购买列表只展示现货，出售列表只展示角色持有且允许回收的物品。
5. 通过跨日入口实现确定性每日完整替换，并验证同日幂等。
6. 注入跨日参与者失败，验证时间、生产与商店状态均不发布。
7. 实现 schema v2 保存、v1 迁移、v2 往返和失败加载零变化。
8. 接线 Unity 商店、HUD 与存档入口，移除旧的全局玩家资产兼容路径。

每个步骤只增加一个先失败的公开行为测试，再做最小实现使其通过；不提前为后续步骤建立未被测试要求的扩展点。

## Resolution

采用选项 1，并确认上述公开测试缝隙和纵向顺序。Runtime 测试只观察稳定 ID 状态、交易结果、跨日结果和存档结果；Unity 测试只观察 Presenter 与公开用例接线。私有容器、MonoBehaviour 内部状态和非存储契约测试中的 JSON 字段不构成测试缝隙。

实施工作转入 [英文执行地图](../implementation-map.md)。
