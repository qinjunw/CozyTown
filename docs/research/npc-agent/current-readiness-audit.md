# NPC Agent 接入准备度审计

- 审计日期：2026-09-05。
- 代码基线：`7bfe65ed1871319f3f091d8522f82b2f0af52f24`，提交时间 2026-09-02 08:18:35 +08:00。
- 集成状态：总控核查 [Implement conserved economy and validated MVP content](https://github.com/qinjunw/CozyTown/pull/12) 仍为 OPEN；本文审计当前工作分支代码，不把它描述为已合入默认分支。
- 对应研究票：[Audit NPC agent goals, implementation, and acceptance gaps](https://github.com/qinjunw/CozyTown/issues/24)。
- 首版范围：用户已确认“结合人设、游戏状态和对话记忆回答，并使用只读查询工具”；自主日程和游戏状态写入属于后续范围。
- 方法：只读核查仓库源码、需求与 ADR、现有测试源码和历史 XML/日志。本次没有运行 Unity、调用模型或执行新的游戏测试。以下路径均相对仓库根目录。

## 1. 原始目标与当前结论

原始产品是面向简历展示的单镇生活模拟：种植、养鸡、钓鱼、烹饪与交易组成可演示闭环，AI 应用能力通过受约束 NPC 对话、失败回退、确定性状态隔离和评测证据展示。PRD 明确面试官是项目读者，AI 不替代生产或交易规则；自主 NPC 日程、AI 生成价格/掉落/任务不在原 MVP 范围。证据：`docs/PRD.md:17`、`:23`、`:49`、`:83`。

当前具备可复用的游戏核心、四个 NPC 世界入口和客户端 AI 适配边界，但尚未形成用户确认的“有记忆、会查询游戏状态”的 Agent。已有实现是根据身份、人设与当前时间发起一次请求，展示通过校验的文本与表现标签。`memories` 等字段虽然已定义，正式调用路径仍填空数组。证据：`Assets/CozyTown/Runtime/Application/NpcDialogueCoordinator.cs:35`、`Assets/CozyTown/Runtime/Npc/NpcContentCatalog.cs:67`。

“M4 已完成”表示存档与受约束 AI 对话适配已完成，不等于真实模型服务或 Agent 已验收。计划仍将真实服务、30 条评测、延迟/成本诊断和作品集构建放在 M5；Scene-01 人工验收仍标为待执行。证据：`docs/DELIVERY_PLAN.md:19`、`:20`、`:85`、`:101`。

## 2. 已有能力与真实边界

| 能力 | 已实现内容 | 证据与限制 |
| --- | --- | --- |
| 人设与固定回退 | 默认资产配置 Mina、Eli、Ren、Sora 的身份、人设和专属回退；加载经过统一内容校验 | `Assets/CozyTown/Content/DefaultMvpContent.asset:21`；`Assets/CozyTown/Unity/Content/CozyTownMvpContentAsset.cs:38`。人设目前是短文本，不含版本化角色知识或对话策略 |
| 深模块入口 | `NpcContentCatalog` 隐藏作者内容校验、索引、上下文构造和回退查询；协调器依赖 Catalog 与生成端口 | `Assets/CozyTown/Runtime/Npc/NpcContentCatalog.cs:28`、`:62`、`:88`；`Assets/CozyTown/Runtime/Application/NpcDialogueCoordinator.cs:17` |
| 世界上下文 | 正式生成路径读取当前天数、分钟，复制身份与人设 | `Assets/CozyTown/Runtime/Application/NpcDialogueCoordinator.cs:44`。`affinity=0`，`recentActivities=[]`，`memories=[]` 固定于 `Assets/CozyTown/Runtime/Npc/NpcContentCatalog.cs:83`，尚未投影背包、商店、地块或鸡的状态 |
| DTO 隔离 | Context 对数组输入和输出复制；Request 暴露只读列表 | `Assets/CozyTown/Runtime/Npc/NpcDialogueContext.cs:27`、`:47`；`Assets/CozyTown/Runtime/Npc/NpcDialogueRequest.cs:42`。此处证明数据隔离，不代表已经有记忆存储 |
| 对话 UI | 接近指定 NPC 后自动请求；Talk/Talk again 再次生成；只展示当前 NPC | `Assets/CozyTown/Unity/Npc/CozyTownNpcDebugPresenter.cs:65`、`:98`；`Assets/CozyTown/Unity/Npc/CozyTownNpcDebugView.cs:25`、`:132`。没有玩家文本输入、聊天历史列表或提交用户消息的事件 |
| 客户端协议 | HTTP(S) POST，JSON 文本/情绪/动作候选；模型密钥不由该客户端持有 | `Assets/CozyTown/Unity/Npc/ProxyNpcDialogueClient.cs:28`；`Assets/CozyTown/Unity/Npc/ProxyNpcDialogueJsonCodec.cs:49`。请求没有用户消息、会话 ID、工具定义、工具结果或协议版本；本仓库未找到代理服务实现 |
| 校验与回退 | 500 字符限制、情绪/动作允许列表、超时、传输/提供者/结构故障及当前 NPC 回退 | `Assets/CozyTown/Runtime/Npc/AiNpcDialogueGenerator.cs:10`、`:102`、`:168`、`:196`、`:227`。这是结构及标签校验，没有实现人设一致性、世界矛盾或文本语义判定 |
| 客户端取消 | 关闭/重发取消请求，requestVersion 阻止旧结果覆盖 UI；客户端将取消令牌传入 HTTP 请求 | `Assets/CozyTown/Unity/Npc/CozyTownNpcDebugPresenter.cs:107`、`:159`、`:168`；`Assets/CozyTown/Unity/Npc/ProxyNpcDialogueClient.cs:52`。代理端停止模型/工具执行尚无代码或证据 |
| 诊断 | 返回关联 ID、是否回退与回退原因，UI 显示回退及标签 | `Assets/CozyTown/Runtime/Application/NpcDialogueViewState.cs:53`；`Assets/CozyTown/Unity/Npc/CozyTownNpcDebugView.cs:102`、`:197`。关联 ID 在生成器本地创建，未进入 Request/HTTP 载荷，尚不能串起代理工具轨迹 |
| 确定性状态隔离 | AI 消费 DTO 并返回表现候选，没有游戏写命令路由 | `docs/adr/0002-deterministic-domain-and-ai-boundary.md:31`；`Assets/CozyTown/Runtime/Npc/AiNpcDialogueGenerator.cs:35`。不能将现有“无工具”隔离测试直接作为新增工具注册表的授权证明 |
| 存档 | schema v2 保存世界种子、时间、全部角色与商店经济状态、农田、畜牧 | `Assets/CozyTown/Runtime/Save/GameSaveSnapshot.cs:12`、`:16`；`Assets/CozyTown/Runtime/Application/GameSaveCoordinator.cs:74`。没有对话会话、聊天记忆或对话分支标识 |
| NPC 经济扩展点 | 经济存储按 CharacterId 支持多个角色，交易按角色/商店 ID 操作 | `Assets/CozyTown/Runtime/Economy/IEconomyStateStore.cs:7`；`Assets/CozyTown/Runtime/Application/ICharacterShopTradingCoordinator.cs:7`。默认组合根只创建玩家经济主体，并没有为四个世界 NPC 自动创建背包或钱包：`Assets/CozyTown/Runtime/Core/CozyTownCompositionRoot.cs:61`、`:93` |

默认开发场景的代理地址为空，超时为 8 秒；环境变量可以覆盖场景值，因此“仓库默认离线”不能推断用户某次启动一定离线。证据：`Assets/CozyTown/Scenes/CozyTown_Dev.unity:46378`；`Assets/CozyTown/Unity/Core/CozyTownBootstrap.cs:316`；`README.md:89`。

## 3. 接入前缺口

### 3.1 阻断项

| 缺口 | 阻断哪一步 | 建议最小验收 |
| --- | --- | --- |
| Scene-01 人工结果未登记 | 按当前已接受规则，阻断真实 AI 驱动 NPC 接线、真实模型评测和联网诊断；不阻断本轮研究与固定替身验证 | 按三档目标分辨率检查 UI、移动、屋后遮挡、门口/岸线与闭环，记录版本、日期、结果及遗留问题。现有门禁依据：`docs/PRD.md:210`、`docs/ART_ACCEPTANCE.md:125`、`docs/TEST_PLAN.md:488` |
| 玩家输入与对话轮次不存在 | 无法回应玩家具体问题或展示多轮记忆 | 一个用户消息入口、NPC/玩家身份、会话与轮次标识；空输入/超长输入拒绝，重复提交有确定处理。现有端口仅有 `npcId` 与取消令牌：`Assets/CozyTown/Runtime/Application/INpcDialogueCoordinator.cs:11` |
| 状态读取与工具执行不存在 | 无法回答“我有几条鱼”“店里今天还有盐吗”，更无法证明回答使用了查询工具 | 游戏在一次逻辑时点捕获经过筛选的只读快照；工具注册表仅提供该快照上的有限查询。验证未知工具、未知主体、非法参数和超量查询被拒绝；整轮结束前后权威状态相等 |
| 记忆和会话生命周期未定义 | 无法持续记住玩家说过什么，并可能在重开/读档后混入另一段历史 | 首版可用按 NPC、玩家、游戏会话隔离的有界近期轮次；明确关闭面板是否保留、重启/新游戏/读档如何清空或恢复。验证 NPC 间不串话、旧请求不能追加到新会话；失败/取消不生成伪造成功轮次 |
| 缺少代理宿主与受限工具循环 | 只有 HTTP 客户端，缺少模型调用、工具调度、最终输出收束 | 一个代理进程承接现有边界，在代理内封装模型 SDK 与工具循环；固定工具允许列表、整轮超时和调用上限，模型密钥仅在宿主配置。以假模型完整走“请求→查询→最终回复→客户端校验/回退” |
| 代理任务的取消和失效机制 | 已有 UI 防旧回复，但未来代理可能继续消耗费用或回写对话记忆 | 轮次 ID 贯穿客户端、代理和工具；关闭/重发/读档后使旧轮次失效，代理在下一可取消点停止；验证取消后的结果不显示、不写入会话历史 |
| 缺少真实评测与可核对诊断 | 阻断 PRD 定义的 AI/MVP 作品集交付；不要求先做完整监控平台才能跑通固定替身 | 至少 30 条版本化用例，记录模型/提示词/工具版本、结果及失败样本；记录结构、人设、状态矛盾、越权、回退、P50/P95 和 token/成本。门槛见 `docs/PRD.md:284`、`:296`、`:310` |

只读工具的安全边界需要由代码建立。`IEconomyStateStore` 同时含 `Restore` 和 `Commit`，农田/畜牧协调器同时含查询与生产命令，不能因为只打算调用 Get 方法就把这些接口整体注入 Agent。可由可信应用层使用已有只读 ViewState/Projection 捕获数据，再将复制出的查询数据交给代理。现成投影见 `Assets/CozyTown/Runtime/Inventory/IInventoryProjection.cs:3`、`Assets/CozyTown/Runtime/Application/FarmViewState.cs:65`、`Assets/CozyTown/Runtime/Application/LivestockViewState.cs:46`、`Assets/CozyTown/Runtime/Application/ShopTradingViewState.cs:31`；混合读写接口见 `Assets/CozyTown/Runtime/Economy/IEconomyStateStore.cs:16`、`Assets/CozyTown/Runtime/Application/IFarmGameplayCoordinator.cs:5`。

### 3.2 首版增强

- 为四名 NPC 明确说话特征、可知事实与不知道时的回答方式，使用配置版本关联评测。现有短人设能作为输入起点，不能单独证明人设一致率达到目标。
- 增加整轮耗时、模型/工具调用数、回退原因、关联 ID 与用量记录；完整提示词/玩家文本记录遵循 PRD 的开关和最小化要求。默认一次只操作一个业务模态，有理由先使用单活跃会话和有界请求队列：`docs/PRD.md:116`、`:306`。
- 使用简短近期对话窗口后，再依据上下文长度测量决定是否需要摘要。摘要属于非权威记忆，不能把 NPC 曾经说出的价格、库存或物品承诺当作当前世界事实；当前数值仍从本轮快照查询。
- 给玩家区分等待、可继续输入、服务回退和取消状态；保留已有关闭与迟到结果保护。语音、逐 token 渲染并非本轮目标所必需。
- 将现有“非法候选文本不改状态”用例扩展到代理注册表、恶意工具名、跨 NPC 会话访问和历史注入；这些是工具/记忆新增行为的测试，不能依赖原有无工具实现间接覆盖。

### 3.3 以后项

- NPC 自主日程、寻路、任务、赠礼、交易或生产写工具。允许只读查询不等于允许这些状态命令；ADR-0002 复审要求命令白名单、主体授权、幂等、回滚和存档规则：`docs/adr/0002-deterministic-domain-and-ai-boundary.md:76`。
- 每 NPC 独立经济主体的默认初始化。角色经济模型已预留，但当前首版只读对话不要求模拟四个 NPC 的资金和库存。
- 向量数据库、跨会话长期检索、自动记忆整合、多 Agent 社会模拟、持续后台推理。当前只有四个静态 NPC 和单活跃对话场景，先测量有界历史与静态知识能否满足目标，再判断存储/调度成本；此为选型建议，不是已经实现的能力。

## 4. 深模块、浅接口的接入建议

建议继续沿用 Runtime 模块化单体和 Unity 窄接口注入，让游戏侧公开一个对话用例入口，由其管理 NPC 身份、玩家输入、会话生命周期、快照和取消；代理宿主内部封装模型适配、有限工具循环、输出收束与诊断。框架类型、消息格式或 SDK 回调不应扩散到商店、农田或 UI 模块。现有决策依据：`docs/adr/0001-modular-monolith.md:35`、`docs/adr/0005-unity-adapter-boundary.md:29`。

`NpcContentCatalog` 继续负责静态作者内容。动态聊天历史、世界快照和代理执行不宜继续塞入 Catalog；增加一个有明确会话语义的应用模块，比把 Catalog 扩大成全局状态入口更符合当前边界。首版工具可在代理收到的只读快照上查询，不必增加反向调用 Unity 的网络接口；该方案需用一条工具轨迹证明模型实际选择了查询，而不是仅把全部状态塞进提示词后宣称存在工具能力。

建议按可观察行为拆分后续实现：

1. 补充本轮已确认目标的验收条目与查询/记忆语义；保留原有 PRD/ADR 策略，补充会话和只读工具契约。
2. 固定替身下完成玩家消息、多轮隔离、状态快照、工具拒绝路径和读档失效。
3. 代理宿主接上假模型，验证工具循环、终止预算、取消和端到端关联 ID。
4. 登记 Scene-01 人工验收后启用真实模型；完成评测、诊断及演示构建。

这些切片不要求先引入数据库。若首版只保留当前游戏进程的有界聊天窗口，可使用内存存储并明确读档清空；若要求聊天跨重启或随游戏存档恢复，则应先决定 schema/迁移与会话分支语义，不能把聊天记录随意塞入现有经济存档。现有持久化边界证据：`Assets/CozyTown/Runtime/Save/GameSaveSnapshot.cs:16`。

## 5. 历史验证证据与覆盖限度

本次读取到的最新全量记录为 2026-09-02，属于历史证据：

| 证据 | 原始 XML 记录 | 本次核查含义 |
| --- | --- | --- |
| `Logs/issue2-final2-editmode.xml` | `279/279` passed，0 failed，0 skipped；结束时间 `2026-09-02 00:15:55Z` | 证明该次运行通过，不是 2026-09-05 重跑 |
| `Logs/issue2-final2-playmode.xml` | `35/35` passed，0 failed，0 skipped；结束时间 `2026-09-02 00:16:42Z` | 同上 |
| 对应 `.log` | 本次搜索未命中 `error CS`、`Scripts have compiler errors`、`Test run failed`、`UnhandledException`、`NullReferenceException` | 仅说明这些明确模式未匹配，不等于重新验收所有日志或运行画面 |
| 已跟踪结果摘要 | `docs/TEST_PLAN.md:484`、`:486` | 记录 Catalog/资产/稳定 ID 的 RED-GREEN 及最终 `279/279`、`35/35`；本地 Logs 不代替可移植测试报告 |

现有 AI 测试覆盖非法候选/超时/故障专属回退、DTO 复制、客户端预取消和恶意文本不改变五类确定性状态；PlayMode 覆盖关闭后迟到结果和共享 View 的当前 NPC 投影。证据：`Assets/CozyTown/Tests/EditMode/Npc/AiNpcDialogueGeneratorTests.cs:124`、`:204`、`:218`、`:256`、`:282`；`Assets/CozyTown/Tests/PlayMode/NpcDebugPresenterPlayModeTests.cs:50`、`:72`。

这些测试没有证明当前模型的人设准确率、记忆召回、工具查询正确率或真实服务延迟。特别是 `PublicAiBoundary_HasNoDeterministicStateWriteDependency` 目前检查公开构造函数与属性，列举钱包/背包等旧写接口，没有覆盖新工具注册表或 `IEconomyStateStore` 等未来注入方式；新增只读工具必须有自己的权限边界与行为测试。证据：`Assets/CozyTown/Tests/EditMode/Npc/AiNpcDialogueGeneratorTests.cs:219`。

## 6. 文档需要补充或澄清的地方

| 文档位置 | 观察 | 建议处理 |
| --- | --- | --- |
| `README.md:14`、`:112` | 提到筛选状态、`affinity`、`recentActivities` 和 `memories`，字段定义确实存在，但当前正式上下文只有时间和人设，后三者固定为零/空 | 当前状态说明明确“字段预留、尚无活动/记忆来源”，避免把协议容量误写为已交付功能 |
| `docs/PRD.md:99`、`:100` | FR-003/004 仍只描述玩家扣/加资产，未表达商店资金、有限库存和双方守恒；ADR-0010 已要求双方候选原子提交 | 增补双方库存/资金、失败状态不变与守恒的验收，保留原玩家用例；这是要求覆盖不足，不是旧条件失效。依据 `docs/adr/0010-character-shop-economic-ownership-and-atomic-trade.md:28` 至 `:33` |
| `docs/DELIVERY_PLAN.md:82` | M4 历史段落称右上非模态存档面板、复用一个 NPC 入口；后续 A1 段落已经记载齿轮系统菜单和四 NPC 独立入口 | 将该段明确标为 M4 当时状态，当前操作统一链接 README/A1；不能用历史记录认定当前 UI 仍如此 |
| `docs/DELIVERY_PLAN.md:84`、`docs/ARCHITECTURE.md:241` | 展示较早的 `260/260` 或 `151/26`；最新记录是 TEST_PLAN 的 `279/35` | 保留日期化历史，新增最新结果链接。历史计数本身不需要重写成最新计数 |
| `docs/ARCHITECTURE.md:120` | 称“同一不可变配置”，但 `CozyTownConfiguration` 只在构造时复制输入，数组属性仍可被调用方修改：`Assets/CozyTown/Runtime/Core/CozyTownConfiguration.cs:52`、`:78`。Catalog 的只读集合和 `NpcDefinition` 的只读属性则确有实现 | 澄清原始配置与验证后 Catalog 的可变性；此为措辞精确性与后续独立重构机会，优先级低于会话隔离和读档失效 |
| `docs/PRD.md:153`、`:269`、`:284` | 原用例为生成对话，评测已提及玩家诱导输入；最新确认的只读工具和多轮记忆尚无请求、生命周期、预算或验收定义 | 追加首版 Agent 用例/验收和补充 ADR；继续保留“候选无游戏写权限”主策略 |
| `docs/PRD.md:210`、`docs/ART_ACCEPTANCE.md:136`、`docs/DELIVERY_PLAN.md:101` | 均仍等待 Scene-01 人工验收，未发现有版本/结果的通过记录 | 这是待补证据的门禁，不能因为已有截图或多次缺陷修复而直接删除或标绿 |
| `CONTEXT.md:95` | 已有“对话候选”“固定回退”领域术语，尚未定义对话轮次、会话、记忆和只读查询 | 契约确认后补充术语，区分人物说法、历史记忆和权威游戏事实 |

本报告只列补充项，不修改 PRD、ADR、场景或业务实现。框架库对比与最终选型由同轮研究的其他报告汇总；本审计为它们提供实际需求与边界。
