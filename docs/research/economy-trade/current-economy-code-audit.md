# 当前经济、交易与物品产出代码审计

## 审计范围

- 代码基线：`d6a00cf`（`fix: normalize player movement sprite frames`）。
- 检查范围：Runtime 的 Economy、Inventory、Farming、Livestock、Fishing、Cooking、Time、Save、Application 与 Core 组合边界，以及商店和背包的 Unity 适配层与相关 EditMode/PlayMode 测试。
- 约束来源：根 `CONTEXT.md`、ADR-0001 至 ADR-0005、ADR-0007。
- 本文描述当前实现，不把候选重构方案记录为已接受决策。此次审计没有执行 Unity 测试，也没有修改业务代码。

## 直接结论

### 目前是否由商店模块完成所有买卖

当前所有“金币换物品”与“物品换金币”的游戏用例都通过 `IShopTradingCoordinator.Buy/Sell` 委托给 `IShopService.Buy/Sell`。Unity 商店 Presenter 也只接收 `IShopTradingCoordinator`，没有直接修改钱包或背包。因此，现有可玩路径的买卖集中在商店模块。

这不等于商店已经建模为交易参与者。`InMemoryShopService` 构造时绑定唯一的 `IWallet` 与 `IInventory`，购买只执行“玩家扣款、玩家加物”，出售只执行“玩家减物、玩家加款”。不存在商店侧的资产账户、库存或所有者身份。

证据：

- `Assets/CozyTown/Runtime/Economy/IShopService.cs:6-12` 只公开报价、购买和出售。
- `Assets/CozyTown/Runtime/Economy/InMemoryShopService.cs:11-13` 只持有报价字典、一个钱包和一个背包。
- `Assets/CozyTown/Runtime/Application/ShopTradingCoordinator.cs:54-59` 原样委托购买和出售。
- `Assets/CozyTown/Unity/Shop/CozyTownShopDebugPresenter.cs:148-163` 把 UI 请求送入协调器。

### 有没有真实商店库存与资金

没有。

- `ShopOffer` 只有 `ItemId`、`BuyPrice`、`SellPrice`，没有可售数量、已售数量、刷新日期或补货批次。
- `IShopService.Offers` 是构造时生成的固定只读集合；跨日不会改变它。
- `ShopViewState.Balance` 是玩家钱包余额，`ShopLineItem.OwnedQuantity` 是玩家背包数量。界面上的 `Town Shop — Coins` 不是商店资金。
- 默认内容为 18 种物品各配置一条报价：6 种只可购买，12 种只可出售。界面展示所有报价行，但价格为零的方向不可执行；这不是“每种物品都能双向交易”。

购买在当前实现中是物品源和金币汇：商品凭空进入玩家背包，玩家支付的金币没有进入另一钱包。出售是物品汇和金币源：物品离开玩家背包，金币凭空增加。两条路径在玩家自身状态内原子，但不满足交易双方的资源守恒。

证据：

- `Assets/CozyTown/Runtime/Economy/ShopOffer.cs:6-19`
- `Assets/CozyTown/Runtime/Economy/InMemoryShopService.cs:45-52`、`:75-82`
- `Assets/CozyTown/Runtime/Application/ShopViewState.cs:20-31`
- `Assets/CozyTown/Unity/Shop/CozyTownShopDebugView.cs:145-165`
- `Assets/CozyTown/Runtime/Content/DefaultMvpContent.cs:80-102`

### 背包是否属于人物

当前不属于人物领域对象。项目没有 Runtime `Player`、`Character`、`CharacterId` 或角色聚合。组合根创建一份全局 `InMemoryInventory` 和一份全局 `InMemoryWallet`，农田、畜牧、钓鱼、烹饪、商店、存档和各应用协调器共享这两个实例。

Unity 中的 `InteractionContext.Actor` 是 `GameObject`，商店 Presenter 只使用它取得 `PlayerModalInputGate2D`。交易并未根据 Actor 查找或选择背包。背包 Presenter 也只接收全局 `IInventoryProjection`。因此现有代码无法区分玩家 A、NPC B 或商店 C 的物品与货币。

证据：

- `Assets/CozyTown/Runtime/Core/CozyTownCompositionRoot.cs:44-52`
- `Assets/CozyTown/Runtime/Core/CozyTownServices.cs:69-75`
- `Assets/CozyTown/Unity/Interaction/InteractionContext.cs:5-13`
- `Assets/CozyTown/Unity/Shop/CozyTownShopDebugPresenter.cs:128-135`
- `Assets/CozyTown/Unity/Inventory/CozyTownInventoryPresenter.cs:25-28`

## 当前状态所有权与公开接口

| 状态 | 当前所有者 | 公开读写入口 | 当前持久化 |
| --- | --- | --- | --- |
| 玩家物品 | 唯一 `InMemoryInventory` | `IInventory.Count/Contains/Add/Remove/CaptureSnapshot/Restore`；UI 只用 `IInventoryProjection` | 是，顶层 `InventorySnapshot` |
| 玩家金币 | 唯一 `InMemoryWallet` | `IWallet.Balance/Credit/Debit/CaptureSnapshot/Restore` | 是，顶层 `WalletSnapshot` |
| 商店报价 | `InMemoryShopService._offers` | `IShopService.Offers` | 否；每次从静态配置重建 |
| 商店库存 | 不存在 | 无 | 否 |
| 商店资金 | 不存在 | 无 | 否 |
| 农田状态 | `InMemoryFarmService` | `IFarmService` | 是 |
| 畜牧状态 | `InMemoryLivestockService` | `ILivestockService` | 是 |
| 钓鱼状态 | 无动态状态；只有固定掉落区间 | `IFishingService.Entries/Catch` | 无需持久化当前规则 |
| 烹饪状态 | 无动态状态；只有固定配方 | `ICookingService.Recipes/CanCook/Cook` | 无需持久化当前规则 |
| 日历 | `InMemoryTimeService` | `ITimeService` | 是 |
| 人物身份与人物资产 | 不存在 | 无 | 否 |

`IInventory` 的容量按物品最大堆叠数计算占用槽位；快照只保存 `ItemId + Quantity`，不保存槽位位置、物品实例、品质、耐久或所有者。`InventoryProjection` 按物品目录顺序重新展开槽位，因此现有快捷栏和包裹是全局库存的只读投影，不是人物实体上的可变槽位数组。

## 当前交易事务与回滚

### 购买

1. 从固定报价中查找物品并计算 `BuyPrice × quantity`。
2. 捕获玩家钱包和全局背包快照。
3. 扣除玩家金币。
4. 向玩家背包添加物品。
5. 任一步失败时尝试恢复两份快照。

该事务覆盖玩家的两个状态，但不检查商店库存，也不增加商店资金。购买任意正数量只受玩家余额和背包容量限制。

### 出售

1. 从固定报价中查找物品并计算 `SellPrice × quantity`。
2. 捕获玩家钱包和全局背包快照。
3. 从玩家背包移除物品。
4. 向玩家钱包增加金币。
5. 任一步失败时尝试恢复两份快照。

该事务检查玩家确实拥有物品，因此现有“背包里有东西才能卖”已经成立；但仅限报价中 `SellPrice > 0` 的物品。出售不会把物品加入商店，也不检查商店能否支付。

### 当前回滚能力的边界

- 商店回滚覆盖两份玩家状态，且有注入失败测试覆盖“先变更后返回失败”和回滚失败诊断。
- 没有交易 ID、幂等键或持久化流水。连续收到两次合法输入会执行两笔交易。
- `ShopReceipt` 只记录物品、数量、总价和买卖方向，不记录玩家、商店、日期、单价版本或双方余额。
- `IWallet` 和 `IInventory` 作为公共写接口仍由 `CozyTownServices` 暴露给组合边界；ADR-0005 依靠 Bootstrap 的窄接口注入阻止 Unity View 越权，不构成 Runtime 内的编译期资产所有权约束。

## 生产、加工与资源源汇

| 流程 | 输入 | 输出 | 当前资源边界 | 失败处理 |
| --- | --- | --- | --- | --- |
| 播种 | 背包种子 1 | 地块进入成长态 | 物品转换为农田状态 | 捕获并恢复背包；地块在扣料成功后设置 |
| 收获 | 成熟地块 | 固定数量作物 | 生产源；成熟状态转换为作物 | 背包拒绝时保留成熟地块 |
| 喂食 | 饲料 1 | 动物当日已喂 | 物品转换为畜牧状态 | 捕获并恢复背包 |
| 跨日产物 | 已喂动物 | 待领取产物 | 状态转换，不立即进背包 | 由跨日协调器统一回滚时间、农田、畜牧 |
| 领取畜产品 | 待领取产物 | 固定数量产物 | 生产源；待领取状态转换为物品 | 背包拒绝时保留产物待领取 |
| 钓鱼 | 固定 roll | 鱼 1 | 直接资源源；无鱼饵或耐力输入 | 背包拒绝时恢复背包 |
| 烹饪 | 明确数量食材 | 明确数量料理 | 配方定义的原子资源转换 | 任一扣料或加成品失败时恢复背包 |

代码入口：

- 农田：`Assets/CozyTown/Runtime/Farming/InMemoryFarmService.cs:43-69`、`:126-149`
- 畜牧：`Assets/CozyTown/Runtime/Livestock/InMemoryLivestockService.cs:47-75`、`:81-98`、`:107-132`
- 钓鱼：`Assets/CozyTown/Runtime/Fishing/InMemoryFishingService.cs:26-50`
- 烹饪：`Assets/CozyTown/Runtime/Cooking/InMemoryCookingService.cs:34-69`

现有代码没有统一资源流水或守恒检查。它通过每个用例的原子性测试保证“不出现部分提交”，而不是证明全世界物品与货币总量守恒。对后续模型而言，钓鱼、收获、畜产品和每日商店补货应被明确标为外部资源源；烹饪、播种、喂食与交易应记录为主体之间或物品形态之间的转换。

## 跨日与每日库存

`DayTransitionCoordinator` 只协调时间、农田和畜牧。它先捕获三份快照，按“时间 → 农田 → 畜牧”推进，失败后恢复三者。商店没有参与跨日事务，也没有 `AdvanceDay`、`Refresh`、`LastRefreshDay` 或随机源接口。

因此当前行为是：

- 第 1 天与后续日期看到同一组固定报价；
- 可购买物品没有数量上限；
- 玩家卖出的物品不会出现在商店库存中；
- 不存在某天完全缺货的商品；
- 不存在可重放的每日随机库存。

证据：`Assets/CozyTown/Runtime/Application/DayTransitionCoordinator.cs:11-29`、`:50-101`。

## 存档边界

schema v1 顶层只包含 `Clock`、`Inventory`、`Wallet`、`Farm` 和 `Livestock`。`GameSaveCoordinator` 捕获并恢复这五个模块，失败时也只回滚这五个模块。JSON 文件适配器和内存适配器都遵循该契约。

当前存档能够恢复玩家金币和全局背包，但不能恢复以下新增需求：

- 商店资金；
- 商店库存及玩家/NPC 卖入的物品；
- 当日随机上架列表与已售数量；
- 最后刷新日、刷新种子或补货批次；
- 人物 ID 与人物资产归属；
- 多个 NPC 的背包或钱包。

若这些字段进入权威状态，不能作为 schema v1 的隐式默认扩展。根据 ADR-0003，字段拆分和语义变化需要增加 schema 版本并提供 v1 到新版本的确定迁移。当前 v1 的顶层 Inventory/Wallet 需要明确迁移到默认玩家，而商店需要确定初始资金和首日库存的迁移规则。

证据：

- `Assets/CozyTown/Runtime/Save/GameSaveSnapshot.cs:11-41`
- `Assets/CozyTown/Runtime/Application/GameSaveCoordinator.cs:75-124`
- `Assets/CozyTown/Tests/EditMode/Application/GameSaveCoordinatorTests.cs:21-169`

## 现有测试覆盖与缺口

### 已有覆盖

- `InMemoryShopServiceTests`：购买成功、余额不足、背包满、出售数量不足、钱包/背包变更后失败、回滚失败诊断。
- `ShopTradingCoordinatorTests`：报价与名称/玩家持有数量的稳定投影、买卖委托和更新后状态。
- `ProductionEconomyLoopTests`：默认内容下完成购买、种植、畜牧、钓鱼、烹饪、出售和再次购买。
- 各生产模块测试覆盖背包拒绝时不丢失种子、产物或食材。
- `DayTransitionCoordinatorTests` 和 `GameSaveCoordinatorTests` 覆盖现有模块的跨日与读取回滚。

### 尚无覆盖

- 商店库存扣减、售罄和玩家出售后入库；
- 商店资金收支与资金不足时拒绝收购；
- 交易双方四份资产状态的原子提交与回滚；
- 交易后的物品与货币守恒；
- 每日刷新范围、允许完全缺货、最小总库存和确定性随机；
- 补货与玩家卖入库存的合并、替换或过期规则；
- 同一天重复刷新幂等、跨日失败回滚和存读档复现；
- 玩家与 NPC 背包隔离、错误主体无法出售他人物品；
- v1 存档迁移到人物资产与商店资产后的兼容性。

## 迁移风险

### 高风险：把全局背包直接改成 `Character.Inventory` 字段

农田、畜牧、钓鱼、烹饪、商店、存档和 UI 都持有同一个 `IInventory`。直接移动字段会同时改变六个模块的构造关系、存档结构与 Unity 绑定。若让 Unity `GameObject` 成为权威所有者，还会违反 ADR-0001 和 ADR-0005 的 Runtime/Unity 边界。

迁移时应先定义 Runtime 人物身份和资产边界，再让当前默认玩家拥有现有 v1 背包与钱包。场景 Actor 只携带或映射稳定 `CharacterId`，不保存权威资产集合。

### 高风险：在现有 `InMemoryShopService` 内增添几个库存字典

当前服务同时承担报价查找和玩家资产事务。加入商店库存与资金后，购买/出售至少涉及玩家钱包、玩家背包、商店钱包、商店库存四份可变状态；沿用当前两快照回滚会留下部分提交风险。商店状态还必须加入跨日和存档协调器。

### 中风险：把“报价存在”继续等同于“当天有货”

静态价格规则、商店可经营的品类、当天上架和实时剩余库存是不同概念。继续复用单个 `ShopOffer` 会导致售罄、只收购不上架、玩家卖入后可购买、每日缺货等状态无法明确表达。

### 中风险：使用不可复现的全局随机数补货

若刷新依赖 Unity 全局随机状态，保存后重载、测试重放和跨日失败回滚可能得到不同库存。每日刷新需要显式随机输入，或持久化实际刷新结果及最后刷新日；同一天重复调用必须幂等。

### 中风险：遗漏 schema 升级

把商店库存和资金只保存在运行时会在读档后重置，允许重复补货或恢复已卖出的商品。把顶层玩家 Inventory/Wallet 政名而不迁移会使现有存档失效。

### 低风险：过早引入数据库

当前只有单机、单槽位、小规模物品目录和少量聚合快照。版本化 JSON 已提供原子文件替换，数据库不能替代领域事务、状态所有权、刷新规则或 schema 迁移设计。只有出现大量商店/NPC、查询型交易历史、独立局部保存、模组数据或需要长期审计流水等可观察需求时，SQLite 才有明确收益。

## 后续设计必须先明确的契约

以下项目应在实施前通过领域决策与 RED 测试固定；本文不替代相应 ADR 或 TDD 接缝确认：

1. **人物资产所有权**：默认玩家的稳定 ID；背包与钱包是否同属人物资产；NPC 是否采用同一资产模型。
2. **商店聚合**：商店稳定 ID、资金、库存、价格规则、最后刷新日及快照。
3. **交易参与者**：购买和出售显式接收哪个人物与哪个商店，不能继续隐式绑定唯一全局背包。
4. **守恒边界**：正常交易同时移动双方金币和物品；生产与每日补货作为显式资源源；每日淘汰作为显式资源汇。
5. **库存刷新**：候选品类、数量范围、允许缺货、保底规则、玩家卖入库存如何保留或刷新掉、同日幂等及随机复现方式。
6. **商店资金**：初始资金、收购资金不足的失败结果，以及是否存在任何每日资金补充。当前需求只明确初始资金较多，没有授权每日凭空补钱。
7. **存档迁移**：schema v1 的顶层背包/钱包迁移到默认玩家；新商店状态的确定默认值；读取和回滚覆盖新增聚合。
8. **公开 TDD 接缝**：从人物资产、商店快照、交易结果、跨日结果和存档往返验证行为，不读取私有字典或 Unity 场景对象。

在这些契约明确前引入数据库会固化尚未确定的数据形状；先完成领域模型和事务边界，再决定持久化适配器，符合现有模块化单体与版本化存档决策。
