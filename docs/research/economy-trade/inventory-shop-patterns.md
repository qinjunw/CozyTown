# 角色库存与商店交易架构调研

## 1. 调研范围与结论

本报告研究单机 RPG／模拟经营游戏中的角色库存归属、商店库存与资金、价格、原子买卖、玩家售入商品、每日补货和确定性随机。资料截至 2026-09-01；外部证据仅采用 Unity、Microsoft、SQLite 的官方文档，以及 OpenMW 官方仓库的文档和源代码。

本报告基于以下实现假设：游戏保持离线单玩家、当前只有一种整数货币和一间小镇商店；普通物品按数量堆叠，不含耐久度或唯一实例；每日刷新在睡眠进入下一天时触发；同一进程不会并发提交两笔交易。若这些条件变化，需要重新评估聚合和持久化边界。

适合 CozyTown 当前模块化单体的结论如下：

1. 保留 `ItemDefinition`／价格／补货规则作为静态定义，把角色背包、商店库存、双方资金和最后补货日作为运行时状态。
2. 玩家和未来 NPC 使用同一种“角色经济状态”：稳定角色 ID、背包和钱包。Unity 场景角色只读取投影或调用用例，不直接修改集合。
3. 商店不是全局报价表。每间商店应拥有独立库存、独立钱包、价格策略和补货状态；静态报价只说明“允许交易及如何定价”。
4. `CharacterShopTradingCoordinator` 作为买卖用例入口；一次成交必须先计算满足不变量的双方候选状态，再通过单一提交边界发布，不能把“依次修改四份状态后尽力回滚”当作强原子性。
5. 每日补货或清退是显式的外部资源源／汇；“全量替换目标库存”“补到目标数量”或“选择性清退”仍是待决策略。普通买卖不产生或销毁物品与货币。
6. 当前数据规模不需要数据库。先扩展现有内存领域模型和版本化存档；只有出现大量角色／商店、交易历史查询、局部写入或多进程访问需求时，再在存档基础设施层评估 SQLite。

不建议直接引入 Unity Game Foundation。它提供 Inventory／Wallet／Transaction／Storefront 分层参照，但 Unity 2020.3 手册把 0.7–0.9 标为 preview；当前项目运行在 Unity 6，直接采用该旧包会新增兼容性和迁移风险。[Unity Game Foundation 版本说明](https://docs.unity.cn/cn/2020.3/Manual/com.unity.game-foundation.html)

## 2. 一手资料中的成熟模式

### 2.1 定义、库存实例与钱包分离

Unity Game Foundation 将 Inventory Item Definition 定义为创建玩家持有物品实例的模板，运行时实例可以持有可变属性；Inventory Manager 根据定义创建、查找和移除实例。[Inventory Item Definition](https://docs.unity.cn/Packages/com.unity.game-foundation%400.9/manual/CatalogItems/InventoryItemDefinition.html)、[Inventory Manager](https://docs.unity.cn/Packages/com.unity.game.foundation%400.4/manual/GameSystems/InventoryManager.html)

同一套官方架构把 Wallet 专门用于货币余额，把 Inventory 用于需要按实例追踪或在包裹中排列的物品。钱包只调整币种余额，不创建物品对象。[Wallet Manager](https://docs.unity.cn/Packages/com.unity.game-foundation%400.9/manual/GameSystems/WalletManager.html)

这支持以下项目内边界：

- `ItemDefinition`：稳定物品 ID、显示名、类别、堆叠上限等静态事实。
- `Inventory`：某个所有者当前持有的 `itemId -> quantity` 或物品实例。
- `Wallet`：某个所有者当前持有的货币余额。
- `PricePolicy`：某间商店对某物品的零售价和收购价，不属于物品持有状态。

Unity 官方把 ScriptableObject 定义为共享数据容器，同时明确说明已部署的 Player 不能用 ScriptableObject 保存运行时数据。因此，未来可以用 ScriptableObject 编写物品、商店和补货规则，但背包、库存和余额仍应进入运行时状态与存档。[Unity 6 ScriptableObject 手册](https://docs.unity3d.com/6000.1/Documentation/Manual/class-ScriptableObject.html)

### 2.2 库存属于角色或容器，而不是商店 UI

OpenMW 的公开 Lua API 从 Actor 取得 Inventory，并对该库存执行 `getAll`、`find` 和 `resolve`；这说明背包是角色／容器的状态，而不是交易窗口的内部列表。[OpenMW Actor Inventory API](https://github.com/OpenMW/openmw/blob/262d1ba0d6a7e86bd0b34d69616c6ef98c5e3a80/files/lua_api/openmw/core.lua)

OpenMW 编辑器文档也把商人物品直接配置在商人的库存中，并用负数量表达会恢复到目标数量的货物。这是“角色库存 + 声明式补货规则”的现有引擎实现先例。[OpenMW：Adding to an NPC](https://github.com/OpenMW/openmw/blob/262d1ba0d6a7e86bd0b34d69616c6ef98c5e3a80/docs/source/manuals/openmw-cs/tour.rst#adding-to-an-npc)

适用于 CozyTown 的所有权模型是：

```text
CharacterEconomyState
├── CharacterId
├── Backpack : Inventory
└── Wallet   : Wallet

ShopState
├── ShopId
├── Stock             : Inventory
├── Wallet            : Wallet
└── LastRestockedDay
```

`CharacterEconomyState` 可以先只为玩家创建一份，未来给 NPC 创建相同类型的实例。表现层的 Player／NPC GameObject 不应持有可变集合；它们引用稳定角色 ID，并通过角色存储库或应用用例取得对应状态。这样可以满足“人物有背包”，同时避免把存档、交易和 UI 逻辑绑在 MonoBehaviour 生命周期上。

### 2.3 商店目录、可见库存与交易是三个概念

Unity Game Foundation 的 Catalog 保存静态项目；Store 保存可展示的 Transaction 集合；Virtual Transaction 分别声明成本和收益，并提供成本／收益校验。[Game Foundation 总览](https://docs.unity.cn/Packages/com.unity.game-foundation%400.9/manual/index.html)、[Store API](https://docs.unity.cn/Packages/com.unity.game-foundation%400.7/api/UnityEngine.GameFoundation.Store.html)、[VirtualTransaction API](https://docs.unity.cn/Packages/com.unity.game-foundation%400.7/api/UnityEngine.GameFoundation.VirtualTransaction.html)

对应到本项目：

- 商品目录回答“这个物品是什么”。
- 价格与接纳策略回答“这间商店是否出售／收购，以及单价是多少”。
- 商店库存回答“现在实际有多少件可买”。
- 玩家背包回答“现在实际有多少件可卖”。
- 交易用例回答“能否以当前状态完成交换”。

因此 UI 应分别生成两个投影：

- 购买列表：`shopStock > 0` 且零售价有效的商店物品。
- 出售列表：`playerBackpack > 0`、商店接受收购且商店资金足够的玩家物品。

玩家售出的物品增加到 `ShopState.Stock`。如果希望这些鱼或农产品之后可以被买回，价格定义必须同时提供正零售价和正收购价；如果零售价为 0，则该物品即使进入库存也只能作为“商店收购但不再出售”的物品处理。

新接口应使用 `RetailUnitPrice`（玩家向商店买入）和 `AcquisitionUnitPrice`（商店向玩家收购），避免 `BuyPrice`／`SellPrice` 无法说明观察者视角的问题。

### 2.4 成交是单个原子业务操作

OpenMW 的交易入口先检查玩家或商人是否有足够金钱；报价接受后，它提交双方物品转移，再以相反方向调整玩家金钱与商人金池。[OpenMW `TradeWindow::onOfferButtonClicked`](https://github.com/OpenMW/openmw/blob/262d1ba0d6a7e86bd0b34d69616c6ef98c5e3a80/apps/openmw/mwgui/tradewindow.cpp#L2366-L2506)

Microsoft 的 DDD 指南把需要在一个业务动作结束时保持一致的对象视为事务边界，并指出跨聚合使用单事务还是最终一致性取决于领域需求；关系型持久化下，在提交前同步处理相关变更是较简单的起点。[聚合与事务一致性](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/microservice-domain-model)、[跨聚合领域事件与单事务](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)

CozyTown 是单进程、单玩家的模块化单体，交易要求又明确需要即时守恒，所以不适合引入消息队列或最终一致性。成熟模型给出的关键约束不是“必须用数据库事务”，而是只发布完整候选结果。建议由一个应用层交易协调器同步执行：

```text
BuyFromShop(shopId, buyerCharacterId, itemId, quantity)
  read current character and shop snapshots
  validate quantity, price, shop stock, player capacity, player funds
  derive candidate character and shop snapshots without publishing mutations
  candidate shop stock    -= quantity
  candidate player bag    += quantity
  candidate player wallet -= total
  candidate shop wallet   += total
  publish both candidates through one commit boundary, or publish neither

SellToShop(shopId, sellerCharacterId, itemId, quantity)
  read current character and shop snapshots
  validate quantity, acceptance, player stock, shop capacity, shop funds
  derive candidate character and shop snapshots without publishing mutations
  candidate player bag    -= quantity
  candidate shop stock    += quantity
  candidate shop wallet   -= total
  candidate player wallet += total
  publish both candidates through one commit boundary, or publish neither
```

提交边界可以由持有内存聚合的经济状态仓库或等价 Unit of Work 提供：它接收两份已经校验的候选快照，成功时一次替换当前角色和商店状态，失败时不改变可观察状态。具体公开接口仍需在商店聚合决策中确认。现有 `Restore` 可能失败，所以只能用于异常诊断，不能作为强原子性的基础。

前置校验还应覆盖：无效数量、整数溢出、不支持的交易方向、背包或商店容量不足。当前单线程 UI 可以让“获取当前投影 + 立即执行命令”共享同一价格策略；只有未来出现并发交易或长期报价缓存时，才增加库存 revision 与过期报价拒绝机制。

对允许双向交易的同一物品，零售价必须高于收购价，或至少禁止 `sellPrice > buyPrice`。OpenMW 源代码也对 NPC 买卖报价做 75% 上限，注释明确说明其目的是避免同一物品反复买卖产生套利。[OpenMW 报价约束](https://github.com/OpenMW/openmw/blob/262d1ba0d6a7e86bd0b34d69616c6ef98c5e3a80/apps/openmw/mwgui/tradewindow.cpp#L2714-L2767)

## 3. 每日补货与确定性随机

### 3.1 补货语义候选

用户需求允许每日列表凭空增减世界物品，也允许玩家前一天卖入商店的物品在刷新时消失。这里至少有三种可实施策略，尚需单独决策：

- **候选 A：按日替换目标库存。** 新的一天生成完整目标表并替换现有库存；没有抽中的物品归零。语义最清晰，能够自然清退玩家售入商品，但会每天删除全部未被新列表选中的剩余货物。
- **候选 B：补到目标数量。** 低于目标数量时补足，高于目标数量时保留。资源只会注入，不会自动清退；玩家售入物品可能长期累积。
- **候选 C：基础库存与售入库存分区，选择性清退。** 可以精细控制过期与保留，但会增加库存批次、来源和存档字段，不符合当前最小实现优先级。

若选择候选 A，可采用以下规则：

1. 商店定义包含多条 `RestockRule(itemId, appearancePermille, minQuantity, maxQuantity)`。
2. `appearancePermille = 1000` 表示核心常驻商品；小于 1000 表示当天可能完全没有。
3. 商店级 `minimumDistinctItems` 确保每日品类不会过少；第一次抽取不足时，用同一随机源从未入选候选中补足。
4. 新的一天生成完整目标表，并用目标数量替换 `ShopState.Stock`；不在目标表中的数量变为 0。
5. 当日玩家／NPC 售入的商品在目标库存上继续累加，直到下一次日刷新被替换。
6. 补货不扣商店资金，商店资金只随买卖变化。这与“暂时不考虑进货成本”一致。

无论选择哪种策略，`RefreshForDay(day)` 都必须幂等：若 `LastRestockedDay == day`，再次调用不得重抽或覆盖当日交易。日切换协调器应把时间、农田、畜牧和商店刷新纳入同一提交边界，不能依赖可能失败的逐模块恢复来声称强原子性。

### 3.2 可复现随机

Unity 说明 `UnityEngine.Random` 是全局共享静态状态；需要多个相互独立的随机源时应管理独立实例。Unity Mathematics 的 `Random` 由调用者显式持有状态，并允许不同来源使用不同 seed。[UnityEngine.Random](https://docs.unity3d.com/kr/current/ScriptReference/Random.html)、[Unity Mathematics Random](https://docs.unity.cn/Packages/com.unity.mathematics%401.3/manual/random-numbers.html)

当前项目不必仅为补货引入 Mathematics 包。可以在 Runtime/Core 定义窄接口 `IRandomSource`，生产实现使用项目固定且带版本号的整数 PRNG 算法，测试实现返回预设序列。每次补货的 seed 应由以下稳定字段组合：

```text
restockSeed = StableHash(worldSeed, day, shopId, restockAlgorithmVersion)
```

禁止使用 `string.GetHashCode()` 生成可持久化 seed；Microsoft 明确说明相同字符串的哈希值可能随 .NET 实现、版本、平台甚至应用域变化，不能持久化。[String.GetHashCode 稳定性约束](https://learn.microsoft.com/en-us/dotnet/api/system.string.gethashcode)

保存 `WorldSeed`、`LastRestockedDay`、补货算法版本和实际 `ShopState.Stock`。只保存 seed 而不保存库存不足以恢复当日状态，因为补货之后的买卖已经改变库存。版本化算法可以避免升级 PRNG 后旧存档在同一天生成不同货架。

## 4. 资源与货币守恒边界

普通交易需要同时满足以下不变量：

```text
Δ(playerCoins + shopCoins) = 0
Δ(playerItem[itemId] + shopItem[itemId]) = 0
```

资源源／汇必须是具名用例，不能隐藏在普通交易中：

| 用例 | 物品变化 | 货币变化 | 归类 |
|---|---:|---:|---|
| 玩家从商店购买 | 双方合计 0 | 双方合计 0 | 守恒转移 |
| 玩家向商店出售 | 双方合计 0 | 双方合计 0 | 守恒转移 |
| 每日补货 | 商店库存可增可减 | 0 | 外部源／汇 |
| 钓鱼、收获、畜产品 | 玩家库存增加 | 0 | 生产源 |
| 烹饪 | 输入减少、输出增加 | 0 | 配方转换 |
| 消耗／丢弃（未来） | 库存减少 | 0 | 资源汇 |

交易成功后返回不可变结果，至少记录双方 ID、方向、物品、数量、单价和总价，供 UI 与测试使用。当前不需要新增交易 ID 或持久化账本；只有出现经济审计、撤销、幂等重试或长期统计需求时，再扩展这些字段并保存交易历史。

## 5. 当前代码审计与差距

调研基线已将交易放在 Economy／Application 层，但当时的状态所有权不足：

- 重构前的 `CozyTownCompositionRoot` 只创建一份 `InMemoryInventory` 和一份玩家 `InMemoryWallet`，再把两者注入 `InMemoryShopService`。商店没有自己的库存或钱包。
- 已删除的 `InMemoryShopService` 购买只扣玩家资金并增加玩家物品；出售只减少玩家物品并增加玩家资金。它没有执行对手方的等量变化，所以重构前的普通交易不满足世界守恒。
- 已删除的 `ShopTradingCoordinator` 按静态 `Offers` 展示全部行，`OwnedQuantity` 读取玩家库存。它没有商店现货数量，也无法隐藏当天不存在的商品。
- [`DefaultMvpContent`](../../../Assets/CozyTown/Runtime/Content/DefaultMvpContent.cs) 把种子、饲料等配置成仅可买，把鱼、收获物和料理配置成仅可卖。鱼并非真的在商店库存中，只是存在静态收购报价。
- 调研基线 `d6a00cf` 中的 [`DayTransitionCoordinator`](https://github.com/qinjunw/CozyTown/blob/d6a00cf2372f1d226b753fd6ebbca34949c2b5b3/Assets/CozyTown/Runtime/Application/DayTransitionCoordinator.cs) 只推进 Time、Farm 和 Livestock，没有商店补货参与者。
- 调研基线 `d6a00cf` 中的 [`GameSaveSnapshot`](https://github.com/qinjunw/CozyTown/blob/d6a00cf2372f1d226b753fd6ebbca34949c2b5b3/Assets/CozyTown/Runtime/Save/GameSaveSnapshot.cs) 在经济状态方面只有全局 Inventory 和 Wallet，没有角色 ID、商店库存、商店资金、世界 seed 或最后补货日。

因此买卖仍应由 Economy 领域服务与 Application 协调器完成，但 `IShopService` 应演进为交易用例，而不是继续把静态报价表当作商店本身。玩家背包也不是新增第二份与现有库存并存的数据；现有全局 `IInventory` 应迁移为 `CharacterEconomyState(playerId).Backpack`，避免双重真相。

## 6. 是否引入数据库

SQLite 官方把单文件、跨平台、高级查询和原子事务列为应用文件格式的优点；其事务可以把多次写入组成全部成功或全部回滚的提交，并可在崩溃或掉电后维持原子性。[SQLite 作为应用文件格式](https://www.sqlite.org/appfileformat.html)、[SQLite 原子提交](https://www.sqlite.org/atomiccommit.html)、[SQLite 事务](https://www.sqlite.org/lang_transaction.html)

这些能力目前不是项目瓶颈。当前存档规模是少量角色、物品和一间商店；已有快照／回滚和 JSON 存档边界。现在引入 SQLite 会新增 Unity 原生库兼容、schema migration、连接生命周期和测试夹具成本，却不会自动修复领域层缺失的商店库存、资金及所有权模型。

建议保持当前内存领域模型与文件存档，直到至少出现以下一个触发条件：

- 数十到数百 NPC／商店需要按条件查询库存和经济状态；
- 需要持久化并检索大量交易历史、价格历史或审计记录；
- 需要局部增量保存而不是一次写入小型完整快照；
- 多进程或工具需要同时读取同一个世界状态；
- 存档体积或 JSON 迁移时间出现已测量的问题。

若未来触发，引入方式应是 `ICharacterEconomyRepository`、`IShopRepository`、`ITradeUnitOfWork` 的 SQLite 基础设施实现；Runtime 领域对象和交易规则不得引用 SQL、连接或 Unity 插件类型。SQLite 的磁盘事务负责保存提交的原子性，领域交易协调器仍负责业务不变量。

## 7. 建议的 TDD 重构顺序

每个阶段先增加失败测试，再实现最小行为；不要同时重写 UI、美术或生产玩法。

1. **所有权竖切**：新增玩家 `CharacterEconomyState`，证明玩家背包可独立快照／恢复；把现有玩家库存迁入该状态，确保存档往返后内容相同。
2. **商店状态竖切**：新增带库存和钱包的 `ShopState`；测试不同角色和商店的库存不会互相污染。
3. **购买守恒竖切**：RED 覆盖商店库存减少、玩家库存增加、玩家资金减少、商店资金增加；再覆盖库存不足、玩家背包满、玩家资金不足，以及提交拒绝或协作者失败时所有可观察状态保持提交前值。旧实现的回滚失败诊断只作为迁移期间的回归保护。
4. **出售守恒竖切**：RED 覆盖售出物品进入商店、商店资金减少；再覆盖商店资金不足、商店容量不足和玩家物品不足。
5. **投影竖切**：购买列表只显示商店现货；出售列表只显示玩家持有且商店收购的物品；两边显示各自库存和资金约束。
6. **补货竖切**：先确认刷新策略，再测试同一 world seed／day／shop ID 产生相同结果、同一天重复刷新不变、补货不改变商店资金，以及被选策略对缺货、保底和玩家售入库存的明确规则。
7. **日切与存档竖切**：日切先计算 Time／Farm／Livestock／Shop 的完整候选结果；任一计算或提交失败时不发布任何新状态。保存后加载能恢复玩家背包／钱包、商店库存／钱包、world seed 和最后补货日。
8. **性质测试**：对多组数量和价格验证普通买卖的物品总量与货币总量不变，补货是唯一允许绕过该不变量的商店入口。

完成这些领域测试后再改商店 UI，可以让 UI 仅消费 `ShopTradingViewState`，避免表现层成为第二套经济规则。
