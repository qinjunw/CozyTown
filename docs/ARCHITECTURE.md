# CozyTown 架构说明

## 1. 范围与当前阶段

本架构采用 Unity 项目内的模块化单体。M4 已实现持久化与受约束的对话适配链路：交易与生产规则仍由确定性模块负责，应用层协调同一时点的保存与原子恢复，Runtime 文件适配器负责 JSON 槽位，Unity 层提供不含客户端密钥的 AI 代理适配并选择应用持久化路径。正式场景通过窄应用接口接入生产经济闭环、单槽位保存/读取和 4 名 NPC 对话。A1 独立美术阶段只替换 Unity 表现与资源引用，不修改这些领域和应用边界。T1 在此基础上增加扩镇与确定性居民日常；真实 Agent 接入仍受联合场景验收门禁约束。仓库当前状态和验证结果见 [`README.md`](../README.md)。

产品边界见 [`PRD.md`](PRD.md)。关键决策见：

- [`ADR-0001：模块化单体`](adr/0001-modular-monolith.md)
- [`ADR-0002：确定性领域与 AI 边界`](adr/0002-deterministic-domain-and-ai-boundary.md)
- [`ADR-0003：存档版本化`](adr/0003-save-versioning.md)
- [`ADR-0004：测试策略`](adr/0004-testing-strategy.md)
- [`ADR-0005：Unity 适配层与窄接口注入`](adr/0005-unity-adapter-boundary.md)
- [`ADR-0006：Production 美术场景接线`](adr/0006-production-art-scene-integration.md)
- [`ADR-0010：角色与商店独立拥有资产并原子提交交易`](adr/0010-character-shop-economic-ownership-and-atomic-trade.md)
- [`ADR-0013：确定性居民日程与派生位置`](adr/0013-deterministic-town-life-and-derived-npc-presence.md)

## 2. 架构目标与约束

- 一个 Unity 客户端承载 MVP，不引入独立游戏服务器或微服务拆分。
- 各业务模块通过接口协作，场景对象不直接修改模块内部状态。
- 领域规则使用普通 C# 类型，使 EditMode 测试无需加载场景。
- 运行时依赖由单一组合根显式创建，不使用全局静态服务定位器。
- AI 对话属于可替换的外部能力；确定性游戏循环在无网络时仍可测试和运行。
- 持久化使用稳定 ID 和版本化 DTO，不序列化 `MonoBehaviour`、`ScriptableObject` 引用或显示名称。

## 3. 代码组织

```text
Assets/CozyTown/
├─ Runtime/
│  ├─ Application/
│  ├─ Content/
│  ├─ Core/
│  ├─ Time/
│  ├─ Inventory/
│  ├─ Economy/
│  ├─ Farming/
│  ├─ Livestock/
│  ├─ Fishing/
│  ├─ Cooking/
│  ├─ Npc/
│  └─ Save/
├─ Unity/
│  ├─ Core/
│  ├─ Input/
│  ├─ Time/
│  ├─ Town/
│  ├─ CameraView/
│  ├─ Player/
│  ├─ Interaction/
│  ├─ Hud/
│  ├─ Shop/
│  ├─ Farm/
│  ├─ Bed/
│  ├─ Coop/
│  ├─ Pond/
│  ├─ Kitchen/
│  ├─ Npc/
│  ├─ Save/
│  └─ Editor/
├─ Scenes/
│  └─ CozyTown_Dev.unity
└─ Tests/
   ├─ EditMode/
   ├─ UnityEditMode/
   └─ PlayMode/
```

`CozyTown.Runtime` 是确定性领域与应用程序集，并设置 `noEngineReferences: true`。`CozyTown.Unity` 单向引用 Runtime 和 Input System；`CozyTown.Unity.Editor` 只在 Editor 平台编译。`CozyTown.Tests.EditMode` 验证普通 C# 行为，`CozyTown.Tests.UnityEditMode` 验证场景资产和不需要运行帧的 Unity 适配行为，`CozyTown.Tests.PlayMode` 验证 Physics2D、生命周期和正式开发场景装配。

## 4. 模块职责与接口

| 模块 | 公共入口 | 职责 | 不负责 |
| --- | --- | --- | --- |
| `Application` | `IDayTransitionCoordinator`、`ICharacterShopTradingCoordinator`、四个 `*GameplayCoordinator`、`INpcDialogueCoordinator`、`IGameSaveCoordinator` | 协调跨日、存档恢复事务，并向交易、生产、对话和存档表现层提供窄用例入口 | 表现、输入、数值平衡 |
| `Content` | `DefaultMvpContent`、`MvpContentValidator` | 提供代码默认内容和启动前引用/可达性校验；Unity 作者资产加载后仍经过同一 Runtime 校验入口 | 运行时状态、UI 编辑器 |
| `Core` | `CozyTownCompositionRoot`、`CozyTownServices` | 创建默认实现并公开类型化服务引用 | 业务规则、存档格式、场景查找 |
| `Time` | `ITimeService` | 当前天数和跨日推进 | 决定作物、动物的具体结算规则 |
| `Inventory` | `IInventory` | 物品数量查询、增加、移除和前置校验 | 价格、配方、掉落概率 |
| `Economy` | `IEconomyStateStore`、角色背包/钱包适配器、角色与商店经济快照 | 按稳定主体 ID 保存角色背包/钱包和商店库存/钱包，并原子发布交易候选 | 静态报价、物品生产、AI 决策 |
| `Farming` | `IFarmService` | 地块、播种、浇水、成长和收获状态 | 玩家移动、商店交易 |
| `Livestock` | `ILivestockService` | 鸡的喂食与鸡蛋产出状态 | 饲料定价、NPC 行为 |
| `Fishing` | `IFishingService` | 固定鱼池规则和钓鱼结果 | 实时操作 UI、背包显示 |
| `Cooking` | `ICookingService` | 配方查询、食材校验和烹饪事务 | 食材生产、料理表现 |
| `Npc` | `NpcContentCatalog`、`INpcDialogueGenerator`、`IAiNpcDialogueClient` | 校验并索引 NPC 作者内容，根据只读上下文校验 AI 候选并返回对话或固定回退 | 写入金币、物品、时间、生产或存档状态 |
| `Save` | `ISaveStorage`、`JsonFileSaveStorage` | 版本化存档快照的单槽读写、JSON 校验和安全替换 | 收集或直接修改各模块状态 |
| `Unity` | `CozyTownBootstrap`、输入门控、交互点、对话/存档及六组玩法 Presenter/View | 连接 Unity 生命周期、Input System、Physics2D、HTTP(S) 代理与窄接口 Presenter | 领域规则、全局服务解析、跨模块事务 |

接口输入和输出使用模块自己的 DTO 或值对象。公开集合应以只读视图或副本返回，调用方不能通过集合引用绕过模块规则。

### 4.1 T1 时钟与场景边界

`TownMap2D` 提供住宅、地点和共享道路的只读查询；`CozyTownTownLayout` 是铺地、地标及道路装配的共同来源。当前 NPC 仍为静态实体，地图路径查询不代表已经实现通勤。

`DaytimeClockCoordinator` 封装日内残余计时，以 `IDaytimeClock.AdvanceElapsed` 接收有效经过时长，5 秒推进 10 游戏分钟，在同日 23:59 封顶。组合根通过 `DaytimeClock`、`DayTransition`、`GameSave` 三个窄端口公开同一个实例；后两者委托原有事务，仅成功睡觉或加载后清空残余计时。保存和失败操作保留残余，存档 schema v2 不增加帧计时字段。

`DaytimeClockDriver` 只取得 `IDaytimeClock` 和玩家输入门控，不取得服务集合或存档写接口。它在 `LateUpdate` 过滤原始 `unscaledDeltaTime`：模态、失焦、未绑定或驱动器停用时不提交时长；真实暂停/绑定状态变化后的首样本丢弃，防止包含旧时间段的帧被补算。重复绑定同一对象不触发重置。该适配不修改全局 `Time.timeScale`。

`CozyTownBootstrap` 负责初始及晚注册绑定；公共场景升级入口维护一个时钟驱动器及其门控引用。未来 NPC 移动必须复用同一有效时长边界，不能只读取暂停布尔值后自行补算原始帧时间；T1-2 不提前增加无人使用的事件总线或日程接口。

## 5. 依赖方向

```text
Unity 场景、输入与 UI 适配
          │
          ▼
用例协调器 / 公开模块接口
          │
          ├──────────► 确定性模块实现
          │              Time / Inventory / Economy / Farming
          │              Livestock / Fishing / Cooking
          │
          ├──────────► Npc 对话端口 ──► 固定实现或 AI 适配器
          │
          └──────────► Save 端口 ─────► 内存实现或文件适配器

CozyTownCompositionRoot 只负责创建并连接上述对象。
```

依赖规则：

1. 表现层依赖接口，不依赖具体内存实现。
2. 具体实现可以依赖完成其事务所需的窄接口。例如生产模块通过默认角色的 `IWallet` 与 `IInventory` 适配器访问权威经济状态，但不能访问其内部集合。
3. 模块不能通过 `FindObjectOfType`、静态单例或字符串路径取得其他服务。
4. 双向依赖通过用例协调器、只读快照或领域事件解除；不得让两个模块互相持有具体实现。
5. `Npc` 生成器没有确定性模块的写依赖。调用方只向它传递复制出的上下文数据。
6. `Save` 适配器只读写版本化载荷。存档协调器负责向各模块导出和恢复状态。

## 6. 组合根

`Runtime/Core/CozyTownCompositionRoot.cs` 是默认对象图的唯一构造入口。`CreateDefault()` 创建经过校验的 MVP 对象图，`Create(configuration)` 接收显式配置，带适配器的重载接收对话生成器与存储端口，`CreateEmpty()` 保留空配置测试入口。入口都返回类型化的 `CozyTownServices`；该服务集合只在组合边界使用，不向通用 `MonoBehaviour` 或交互上下文公开。

正式场景由 `CozyTownMvpContentAsset.Load()` 把唯一的作者资产转换为 `CozyTownConfiguration`。Runtime 的 `MvpContentValidator` 统一校验经济、生产、全局对话回退和四名 NPC 的稳定 ID、显示名称、人设及专属回退；失败时 Bootstrap 不创建 NPC Catalog、固定回退生成器或 AI 适配器。通过校验后，各组合边界只把经同一工厂校验的不可变 `NpcContentCatalog` 交给消费者；对话协调器与固定回退生成器只依赖 Catalog 的查询和投影方法，不接收可变的原始 NPC 集合。Bootstrap 与 Runtime 组合根可以各自从同一不可变配置创建 Catalog，不承诺跨边界实例复用。

当前 `CozyTownBootstrap` 的职责限定为：

1. 调用组合根创建一次对象图；
2. 私有持有对象图，并将 HUD、商店、农田、床、鸡舍、池塘、厨房、NPC 和存档所需的窄入口推送给对应 Presenter；
3. 支持场景序列化注册和初始化后的显式晚注册；
4. 不把 `CozyTownServices`、背包或原始生产服务交给场景 Presenter。

测试可以直接构造单个模块，也可以调用组合根验证默认对象图。批处理运行向组合根注入内存存档；常规 Editor Play 和构建注入应用持久化目录下的文件存储。AI 代理配置先读取 `COZYTOWN_AI_PROXY_ENDPOINT` 和 `COZYTOWN_AI_PROXY_TIMEOUT_SECONDS` 进程环境变量，未设置时使用场景序列化默认值；最终端点为空时注入固定回退，配置绝对 HTTP(S) 端点时才创建代理客户端。

## 7. 关键数据流

### 7.1 商店购买

```text
Shop UI
  → ICharacterShopTradingCoordinator.Buy(商店 ID, 角色 ID, 商品, 数量)
  → 读取角色背包/钱包与商店库存/钱包快照
  → 校验报价、现货、资金和背包接收条件
  → 构造角色与商店两份完整候选状态
  → IEconomyStateStore.Commit 同时发布双方候选
  → 返回成功或稳定失败原因
  → UI 按相同稳定 ID 重新读取库存感知投影
```

任一前置校验或提交失败时，角色与商店的物品总量和金币总量均保持调用前状态。Unity Presenter 只持有交易用例与稳定 ID，不持有权威经济仓库。

### 7.2 烹饪

```text
Cooking UI
  → ICookingGameplayCoordinator.Cook(配方)
  → ICookingService.Cook(配方)
  → 读取单一配方定义
  → 校验全部食材并捕获背包快照
  → 消耗食材并尝试添加料理
  → 任一背包写入失败时恢复快照
  → 返回成功或稳定失败原因
```

烹饪失败不得部分消耗食材。

### 7.3 跨日结算

```text
Sleep interaction
  → IDayTransitionCoordinator.SleepToNextDay()
  → 捕获 Time / Farming / Livestock / Shop 快照
  → 为唯一目标 Day 生成确定性商店库存候选
  → 时间、农田和畜牧按同一个 Day 值结算一次
  → IEconomyStateStore.CommitShop 发布目标日库存
  → 任一步失败时恢复调用前状态
```

同一天的重复通知必须可检测或无副作用，防止重复成长和重复产出。

### 7.4 NPC 对话

```text
NPC interaction
  → 从确定性模块复制最小只读快照
  → INpcDialogueCoordinator.GenerateAsync(npcId)
  → INpcDialogueGenerator.GenerateAsync(context)
  → 解析并校验文本与允许标签
       ├─ 有效：返回对话候选
       └─ 超时/异常/无效：返回固定对话
  → UI 显示结果
```

该路径没有通往钱包、背包、时间、生产或存档写接口的调用边。模型服务密钥由 Unity 客户端之外的代理服务保存；客户端只调用受控端点。

### 7.5 保存与读取

```text
Save use case
  → IGameSaveCoordinator 从持续性模块导出 DTO
  → 组装带 SchemaVersion 的 GameSaveSnapshot
  → JsonFileSaveStorage 在同目录写入并复读验证临时文件
  → 原子替换 main 槽位；失败时保留旧文件

Load use case
  → ISaveStorage 区分空槽、损坏、版本和载荷错误
  → v1 先确定迁移为 v2，随后校验主体、资产及跨模块日期
  → 恢复 WorldSeed / Time / EconomyState / Farm / Livestock
  → 任一步失败时恢复五份调用前快照
```

当前写入 schema v2，并通过固定迁移器读取 schema v1；未知未来版本或损坏载荷不得覆盖原文件。通用规则见 [`ADR-0003`](adr/0003-save-versioning.md)，经济状态字段及迁移规则见 [`ADR-0012`](adr/0012-economic-save-schema-v2-and-v1-migration.md)。

## 8. 数据与配置约定

- `ItemId`、`CropId`、`FishId`、`RecipeId` 和 `NpcId` 在发布后保持稳定。
- 显示文本、本地化键和资源路径可以变化，不作为状态身份。
- 数量、金币、天数和成长进度在模块入口校验非负范围和上限。
- 商品目录、配方、作物和鱼池规则由单一数据源定义；UI 只读取定义。
- 运行时状态与静态定义分离。存档只记录恢复状态所需的数据，不复制可由定义表重建的显示信息。
- 对外结果使用明确的成功状态与失败原因，不以异常表示余额不足、食材不足等预期业务结果。

## 9. 测试策略

### 9.1 EditMode 组件测试

默认测试针对普通 C# 模块：

- `Time`：天数初值、单调推进和非法初值；
- `Inventory`：增加、移除、数量不足和非法数量；
- `Economy`：购买、出售、余额不足、库存不足和交易原子性；
- `Farming`：播种、浇水、成长、成熟与重复结算；
- `Livestock`：喂食、跨日产出和重复领取；
- `Fishing`：固定结果、成功/失败和背包接收失败；
- `Cooking`：配方成功、食材不足和失败不部分扣除；
- `Npc`：固定文本、允许标签和故障回退；
- `Save`：单槽位读写、空槽、往返一致和版本拒绝；
- `Application`：跨日回滚、四组只读玩法投影和默认生产经济闭环；
- `Content`：默认数量、稳定 ID、引用可达性和配置数组隔离；
- `Core`：默认组合根返回完整且一致的对象图；
- `Unity`：移动、HUD、模态输入互斥、七点场景装配、Presenter 生命周期和正式经济闭环。

钓鱼测试直接传入固定 `roll`；文件系统和 AI 服务通过接口或固定替身隔离。默认测试不访问网络，也不依赖调用计费模型。

2026-08-29 的 Unity `6000.5.5f1` M4 批处理运行发现并执行 151 个 EditMode 用例和 26 个 PlayMode 用例，结果分别为 151 passed 与 26 passed，均为 0 failed、0 skipped。新增覆盖 JSON 往返与损坏保护、五模块保存恢复与回滚、AI 超时/异常/无效候选回退、恶意状态指令隔离、对话异步生命周期，以及正式场景中的 4 名 NPC 和存档面板装配。

### 9.2 测试层

| 层级 | 触发条件 | 覆盖内容 |
| --- | --- | --- |
| EditMode 组件测试 | 每次业务规则变更 | 模块不变量、边界、原子操作和存档迁移 |
| EditMode 协作测试 | 两个以上模块形成用例后 | 购买、烹饪、跨日结算和保存快照 |
| PlayMode 测试 | 场景、物理或生命周期变更 | 移动、碰撞、交互触发、场景绑定和 HUD 装配 |
| AI 离线评测 | 提示词、模型或解析器变更 | 结构、人设、世界状态矛盾和越权请求 |
| 人工演示检查 | 发布作品版本前 | 完整经济闭环、回退体验、存档恢复和录屏脚本 |

测试的具体用例、覆盖矩阵和退出条件见 [`TEST_PLAN.md`](TEST_PLAN.md) 与 [`ADR-0004`](adr/0004-testing-strategy.md)。

## 10. 错误处理与可观察性

- 预期业务失败返回可枚举或稳定字符串原因，供 UI 映射为提示文本。
- 未预期的存档异常映射为稳定错误码，AI Presenter 显示不可用状态；统一诊断记录器属于 M5。
- M4 的 AI 生成与 AI 回退结果携带关联 ID、是否回退和回退原因，调试面板显示回退原因；固定离线对话不生成关联 ID。提供商、模型、延迟分位数、Token、成本和重试汇总在 M5 接入真实服务后补齐。
- 存档失败保留原有载荷，并向调用方返回失败结果；UI 决定如何提示和重试。
- 当前开发场景使用 Production UGUI；M5 再定义发布构建的诊断开关和隐藏策略。

## 11. A1 表现层接线边界

A1 使用 [`ADR-0006`](adr/0006-production-art-scene-integration.md) 约束正式场景接线。地图由 Tilemap 消费 Production Tile；建筑、功能物件、玩家、世界 NPC、农田状态和母鸡状态由 SpriteRenderer 表现。Pixel Perfect Camera 使用与资源一致的 `16 PPU` 和 `320×180` 参考分辨率。

表现组件只读取现有移动方向、速度或应用层只读 ViewState。它们不能取得钱包、背包、时间、农田、畜牧、存档或 AI 的写接口。碰撞体和 `TownInteractionPoint2D` 保留在原有逻辑对象上，视觉子对象可以替换或重排，但不能改变 7 个交互种类和提示生命周期。

Scene-01 分为世界静态表现、运行时状态表现、UI 皮肤、常驻 UI 信息架构、场景语义边界、人工问题收口、屋后遮挡、门槽深度修正和主角图集规格化九个垂直切片。Scene-01a 至 Scene-01i 自动化均已完成；正式界面使用 UGUI 消费 Production 九宫格、按钮状态和图标，常驻区域收敛为左上 HUD、目标 `E` 气泡、五格快捷栏与无底框灰色齿轮。七个业务模态在 Canvas sibling 顺序上位于快捷栏之后；按钮主题只作用于这些模态，快捷栏数字使用独立深色文本。六个独立实体障碍位于交互点父链之外；房屋实体只覆盖贴图下方约 3/5，四个门槽深度统一为 0.6 世界单位。每座房屋由排序值 5 的完整底图和排序值 30 的透明屋顶前景组成；玩家与 NPC 分别位于排序值 20 和 15，因此可在背面通行区被屋顶遮挡。四名 NPC 使用各自的稳定 ID、`24×32` 世界 Sprite 和 Presenter，共享同一个只显示当前 NPC 的对话 View；母鸡表现节点位于农田旁草地。

主角图集的 12 个单元由 `ArtSource/Authored/A1/Characters/Player/*.pixels` 精确覆盖。A1 编译器校验每个单元为 `24×32`、二值 Alpha 和 `WarmRural32` 色板成员后直接写入既有 `chr_player_move_24x32.png`；Unity 导入仍使用 `16 PPU`、BottomCenter Pivot、原 Sprite 名称与原 3×4 顺序，`CozyTownPlayerSpriteAnimator` 的四方向 idle/walk 映射不变。包络测试约束脚底、顶部、水平范围、中心、接地四邻域连通、帧高差、整体镜像轮廓和底部 8 行镜像轮廓，避免逐帧自动裁边缩放造成体型跳变或脚部缺失。

Scene-01 仍需人工确认实际画面的可读性、角色动作、屋后遮挡、门口手感、岸线贴合、接缝、脚底稳定和整体构图。真实 AI 驱动 NPC、模型评测和联网诊断在人工验收后进入 M5。

## 12. 后续实现顺序

1. 已完成：固定模块接口、内存实现和 EditMode 测试。
2. 已完成：增加跨日用例协调器、默认稳定 ID 内容和启动前校验。
3. 已完成：接入 Unity Bootstrap、输入/刚体移动、交互探测契约和调试 HUD 骨架。
4. 已完成：生成单一小镇场景，加入可见玩家、碰撞边界、交互提示、商店/NPC/床/农田浅交互点和 PlayMode 冒烟测试。
5. 已完成：商店交易门面、购买/出售调试 UI 和统一输入门控。
6. 已完成：接入种植、畜牧、钓鱼、跨日和烹饪，并在正式场景完成成功出售与再次投入。
7. 已完成：实现 schema v2 单槽 JSON 文件存档、v1 确定迁移、损坏保护、五模块恢复和失败回滚。
8. 已完成：接入 AI HTTP(S) 代理适配器、结构校验、超时、固定回退和 4 名 NPC 场景切片。
9. 待人工执行：A1 Scene-01 自动接线与全量回归已完成；按验收脚本检查实际画面，不改变既有玩法和 AI 权限边界。
10. Scene-01 通过后：运行不少于 30 条 AI 离线评测，补充延迟与成本诊断，执行 Windows 构建、性能检查和演示录制。

每一步只扩展已定义接口所需的行为；如果接口不能表达已确认用例，先补充失败用例测试和 ADR，再调整公共契约。
