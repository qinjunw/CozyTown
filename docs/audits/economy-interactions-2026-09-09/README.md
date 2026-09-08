# 经济交互审计：2026-09-09

## 结论与证据范围

种植、浇水、收获、喂鸡、收蛋、钓鱼、烹饪、买卖和存读档均有业务实现及正式场景接线。当前缺口主要在列表可达性、生产画面同步、按钮条件和操作反馈，不能把这些模块整体判为未实现。

审计基线为远端 `main` 的 `a3e09b0ef0181e7514b3997de70deba2f70e89ec`；开始时本地文件树一致、工作区干净。覆盖正式 `CozyTown_Dev.unity`、UI/Presenter、应用用例、经济与生产领域、内容配置和既有测试。三名子代理分别静态核对交易/背包、农田/畜牧、其余生产/存档；主代理检查公共 UI 并运行 Unity。

本轮只交付审计与规划，没有修补游戏实现、改动正式场景或启用真实 AI。诊断源保存在本目录，执行时临时加入 PlayMode 测试程序集，结束后移出；不会把已知失败探针混入正常回归套件。GitHub 记录见[审计经济交互并明确修补范围](https://github.com/qinjunw/CozyTown/issues/42)和[核验现有经济按钮与生产交易闭环](https://github.com/qinjunw/CozyTown/issues/43)。

## 交互覆盖

| 交互 | 实现与现有规则 | 本轮证据及未覆盖部分 |
| --- | --- | --- |
| 商店购买 | 从商店转物品到角色，同时把金币转给商店；检查库存、资金和背包容量 | 正式场景模拟鼠标点击成功，钱包实际扣款；没有逐个商品点击 |
| 商店出售 | 从角色转物品到商店，同时由商店支付；只列出持有且有回收价的物品 | 模拟鼠标出售鲤鱼成功，余额增加、背包对应条目消失；探针主动滚到该行，未证明自然发现路径 |
| 种植与浇水 | 六地块；行内选择三种种子，持有量为零时置灰；成长中的作物可浇水 | 模拟鼠标种胡萝卜、浇水成功，状态从 Empty 到 Growing/Watered；其余种子沿同一链路静态核对 |
| 收获 | 满足有效成长周期后可收，入包成功才清空地块 | 既有正式场景闭环和领域用例；本轮未新增真实鼠标收获探针 |
| 喂鸡与收蛋 | 固定一只母鸡；喂一次消耗一份饲料，下一晨间生成一枚待收蛋 | 模拟鼠标喂食成功；待收时再次点 Feed 的错误已复现；收蛋保留既有闭环证据 |
| 钓鱼 | 点击立即取得随机结果，成功入包或正常空竿 | 接线、失败回滚与既有闭环；本轮出售夹具使用固定抛竿结果，未新增真实鼠标抛竿验证 |
| 烹饪 | 五个配方，按材料原子扣除并生成料理 | 接线和既有闭环；材料说明缺失由代码确认，未逐个配方用鼠标执行 |
| 睡眠与结算 | 选择 1～12 小时；午夜只换日期，05:00 结算生产并替换商店库存 | 既有场景按钮用例及本轮 24 小时生产探针；产物服务变化正确，世界画面同步失败 |
| 保存与读取 | 单槽 schema v3，保存角色/商店资产、生产、时间和种子；支持 v1/v2 迁移 | 现有场景和领域回归；未访问用户真实存档，读档后生产画面不刷新的判断来自调用链 |
| 背包与快捷栏 | 背包为只读投影；1～5 只改变快捷栏高亮，不使用种子或饲料 | 属已声明范围，不是漏接的生产按钮；批量交易、物品使用、拖动均尚未实现 |

新游戏为空背包、300 金币。种子按钮初始全灰符合当前规则，需要先买种子；商店只回收作物、蛋、鱼和料理，种子、饲料、盐、面粉没有回收价。没有可售物品时不生成 Sell 行。商品按当日库存生成；默认种子下首日没有土豆种子，诊断改用当天实际在售种子，不能把缺货当成购买实现缺失。

## 已复现缺陷

### ECO-01：商品文字区域无法滚动列表（P2）

打开商店，将鼠标放在商品名称区域，滚动滚轮。正式场景诊断的 Raycast 结果为空，列表 Y 偏移在输入前后均为 0；鼠标事件没有送到 ScrollRect。

列表 Content 只挂 RectMask2D 和 ScrollRect，没有接收指针的背景；文字、图标与面板背景均不参与 Raycast。事件可命中操作按钮，但不能覆盖常见的文字与空白区域。这会影响寻找下面的商品、出售行和农田后续地块；农田同类影响由共享模板推导，鼠标失败探针本轮只测商店。

证据：[列表创建与滚动配置](../../../Assets/CozyTown/Unity/Editor/CozyTownProductionUiSceneUpgrader.cs)，第 716～750 行；[诊断探针](EconomyInteractionAuditPlayModeTests.cs)的 `ScrollWheel_OverProductLabelReachesShopList`。

验收应覆盖鼠标在文字、空白和按钮区域滚动，都能到达需要的行；不能用直接修改 `verticalNormalizedPosition` 代替输入验收。

### ECO-02：晨间结算后农田、母鸡仍显示旧画面（P2）

买种子和两份饲料，种植并浇水、喂鸡，关闭面板，睡过下一次 05:00。服务已经成长并产蛋，但世界贴图未更新。重新打开农田和鸡舍后，同一帧内才发生以下变化：

- `farm_plot_soil_watered` → `farm_plot_soil_dry`
- `crop_carrot_stage_00` → `crop_carrot_stage_01`
- `animal_hen_fed` → `animal_hen_product_ready`

原因是 WorldView 只在绑定、打开相应面板或执行相应生产命令时刷新，没有消费世界结算或加载状态变化。自然经过 05:00 和读档走同一缺失刷新链路，其影响为静态推导；本轮实测触发是睡眠跨晨间。

证据：[农田 Presenter](../../../Assets/CozyTown/Unity/Farm/CozyTownFarmDebugPresenter.cs)，第 33、56、62、70 行；[鸡舍 Presenter](../../../Assets/CozyTown/Unity/Coop/CozyTownCoopDebugPresenter.cs)，第 33、54、59、67 行；探针 `MorningSettlement_RefreshesWorldSpritesWithoutReopeningPanels`。

验收应在面板关闭时分别经过自然结算、睡眠和成功加载，直接检查世界中的土壤、作物阶段和鸡蛋表现，不先重开面板。

### ECO-03：待收鸡蛋时 Feed 仍启用，但操作必然被拒绝（P2）

喂鸡后跨过晨间，持有一份剩余饲料、尚未收蛋。状态为 `FedToday=false`、`ProductReady=true`，Feed 仍可点。模拟鼠标点击得到 `Feed failed: livestock.product_pending`；饲料保留一份，没有发生错误扣料。

View 的按钮条件仅检查 `!FedToday && OwnedFeedQuantity > 0`；领域还要求没有待收产物。

证据：[鸡舍 View](../../../Assets/CozyTown/Unity/Coop/CozyTownCoopDebugView.cs)，第 125 行；[畜牧服务](../../../Assets/CozyTown/Runtime/Livestock/InMemoryLivestockService.cs)，第 54 行；探针 `PendingEgg_DisablesFeedUntilCollected`。

验收应使用领域真实可达的待收状态，确认 Feed 禁用、Collect 可用，并解释“先收取鸡蛋”。

## 静态确认的界面缺口

| 编号 / 优先级 | 缺口与玩家影响 | 证据 / 建议验收 |
| --- | --- | --- |
| ECO-04 / P2 | 购买行全部排在出售行前面，没有买卖分区或滚动提示。内容窗口 111 高、行高 22；现货较多时出售入口在首屏下方。 | [Shop View](../../../Assets/CozyTown/Unity/Shop/CozyTownShopDebugView.cs)，149～190 行；[UI 模板](../../../Assets/CozyTown/Unity/Editor/CozyTownProductionUiSceneUpgrader.cs)，546、867 行。持有鱼/蛋进入商店，应能识别出售入口及当前位置。 |
| ECO-05 / P2 | 商店内容高度固定为 `18×22=396`，实际不用的行只隐藏；没有随行数缩高或显式重置滚动位置。少量商品、售罄或重开时可落在无内容区域。 | UI 模板 732 行；Shop View 76～90、192～195 行。静态推导，未单独实测滚到底；验收应覆盖少量行、售罄和重开。 |
| ECO-06 / P2 | 厨房只显示成品和灰色 Cook，不显示配方、需求数量、持有量和缺项。玩家无法从界面判断准备什么；烤鱼实际要求鲤鱼。 | [Kitchen View](../../../Assets/CozyTown/Unity/Kitchen/CozyTownKitchenDebugView.cs)，107～114 行；[已有材料投影](../../../Assets/CozyTown/Runtime/Application/CookingGameplayCoordinator.cs)，49 行。展示五配方的实际 Ingredients/Owned/Required。 |
| ECO-07 / P3 | 钱不够、商店钱不够、背包满等情况缺少就地理由，失败直接显示内部错误码；钓鱼正常空竿也显示 `Catch failed`。 | [Shop Presenter](../../../Assets/CozyTown/Unity/Shop/CozyTownShopDebugPresenter.cs)，175～177、194～196 行；[Pond Presenter](../../../Assets/CozyTown/Unity/Pond/CozyTownPondDebugPresenter.cs)，55 行。用玩家能执行的说明区分正常结果与操作失败。 |
| ECO-08 / P3 | 鸡舍未产蛋时仍显示固定 `Product: 1`；成熟作物显示 `Ready ... Needs water`，却只有 Harvest。 | Coop View 120 行；[Farm View](../../../Assets/CozyTown/Unity/Farm/CozyTownFarmDebugView.cs)，188、199、253 行。区分预计产量/待收数量，成熟态只提示收获。 |

场景模板虽有名为 `Buy Button` 和 `Sell Button` 的两个对象，运行时买卖行都使用索引 0 并改标签，再隐藏索引 1。这是现有渲染方式，不能凭旧 Sell 对象无 listener 判定卖出未实现。

## 经济数值与规则边界

价格与作物、配方、鱼池数据来自正式 [DefaultMvpContent.asset](../../../Assets/CozyTown/Content/DefaultMvpContent.asset)。动物定义由内容加载器补入默认一只母鸡、每次一蛋。以下净额假设成功出售，不计现实操作耗时。

| 生产 | 每轮购买成本 | 收成回收收入 | 净额 | 必要结算 |
| --- | ---: | ---: | ---: | --- |
| 土豆 | 20 | 2×20=40 | +20 | 2 次有浇水的晨间 |
| 胡萝卜 | 25 | 2×25=50 | +25 | 3 次有浇水的晨间 |
| 番茄 | 30 | 3×30=90 | +60 | 4 次有浇水的晨间 |
| 鸡蛋 | 10 | 20 | +10 | 喂食后下一晨间；待收时不能继续喂 |

料理增值按“食材直接出售的机会成本 + 盐/面粉购入成本”计算：

| 料理 | 输入折算 | 成品售价 | 加工增值 |
| --- | ---: | ---: | ---: |
| 烤土豆 | 土豆20+盐5=25 | 50 | +25 |
| 蔬菜汤 | 胡萝卜25+番茄30=55 | 60 | +5 |
| 烤鱼 | 鲤鱼25+盐5=30 | 55 | +25 |
| 番茄炒蛋 | 番茄30+蛋20=50 | 75 | +25 |
| 鱼派 | 鳟鱼35+蛋20+面粉10=65 | 100 | +35 |

当前配方和生产没有直接负收益项。蔬菜汤增值较少是数值差异，本轮没有证据将其判为错误。

需要后续明确的规则有两项：

1. **即时钓鱼的收益节奏。** 鲤鱼/鳟鱼/鲈鱼/空竿概率为 40%/30%/20%/10%，回收价为 25/35/45/0；单次抛竿产物期望回收价值为 29.5。点击没有耗材、等待或游戏时间成本，池塘模态又暂停世界时间，因此同一游戏分钟可反复获取鱼，受背包和最终出售的商店资金约束。这与需等待结算的农牧玩法存在投入差异；是否保留 Demo 简化规则，尚未决定。
2. **有限商店资金。** 玩家初始 300、商店 10,000；普通交易转移金币，晨间补货保留商店钱包。当前规则内这两主体合计 10,300，不靠每日补货增加金币。连续出售会耗尽商店付款能力；购买补给又能把钱转回商店，因此不能称必然永久死局或无限刷金币。应确认长期玩法是否继续采用这条已接受边界，并在界面清楚说明。

当前 UI 每次交易 1 件，Runtime 支持指定数量；界面没有批量或卖出全部。快捷栏使用物品、拖动背包、购入动物、多物种、扩地、料理加工时长也未实现；它们属于新增能力，不能与上述已有行为缺陷混为一谈。当前每个新局使用固定世界种子 12345，商店按日确定性补货；与旧 ADR 中新局生成种子的文字差异应在后续明确规则时同步文档。

## 验证结果与限制

| 本轮实际运行 | 结果 | 证据 |
| --- | --- | --- |
| 既有 EditMode 全量 | 450/450 通过，0 失败、0 跳过；Unity exit 0 | `Logs/economy-audit-editmode.xml`、`.log`、`.exit.txt` |
| 既有正式场景 PlayMode 类 | 6/6 通过，0 失败、0 跳过 | `Logs/economy-audit-existing-scene.xml` 与同名日志 |
| 经济交互诊断，图形 PlayMode | 4 项中 1 通过、3 失败，0 跳过；Unity exit 2 | `Logs/economy-audit-probe04.xml`、`.log`、`.exit.txt` |

通过项实际注入 Mouse 的按下/抬起，并在点击前验证 EventSystem 的首个 Raycast 命中；购买、种植、浇水、喂食和卖出的实际资产/生产断言通过。该探针主动将目标行移入可见区，因此只证明可见按钮可点，不替代滚动发现路径。

三项失败分别对应 ECO-01、ECO-02、ECO-03，尚未修复。诊断分辨率为 Unity 批处理报告的 `640×480`，并非 Scene-01 要求的三组分辨率验收。打开面板使用真实场景交互点入口，未模拟玩家从地图另一端完整走到每个点。批处理 Bootstrap 使用内存存档；本轮未接触用户主存档。

可随报告保存的运行摘要见 [verification.json](verification.json)；完整 XML 和日志在本地忽略目录 `Logs/`，新克隆不一定具备。

前期探针有一次编译适配错误和两次夹具修正（默认首日无土豆种子、成功反馈文字假设错误），对应早期 probe/probe02/probe03 日志；这些不能算游戏缺陷，表中只使用修正后的 probe04。探针源已由另一代理检查输入链路与夹具边界。

既有闭环多数调用 `RequestPlant/Feed/Catch/Cook/Sell`；手工 UI 用例调用 `onClick.Invoke()`。它们覆盖业务调用或事件路由，没有覆盖鼠标滚轮命中和所有按钮的自然可达性。旧鸡舍按钮夹具使用 `fed=true, ready=true`，漏测真实待收状态 `fed=false, ready=true`；旧场景用例在睡醒后先重开鸡舍再检查贴图，掩盖 ECO-02。

## 后续顺序

1. 先修已有行为：滚动命中、有效内容范围、生产画面同步、待收鸡蛋的 Feed 条件；从上述失败探针和公开 UI/状态结果建立回归。
2. 再明确界面呈现：买卖分区、配方材料、缺货/缺钱/缺材料提示和一致反馈；数量选择作为单独决定，不顺带重构经济存储。
3. 最后决定钓鱼节奏与有限资金是否需要调整；保留普通交易守恒，规则变化单独验收。

人工 Scene-01、Town-01 仍需用户分别记录；本报告、模拟鼠标和自动化结果均不代表人工通过。诊断复现方式见下节。

## 复现诊断

在没有 Unity 进程占用项目时，将本目录 `EconomyInteractionAuditPlayModeTests.cs` 临时复制到 `Assets/CozyTown/Tests/PlayMode/`。通过 Unity `6000.5.5f1` 的批处理 Test Runner 运行：

```text
<Unity.exe> -batchmode -projectPath <project-root> -runTests -testPlatform PlayMode -testFilter CozyTown.Tests.PlayMode.EconomyInteractionAuditPlayModeTests -testResults <audit-results.xml> -logFile <audit.log>
```

不要加入 `-quit` 或 `-nographics`。确认进程已退出后移除本次临时复制的源文件及其 Unity 生成的 `.meta`；保留本目录原件。探针要求 batch mode，以使用项目现有的内存存档隔离；已知三项失败是本次审计要观察的结果。
