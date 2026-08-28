# CozyTown 架构说明

## 1. 范围与当前阶段

本架构采用 Unity 项目内的模块化单体。目标骨架由模块契约、确定性内存实现、显式组合根和 EditMode 组件测试组成；Unity 场景、表现层、持久化文件适配器和线上 AI 适配器在后续阶段接入。仓库当前状态和验证阻塞见 [`README.md`](../README.md)。

产品边界见 [`PRD.md`](PRD.md)。关键决策见：

- [`ADR-0001：模块化单体`](adr/0001-modular-monolith.md)
- [`ADR-0002：确定性领域与 AI 边界`](adr/0002-deterministic-domain-and-ai-boundary.md)
- [`ADR-0003：存档版本化`](adr/0003-save-versioning.md)
- [`ADR-0004：测试策略`](adr/0004-testing-strategy.md)

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
└─ Tests/
   └─ EditMode/
```

`CozyTown.Runtime` 是当前运行时程序集，并设置 `noEngineReferences: true`。目录和命名空间提供模块边界；当出现可证明的编译隔离、平台适配或依赖环问题时，再评估拆分运行时程序集。测试程序集为 `CozyTown.Tests.EditMode`，仅引用运行时程序集和 Unity Test Framework。

## 4. 模块职责与接口

| 模块 | 公共入口 | 职责 | 不负责 |
| --- | --- | --- | --- |
| `Core` | `CozyTownCompositionRoot`、`CozyTownServices` | 创建默认实现并公开类型化服务引用 | 业务规则、存档格式、场景查找 |
| `Time` | `ITimeService` | 当前天数和跨日推进 | 决定作物、动物的具体结算规则 |
| `Inventory` | `IInventory` | 物品数量查询、增加、移除和前置校验 | 价格、配方、掉落概率 |
| `Economy` | `IWallet`、`IShopService` | 余额、报价、购买和出售的交易边界 | 物品生产、AI 决策 |
| `Farming` | `IFarmService` | 地块、播种、浇水、成长和收获状态 | 玩家移动、商店交易 |
| `Livestock` | `ILivestockService` | 鸡的喂食与鸡蛋产出状态 | 饲料定价、NPC 行为 |
| `Fishing` | `IFishingService` | 固定鱼池规则和钓鱼结果 | 实时操作 UI、背包显示 |
| `Cooking` | `ICookingService` | 配方查询、食材校验和烹饪事务 | 食材生产、料理表现 |
| `Npc` | `INpcDialogueGenerator` | 根据只读上下文返回对话候选或固定回退 | 写入金币、物品、时间、生产或存档状态 |
| `Save` | `ISaveStorage` | 版本化存档快照的读写边界；MVP 调用方使用一个固定槽位 ID | 收集各模块状态、业务迁移决策 |

接口输入和输出使用模块自己的 DTO 或值对象。公开集合应以只读视图或副本返回，调用方不能通过集合引用绕过模块规则。

## 5. 依赖方向

```text
Unity 场景与 UI（后续）
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
2. 具体实现可以依赖完成其事务所需的窄接口。例如商店可以依赖 `IWallet` 与 `IInventory`，但不能访问其内部集合。
3. 模块不能通过 `FindObjectOfType`、静态单例或字符串路径取得其他服务。
4. 双向依赖通过用例协调器、只读快照或领域事件解除；不得让两个模块互相持有具体实现。
5. `Npc` 生成器没有确定性模块的写依赖。调用方只向它传递复制出的上下文数据。
6. `Save` 适配器只读写版本化载荷。存档协调器负责向各模块导出和恢复状态。

## 6. 组合根

`Runtime/Core/CozyTownCompositionRoot.cs` 是默认对象图的唯一构造入口。`Create(configuration)` 根据 `CozyTownConfiguration` 创建对象图，`CreateEmpty()` 创建空配置骨架；两者都返回通过显式类型化属性公开服务的 `CozyTownServices`。调用代码不得通过 `Get<T>()` 或全局容器解析服务。

后续场景入口的职责限定为：

1. 调用组合根创建一次对象图；
2. 将所需接口注入控制器或 presenter；
3. 订阅状态变化并更新 Unity 视图；
4. 在退出或保存点调用存档用例。

测试可以直接构造单个模块，也可以调用组合根验证默认对象图。外部 AI 和文件系统使用不同的适配器构造方法，不改变领域接口。

## 7. 关键数据流

### 7.1 商店购买

```text
Shop UI
  → IShopService.Buy(商品, 数量)
  → 校验商品、数量、余额和背包接收条件
  → 同一事务中扣除金币并增加物品
  → 返回成功或稳定失败原因
  → UI 刷新只读余额与物品数量
```

任一前置校验失败时，钱包和背包均保持调用前状态。

### 7.2 烹饪

```text
Cooking UI
  → ICookingService.Cook(配方)
  → 读取单一配方定义
  → 校验全部食材和成品接收条件
  → 一次性消耗食材并添加料理
  → 返回结果
```

烹饪失败不得部分消耗食材。

### 7.3 跨日结算

```text
Sleep interaction
  → ITimeService.SleepToNextDay()
  → 产生新的 Day 值
  → 用例协调器依次通知 Farming 与 Livestock
  → 各模块按同一个 Day 值结算一次
  → 保存点按需要创建快照
```

同一天的重复通知必须可检测或无副作用，防止重复成长和重复产出。

### 7.4 NPC 对话

```text
NPC interaction
  → 从确定性模块复制最小只读快照
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
  → 从持续性模块导出 DTO
  → 组装带 SchemaVersion 的 GameSaveSnapshot
  → ISaveStorage 写入单槽位

Load use case
  → ISaveStorage 读取载荷
  → 校验版本并按需要迁移
  → 校验稳定 ID 与数值范围
  → 恢复各模块
```

未来版本或损坏载荷不得在未确认的情况下覆盖原文件。详细规则见 [`ADR-0003`](adr/0003-save-versioning.md)。

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
- `Core`：默认组合根返回完整且一致的对象图。

钓鱼测试直接传入固定 `roll`；文件系统和 AI 服务通过接口或固定替身隔离。默认测试不访问网络，也不依赖调用计费模型。

### 9.2 后续测试层

| 层级 | 触发条件 | 覆盖内容 |
| --- | --- | --- |
| EditMode 组件测试 | 每次业务规则变更 | 模块不变量、边界、原子操作和存档迁移 |
| EditMode 协作测试 | 两个以上模块形成用例后 | 购买、烹饪、跨日结算和保存快照 |
| PlayMode 测试 | 场景与控制器接入后 | 交互触发、场景绑定、UI 刷新和场景切换 |
| AI 离线评测 | 提示词、模型或解析器变更 | 结构、人设、世界状态矛盾和越权请求 |
| 人工演示检查 | 发布作品版本前 | 完整经济闭环、回退体验、存档恢复和录屏脚本 |

测试的具体用例、覆盖矩阵和退出条件见 [`TEST_PLAN.md`](TEST_PLAN.md) 与 [`ADR-0004`](adr/0004-testing-strategy.md)。

## 10. 错误处理与可观察性

- 预期业务失败返回可枚举或稳定字符串原因，供 UI 映射为提示文本。
- 未预期异常在系统边界记录模块、操作和关联 ID，不记录模型密钥。
- 线上 AI 适配器接入后，调用日志记录提供商、模型、延迟、Token 计量、重试次数、结构校验结果和回退原因。
- 存档失败保留原有载荷，并向调用方返回失败结果；UI 决定如何提示和重试。
- 开发构建可以显示 AI 诊断信息，发布构建默认隐藏诊断界面。

## 11. 后续实现顺序

1. 固定模块接口、内存实现和 EditMode 测试。
2. 增加跨模块用例协调器，验证交易、烹饪和跨日结算原子性。
3. 定义 ScriptableObject 或等价静态数据资产，并锁定稳定 ID。
4. 接入单一小镇场景、角色移动、交互检测和 UI presenter。
5. 实现版本化本地文件存档适配器和迁移测试。
6. 接入 AI 代理适配器、结构校验、超时与固定回退。
7. 运行可玩验收、AI 离线评测和演示录制。

每一步只扩展已定义接口所需的行为；如果接口不能表达已确认用例，先补充失败用例测试和 ADR，再调整公共契约。
