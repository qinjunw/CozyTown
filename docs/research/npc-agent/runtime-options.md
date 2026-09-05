# NPC Agent 运行时选型研究

- 日期：2026-09-05；游戏代码基线：`7bfe65e`；研究票：[Compare bounded NPC runtime and agent framework designs](https://github.com/qinjunw/CozyTown/issues/26)。
- 目标：4 名 NPC 根据人设、游戏状态和对话记忆回答玩家，并调用只读查询工具。
- 状态：选型建议，尚未成为已接受 ADR；本次仅核查代码和第一方资料，没有安装依赖、启动模型或产生计费调用。

## 建议及比较

建议用独立的 `NpcAgentService` 封装 **Pi agent-core 0.84.4 + pi-ai**，由 Unity 通过现有 HTTP 代理边界调用。四名 NPC 共用服务和模型适配器，各自有隔离的对话记录；玩家发起交谈才运行一次有限工具循环。人设、查询授权、存档时间线和输出验收由游戏应用契约控制。

本地已安装的 Pi 核心包是 `@earendil-works/pi-agent-core` 0.84.4；对应上游 [v0.84.4 文档](https://github.com/earendil-works/pi/blob/v0.84.4/packages/agent/README.md)。旧 `badlogic/pi-mono` 地址已重定向，网上 `@mariozechner/*`、全局 `getModel()` 示例不能直接作为该版本接入代码。

| 方案 | 可复用入口与能力 | 本项目还需承担的工作 | 适用判断 |
| --- | --- | --- | --- |
| Pi agent-core + pi-ai | `new Agent({ initialState, streamFn })`，显式工具表、事件流、上下文转换和取消。 | 对话契约、只读快照、调用预算、记忆提交和 Unity 适配。 | 首选；已有循环和扩展点能承载首版需求。[Pi 核心](https://github.com/earendil-works/pi/blob/v0.84.4/packages/agent/README.md) |
| pi-ai + 薄编排函数 | `createModels()`、注册 provider、`complete/stream`，支持自定义兼容端点。 | 自行组织工具结果回填、循环终止和事件记录。 | 最小对照基线；当流程固定为预取上下文后生成一句回复时可用。[pi-ai](https://github.com/earendil-works/pi/blob/v0.84.4/packages/ai/README.md) |
| LangGraph JS | `StateGraph` 的模型节点、工具节点和结束边；checkpointer 管理线程检查点。 | 图状态/reducer、检查点与游戏存档映射、同样的领域授权。 | 首版无多阶段恢复需求；出现跨交互持续任务时再评估。[Quickstart](https://docs.langchain.com/oss/javascript/langgraph/quickstart)、[Persistence](https://docs.langchain.com/oss/javascript/langgraph/persistence) |
| OpenAI Agents SDK JS | `Agent` + `Runner.run`；`maxTurns`、`signal`、输出 schema、Session 与 tracing。 | 自定义游戏 Session、只读工具、非默认模型适配和追踪出口配置。 | 若后续标准化使用其模型适配与追踪栈，可作为替代。[运行](https://openai.github.io/openai-agents-js/guides/running-agents/)、[Agent](https://openai.github.io/openai-agents-js/guides/agents/) |

这里“适合”是对集成面的判断，尚无真实模型延迟或质量排名。LangGraph 可以脱离 LangChain 使用，也不要求购买托管平台；选型差异来自当前是否需要图执行和恢复语义。[LangGraph Overview](https://docs.langchain.com/oss/javascript/langgraph/overview)

## Pi 的嵌入边界与执行限制

Pi 0.84.4 的 `Agent` 默认工具为空，可只传入游戏查询函数；`streamFn` 可由 `createModels()` 注册的 provider 提供。只引用 `pi-agent-core` 和 `pi-ai` 即可组织该调用，不需要加载 coding CLI、开发会话、项目 skills 或文件/终端工具。[Agent 源码](https://github.com/earendil-works/pi/blob/v0.84.4/packages/agent/src/agent.ts)

建议服务通过以下已有扩展点封装预算，预算值在验证请求与真实模型耗时后确定：

- `beforeToolCall`：校验工具白名单、参数范围、当前 NPC 可见范围和剩余工具额度；拒绝任意文件路径、命令、URL 或跨角色私有记忆查询。
- `shouldStopAfterTurn`：完成当前模型与工具轮次后阻止下一轮；这是轮末停止，不会中断当前网络请求或正在执行的工具。
- `streamFn` 包装：每次准入模型请求前检查次数与总时间预算；限制输出 token，并把重试计入预算。
- `abort()` 与传入工具的 `AbortSignal`：联动面板关闭、切换 NPC、读档和 HTTP 连接取消。工具实现也必须遵守取消信号。

上述 hooks 和执行次序可见 [Agent 类型契约](https://github.com/earendil-works/pi/blob/v0.84.4/packages/agent/src/types.ts) 与 [循环源码](https://github.com/earendil-works/pi/blob/v0.84.4/packages/agent/src/agent-loop.ts)。不能仅依赖 `terminate: true` 充当硬限制：混合工具批次只有全部结果都要求停止才提前终止；当前轮内大量工具仍需逐次准入。

建议初始实验从少量模型轮次和工具调用开始，分别统计次数、总耗时和 token。达到预算而未获得可验证最终回复时走现有回退，不继续重试到成功。该预算策略是本项目建议，SDK 本身不会替游戏定义花费上限。

Pi 的统一 API 覆盖多个 provider，并提供 OpenAI/Anthropic 兼容端点入口；这只能证明适配入口存在。候选模型仍须分别验证中文人设、工具参数、工具结果续写、结构输出与取消表现，不能用“兼容 OpenAI”推导这些行为全部等价。[pi-ai 兼容与取消说明](https://github.com/earendil-works/pi/blob/v0.84.4/packages/ai/README.md)

## 当前契约与接入缺口

当前 [JSON codec](../../../Assets/CozyTown/Unity/Npc/ProxyNpcDialogueJsonCodec.cs) 发送 `npcId/displayName/persona/day/minuteOfDay/affinity/recentActivities/memories`，接收 `text/emotion/action`。[Coordinator](../../../Assets/CozyTown/Runtime/Application/NpcDialogueCoordinator.cs) 注入时钟；[Catalog](../../../Assets/CozyTown/Runtime/Npc/NpcContentCatalog.cs) 仍把 affinity 设为 0，活动与记忆设为空数组。

因此现有字段不等于已有游戏记忆或完整世界知识。首版还需玩家输入入口、对话身份、真实状态投影和记忆提交。建议请求契约补充 `requestId`、稳定 `worldId/playerId/npcId`、运行 `timelineId`、`snapshotVersion`、玩家发言、受限记忆和只读世界快照。`worldId` 不宜直接拿随机种子替代：相同种子可创建不同存档。

建议保持一个应用门面，并在门面内部隐藏框架消息、工具调用格式与 provider 配置：

```text
Unity 对话用例：玩家发言 + NPC ID
  → 从同一游戏状态构造不可变快照与记忆
  → NpcAgentService：校验请求、装配人设、有限查询循环、验证候选
  → Unity 验证 request/timeline 与 text/emotion/action
  → 显示有效结果并提交本轮对话记录
```

现有 [AiNpcDialogueGenerator](../../../Assets/CozyTown/Runtime/Npc/AiNpcDialogueGenerator.cs) 已有 500 字符上限、表现标签允许列表、取消和分类回退，可继续担当客户端最后一道候选验收。它本地生成的 correlation ID 当前没有进入请求，需将端到端 request ID 与诊断记录贯通。[ADR-0002](../../adr/0002-deterministic-domain-and-ai-boundary.md) 的只读状态边界继续适用。

## 只读工具如何获取可信游戏信息

建议工具首先查询本次请求携带的不可变快照。例如 `query_shop_stock(itemId)` 返回实际库存和配置价格，`query_player_items(itemId)` 返回玩家允许公开的背包项，`query_recipe(recipeId)` 返回静态配方。它们是领域查询函数，工具参数不能选择另一个世界、任意角色或文件。

快照由 Unity 应用层从确定性模块投影，模型只提出查询参数。快照建立之后，同轮所有工具返回相同 `snapshotVersion`；库存数字和价格来自领域状态及配置，模型不得自行补齐缺失事实。此方式无需为首版开放能够执行 Unity 命令的反向服务器。

单机本地原型中，Unity 是游戏状态来源；远端服务看到的仍是客户端声明，快照 hash 只证明内容一致，不能证明客户端没有作弊。若以后增加多人或共享经济，权威状态和授权应迁入对应服务。这是升级条件，不是首版框架自带能力。

提示中区分角色人设、世界事实、对话记录与玩家原话。玩家说“我刚卖了三条鱼”只代表他说过这句话；首版可查询当前资产，但没有交易事件来源时不能据此确认历史交易。带时效的“昨日商店有鱼”不能覆盖今天库存查询。

## 会话、记忆与读档时间线

建议首版由游戏进程在内存中持有有界对话记录，服务只使用传入的已提交记录。每请求可创建短命 Agent，从这些记录重建上下文；四名 NPC 不要求四个常驻进程。这样仅代理服务重启不会丢失游戏进程中的记录，也不需要把框架消息写入存档。

如服务缓存 Agent，会话键至少区分 `worldId/playerId/npcId/timelineId`，并检查 memory revision；同一键串行提交。Pi 的 `sessionId` 文档用途是 provider caching，它不能代替上述游戏身份与恢复协议。[Pi Session 说明](https://github.com/earendil-works/pi/blob/v0.84.4/packages/agent/README.md)

首版建议关闭对话面板时保留记录；每次成功读档生成新的运行 timeline，清空旧对话和任何派生摘要，取消旧 timeline 的请求。新游戏或游戏重启不继承记忆。读档前的晚到回复必须同时被 UI 与记忆提交拒绝。例如第 3 天的交流，读回第 2 天存档后不能成为 NPC 已知事实。此方案尚待实施前确认。

首版区分“某人说过什么”与“当前查询事实”即可，不以新增世界事件存储为前置。预算不足时优先保留最近的完整对话轮次。若以后需要历史事件回忆或摘要，再引入有时间与来源的已提交事件记录，并要求摘要指回来源；模型叙述不能替代事件证据。

如果另行确认需要跨重启记忆或读档时恢复当时的交流，再扩展存档捕获已提交对话、读档恢复对应记忆，并补充 schema 与迁移设计；首版内存方案不包含该改造。[ADR-0012](../../adr/0012-economic-save-schema-v2-and-v1-migration.md) 当前使用完整 JSON 快照，后续有界记忆也可评估沿用该格式，不必仅因加入 Agent 就引入数据库。

LangGraph 的 checkpointer/store，或 OpenAI SDK 的 Session，都能保存运行记录，但不会自动遵守游戏读档分叉。LangGraph 的图 time-travel 也不是整套游戏存档恢复。[LangGraph Persistence](https://docs.langchain.com/oss/javascript/langgraph/persistence)、[Time travel](https://docs.langchain.com/oss/javascript/langgraph/use-time-travel)、[OpenAI Sessions](https://openai.github.io/openai-agents-js/guides/sessions/)

## 验证与部署

以下是接入实现前应锁定的验收行为，尚无本轮实测结果：

| 验证层 | 建议断言 |
| --- | --- |
| 契约与工具 | 未知 NPC/工具/越界参数被拒；工具只能读本次投影；模型诱导交易后钱包、物品、时钟不变。 |
| 会话与存档 | 4 NPC、不同 world/player、读档后的 timeline 相互隔离；取消、旧回复、重复 request 不提交记忆。 |
| 事实时效 | 买卖或换日后新请求反映新库存；同轮查询始终来自同一快照；玩家自述不变成世界事件。 |
| 预算与回退 | 模型反复请求工具、一次返回过多工具、流不结束、格式错误、provider 异常均在预算内终止；既有固定回退可显示。 |
| 人设评测 | 按 ADR-0002 至少 30 条夹具，覆盖 4 NPC 人设、短期记忆、事实冲突和越权诱导；记录模型/提示/配置版本及人工评分。 |

TDD 可先用脚本化模型响应验证循环与协议，再以可控 HTTP 假服务验证 Unity 取消/读档/回退。真实模型只承担离线质量和耗时评测；记录输出与评分，不能要求每次生成逐字相等。

服务日志建议包含 request/session/timeline/snapshot ID、模型和提示版本、工具名及结果版本、token、耗时、回退原因。Pi 事件流提供采集入口；OpenAI SDK 自带 tracing，但服务端默认会导出追踪且可包含模型/工具输入输出，采用时须显式选择关闭或配置出口。[Pi 事件](https://github.com/earendil-works/pi/blob/v0.84.4/packages/agent/src/types.ts)、[OpenAI Tracing](https://openai.github.io/openai-agents-js/guides/tracing/)

开发可使用同机 Node 服务；分发演示可选择托管代理。凭据只在服务配置中，Unity 在服务不可达时走回退。首版无需 MCP、向量数据库、图服务器或每 NPC 独立进程。所有依赖应写入项目服务目录的 manifest/lockfile，不能把全局 CLI 的存在当成可重现部署。

成本按每次交谈的实际模型请求累加：输入 token、输出 token、缓存和 provider 定价；工具结果会增加下一轮输入。具体金额与 p95 延迟尚未测量，不能把 SDK 免费开源等同于模型调用免费，也不能把取消请求当成已经消费 token 的退款。避免持续后台自言自语，先测交谈触发的请求量和回退比例。

## 复审条件

- 需要跨多次交互暂停/恢复的任务、分支流程或审批时，再比较 LangGraph；其 `recursionLimit` 计算图 super-step，不直接等同工具数或模型请求数，预算仍需另行约束。[Graph API](https://docs.langchain.com/oss/javascript/langgraph/graph-api)
- 标准化采用 OpenAI provider、结构输出和集中 tracing 时，重新评估 Agents SDK；非 OpenAI 模型有 ModelProvider/AI SDK 适配路径，但专属 Responses 功能不应假设跨 provider 可用。[Models](https://openai.github.io/openai-agents-js/guides/models/)
- 长期记忆出现可测量的检索、并发或增量写需求时，再评估 SQLite/服务端存储；存档与记忆的同点恢复必须保留。
- NPC 获准影响交易、奖励或日程时，按 ADR-0002 另行设计候选命令、授权和确定性执行，不通过给当前工具加写引用完成升级。

CodeWhale 已有 SDK，但本次查到的 [runtime-sdk](https://github.com/Hmbown/CodeWhale/blob/main/npm/runtime-sdk/README.md) 是驱动 Rust coding Runtime 的客户端；[外部 harness 合约](https://github.com/Hmbown/CodeWhale/blob/main/docs/AGENT_RUNTIME.md) 也以 headless `exec` 与隔离配置为入口。它可用于开发工作，总体仍需额外隔离编码工具、工作目录和执行账本才能用作 NPC；主线能力不能视作本地旧版本已具备。Pi、CodeWhale 与独立 DeepSeek Harness 的本地版本核查由同轮环境审计记录补充。
