# 经济交易状态的持久化与数据库引入阈值

## 范围与假设

本报告评估以下场景的本地持久化方案：单机 Unity 游戏、单槽存档、少量物品定义、一个或少量商店，以及后续增加多个拥有背包和资金的角色。运行时状态由单个游戏进程负责写入，不要求多设备同步、网络共享数据库或多个进程同时写入。

调研日期为 2026-09-01。外部依据限于 Unity、SQLite 和 Microsoft 的官方资料；项目现状依据仓库代码和包清单。

## 结论

当前阶段不引入 SQLite。保留“内存领域聚合 + 版本化 JSON 快照 + `Application.persistentDataPath` 文件存储”，并把静态物品与每日补货规则放在只读内容定义中。角色背包、角色资金、商店库存、商店资金和当日刷新状态属于动态存档，不能把 ScriptableObject 资产当作它们的发布版存储。

多个角色或动态商店不会单独构成数据库需求。只有出现可测量的全量快照性能问题、必须增量持久化每笔交易、需要对大量历史或实体做关系查询，或者存档状态需要被多个进程访问时，才重新评估 SQLite。若状态位于网络另一端或需要多个并发写入者，应评估服务端数据库，而不是把同一 SQLite 文件放到网络共享目录。

## 一手资料事实

### Unity 内容定义与运行时存档不是同一种数据

Unity 6.0 将 ScriptableObject 定义为独立于 GameObject 的项目资产和共享数据容器。独立 Player 在运行时只能读取构建中已有的 ScriptableObject 资产，不能依赖它保存运行时变化。因此，ScriptableObject 适合物品目录、基础买卖价格、每日刷新权重和商店配置，不适合角色背包、商店实时库存或资金余额。[Unity：ScriptableObject](https://docs.unity3d.com/6000.0/Documentation/Manual/class-ScriptableObject.html)

Unity 的序列化最佳实践要求缩小被序列化的数据集、避免重复或缓存数据，并保持数据结构简单，从而降低版本兼容和迁移风险。这支持“存稳定 ID 与动态数量，不把物品静态定义和 UI 投影复制进存档”的方案。[Unity：Serialization best practices](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization-best-practices.html)

Unity JSON 是结构化序列化：由明确的类或结构描述字段。若使用 `JsonUtility`，它遵循 Unity 字段序列化规则，不支持 `Dictionary<,>` 等类型，数组或基本类型也需要外层对象；需要更复杂 JSON 结构时，Unity 文档允许配合通用 .NET JSON 库。[Unity：JSON Serialization](https://docs.unity3d.com/6000.0/Documentation/Manual/json-serialization.html) [Unity：Serialization rules](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization-rules.html)

`Application.persistentDataPath` 是 Unity 提供的跨运行持久数据目录。只要后续版本保持相同 Bundle Identifier，应用更新仍访问相同位置；不同平台的实际目录不同，因此业务层不应硬编码平台路径。[Unity：Application.persistentDataPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-persistentDataPath.html)

### SQLite 能解决什么

SQLite 是嵌入应用的零配置数据库，完整数据库可以存放在单个跨平台文件中，并提供 SQL、索引和 ACID 事务。[SQLite：Features](https://www.sqlite.org/features.html) SQLite 官方说明，同一事务内的更改在程序、操作系统或电源中断时仍表现为全部完成或全部不完成。[SQLite：Transactional](https://www.sqlite.org/transactional.html)

SQLite 适合设备本地、低写并发的数据和应用文件格式；当应用需要从大量数据中执行多种筛选、排序和连接时，SQL 与索引可以替代手工维护查询结构。[SQLite：Appropriate Uses](https://www.sqlite.org/whentouse.html) SQLite 同一数据库文件同一时刻只有一个写入者。若数据库通过网络由多个客户端直接访问，或者要求多个无法排队的并发写入者，SQLite 官方建议改用客户端/服务器数据库。[SQLite：Appropriate Uses](https://www.sqlite.org/whentouse.html) [SQLite：File Locking and Concurrency](https://www.sqlite.org/lockingv3.html)

## 项目现状

- `CozyTown.Runtime` 不依赖 `UnityEngine`，领域对象以接口和快照工作；该边界适合继续隔离具体存储实现。
- [`GameSaveSnapshot`](../../../Assets/CozyTown/Runtime/Save/GameSaveSnapshot.cs) 当前是 schema version 1，只包含一个 Inventory 和一个 Wallet，尚未表达角色集合、商店库存、商店资金或刷新状态。
- [`GameSaveCoordinator`](../../../Assets/CozyTown/Runtime/Application/GameSaveCoordinator.cs) 先捕获完整内存快照，再交给 `ISaveStorage`；加载失败时尝试恢复加载前状态。
- [`JsonFileSaveStorage`](../../../Assets/CozyTown/Runtime/Save/JsonFileSaveStorage.cs) 将临时文件完整写盘、读回并验证，随后替换主文件；当前只接受 `main` 槽位并拒绝不受支持的 schema version。
- [`CozyTownBootstrap`](../../../Assets/CozyTown/Unity/Core/CozyTownBootstrap.cs) 已把存档位置组合到 `Application.persistentDataPath`。
- [`Packages/manifest.json`](../../../Packages/manifest.json) 当前没有 SQLite 提供程序或原生库依赖。引入 SQLite 将同时增加依赖选择、目标平台原生二进制、构建和迁移验证工作。

以上现状意味着下一步的最小改动是扩充领域快照和版本迁移，而不是替换 `ISaveStorage`。

## 推荐的数据分层

| 数据 | 权威位置 | 是否进入存档 | 原因 |
| --- | --- | --- | --- |
| `ItemDefinition`：稳定 `itemId`、名称、类别、基础买价/卖价 | Runtime 代码配置，或 Unity 层 ScriptableObject 资产再转换为 Runtime 定义 | 否 | 构建内容；多个持有者按稳定 ID 引用，避免复制静态数据 |
| `DailyStockRule`：可出现物品、权重、最小/最大数量、刷新规则版本 | 同上 | 只存规则版本或内容版本 | 规则是静态定义；存版本用于兼容旧存档 |
| `CharacterState`：`characterId`、背包、资金 | 内存 Character 聚合 | 是 | 每个角色独立拥有并转移资源；玩家和未来 NPC 使用同一形状 |
| `ShopState`：`shopId`、库存、资金 | 内存 Shop 聚合 | 是 | 玩家买卖会持续改变数量和资金 |
| `ShopRefreshState`：最后刷新日、随机种子或已落地刷新批次、规则版本 | 内存 Shop 聚合 | 是 | 支持同一天加载后复现库存，并区分刷新注入与交易转移 |
| UI 商品列表、合计价格、可买/可卖标志 | 查询投影 | 否 | 可从聚合与静态定义重建，存储会产生重复状态 |
| 完整交易历史 | 暂不持久化 | 否 | 当前守恒可由命令与不变量测试验证；没有查询或审计需求时不增加无界日志 |

静态定义可以先继续使用 Runtime 代码配置，以保持当前 `CozyTown.Runtime` 的纯 .NET 边界。如果内容维护量增加，再在 Unity 层增加 ScriptableObject 作者资产，并在启动时校验和转换为 Runtime 定义。动态状态仍只进入存档快照。

## 推荐的 JSON 快照边界

下一版快照应以稳定 ID 表达所有权，不保存对象引用：

```text
GameSaveSnapshot
├─ schemaVersion
├─ clock
├─ characters[]
│  ├─ characterId
│  ├─ walletBalance
│  └─ inventory[] { itemId, quantity }
├─ shops[]
│  ├─ shopId
│  ├─ walletBalance
│  ├─ inventory[] { itemId, quantity }
│  └─ refresh { lastDay, seedOrBatch, ruleVersion }
└─ existing farm/livestock state
```

保存应发生在完成一笔领域交易之后，不能在扣除一方资源与增加另一方资源之间捕获快照。购买、出售和每日刷新分别通过领域命令修改内存聚合；存储层只保存已经满足不变量的完整快照。这样，“交易原子性”由领域应用服务保证，“跨运行持久化”由存储适配器保证，两者不依赖同一个具体数据库。

版本化 JSON 需要显式迁移链，而不是仅比较当前版本：先解析最外层 `schemaVersion`，按 `v1 -> v2 -> ... -> current` 迁移数据传输对象，再执行领域校验并恢复聚合。未知的未来版本仍应拒绝加载。迁移测试至少覆盖上一发布版本、当前版本、缺字段、重复 ID、负数量、非法物品 ID 和刷新日早于/晚于世界日等情况。

当前临时文件写入、强制刷新、读回验证和替换主文件的流程可以保留。Microsoft 对 `File.Replace` 的契约是用源文件替换目标文件，并允许不创建备份；该 API 文档没有承诺所有 Unity 目标平台上的数据库级事务语义，因此仍需通过目标平台故障测试验证断电或崩溃恢复要求。[Microsoft：File.Replace](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace)

## 方案比较

| 方案 | 当前适配度 | 优点 | 约束 |
| --- | --- | --- | --- |
| 内存聚合 + 版本化 JSON 快照 | 高 | 与现有 `ISaveStorage`、单槽和完整快照一致；文本可检查；无需新原生依赖 | 保存时重写完整快照；需要维护 DTO 迁移与完整性校验 |
| ScriptableObject 静态定义 + JSON 动态快照 | 高，作为内容作者层可选 | 共享静态内容、Inspector 可编辑；运行态仍保持领域纯净 | 独立 Player 不能把运行时变更持久回资产；需要 Unity 层转换与定义校验 |
| 纯代码静态定义 + JSON 动态快照 | 当前最高 | 不新增 Unity 依赖，适合物品量少且定义稳定 | 内容编辑体验随物品数量增长会变差 |
| SQLite + 内存领域聚合 | 当前低 | 原子增量写入、索引、关系查询、长历史查询和单文件格式 | 新增提供程序与原生平台集成；需要 SQL schema 迁移、连接生命周期和构建矩阵；仍不能替代领域不变量 |

## 数据库引入阈值

不要按“物品数达到某个拍脑袋数量”切换存储。先建立可重复的目标设备基准；满足下列任一可观察条件，再设计 SQLite 原型并与 JSON 基线比较：

1. **全量快照超过已定义的帧预算或保存延迟预算。** Profiler 和磁盘测量证明，序列化或整文件重写是主要瓶颈，而不是 UI、日志或领域计算；增量写入能够解决该瓶颈。
2. **每笔交易必须立即耐受进程或系统崩溃。** 产品要求不允许回退到最近一次显式/自动保存，而每笔交易后重写并验证完整 JSON 的成本已被测量为不可接受。SQLite 的单事务 ACID 提供直接对应的持久化语义。
3. **出现大量且无界的交易、生产或刷新历史，并需要组合查询。** 例如按角色、物品、商店、时间区间筛选和聚合，现有做法需要反复加载整份历史或维护多份易失同步的索引。
4. **运行时状态不能合理整体装入内存。** 系统需要按实体或区域分页加载、局部更新和索引查询；这与当前“小镇、少量物品、少量角色”的约束不同。
5. **多个工具或进程必须安全地读取同一存档，同时有单一可排队写入者。** SQLite 可以协调本机文件访问；仍需验证 Unity 目标平台和所选提供程序。
6. **关系完整性和迁移成本已有实证收益。** 多个商店、角色、订单或历史表之间的查询和约束已经使手工 DTO 迁移与索引维护明显复杂，并且 SQL schema 原型减少了已测量的实现或缺陷成本。

下列情况不足以引入数据库：新增几个 NPC 背包、商店每日随机库存、鱼或农产品进入贸易、多个存档字段、需要保存商店资金，或希望系统“看起来成熟”。这些状态都能由稳定 ID、内存聚合、一次交易命令和版本化快照直接表达。

如果未来要求云同步、跨设备共享、多人交易或多个并发写入者，SQLite 不是目标架构；应把权威经济状态放在服务端，并使用适合并发写入的服务端数据库。SQLite 可以继续作为本地缓存，但不能把数据库文件直接放到网络共享路径供多个客户端写入。

## 下一阶段决策建议

1. 保留 `ISaveStorage`，将存储格式继续隐藏在适配器之后。
2. 先决定 `Character`、`Shop` 和 `DailyStockRefresh` 的聚合边界，再将它们的快照加入新的 schema version。
3. 为 schema version 1 到新版本定义迁移策略；旧的单一 Inventory/Wallet 映射为 `player` 角色状态。
4. 对每笔买卖执行“角色与商店的物品和货币同时转移”，成功后才允许保存。
5. 每日刷新显式记录资源注入/移除边界；保存最后刷新日以及可复现刷新所需的种子/批次和规则版本。
6. 给 JSON 建立保存大小、保存时长和加载时长基准。只有触发上述阈值后，才创建 SQLite 垂直原型；原型必须通过相同领域契约、迁移测试和目标平台构建测试。

## 资料来源

- [Unity 6.0 Manual: ScriptableObject](https://docs.unity3d.com/6000.0/Documentation/Manual/class-ScriptableObject.html)
- [Unity 6.0 Manual: JSON Serialization](https://docs.unity3d.com/6000.0/Documentation/Manual/json-serialization.html)
- [Unity 6.0 Manual: Serialization rules](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization-rules.html)
- [Unity 6.0 Manual: Serialization best practices](https://docs.unity3d.com/6000.0/Documentation/Manual/script-serialization-best-practices.html)
- [Unity 6.0 Scripting API: Application.persistentDataPath](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Application-persistentDataPath.html)
- [SQLite: Features](https://www.sqlite.org/features.html)
- [SQLite: Appropriate Uses For SQLite](https://www.sqlite.org/whentouse.html)
- [SQLite: SQLite Is Transactional](https://www.sqlite.org/transactional.html)
- [SQLite: File Locking And Concurrency In SQLite Version 3](https://www.sqlite.org/lockingv3.html)
- [Microsoft Learn: File.Replace Method](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace)
