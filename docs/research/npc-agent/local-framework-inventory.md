# 本地 NPC Agent 框架核查

核查日期：2026-09-05。关联研究票：[Inspect local Pi, CodeWhale, and DeepSeek Harness runtimes](https://github.com/qinjunw/CozyTown/issues/25)。

目标是首版 NPC 根据人设、游戏状态和对话记忆回答，并调用只读查询工具。以下结论来自已安装包、已存在源码及第一方文档的静态检查；未启动代理、调用模型、安装依赖或读取个人凭据、会话与聊天日志。

## 结论

在这三个本地候选中，建议先验证 **Pi 的 `Agent` + `pi-ai` 作为独立 Node NPC 服务的内核**。本地版本已提供构造 Agent、注入消息和工具、流式事件、取消及循环控制入口。游戏宿主仍需实现自身的上下文、工具、记忆、预算和响应协议。此处选择的是可编程 API；安装的 Pi coding CLI 及其个人扩展不属于 NPC 服务的运行配置。

DeepSeek Harness 可作为后备：其插件结构能表达同一方案，但需要装配 Cordis 服务和 NPC 专用入口。本地版本仍为 developer preview；现成 SDK 的取消语义也不适合直接复用为游戏对话请求。CodeWhale 更适合继续辅助项目开发；本次查到的公开 SDK 驱动的是既有 coding runtime，而不是只接收 NPC 工具的通用循环库。

这是基于接口和装配工作的判断，尚无三者的 NPC 场景延迟、模型质量或资源占用对比结果。

## 身份、版本与本地证据

报告使用逻辑根目录，避免把机器路径写入项目：`<pi-install>` 指全局安装的 `@earendil-works/pi-coding-agent`；`<codewhale-install>` 指全局 `codewhale` npm 包；`<dsh-source>` 指已归档的 DeepSeek Harness shallow clone。具体位置由命令入口和 Git 元数据解析，可在本机重新定位。

| 候选 | 本地可核实状态 | 来源与版本限制 |
| --- | --- | --- |
| Pi | CLI、嵌套 `pi-agent-core`、`pi-ai` 均为 **0.84.4**；包名均为 `@earendil-works/*` | `package.json` 指向 `earendil-works/pi`，作者 Mario Zechner；不是旧 `@mariozechner/*` API。安装包没有 Git 历史，本次未将其与发布 tarball 做逐字节校验。 |
| CodeWhale npm | PATH 优先入口是 Node 启动器；包版本 **0.8.57**，下载目录内两个 `.exe.version` 标记也为 **0.8.57** | 包来源 `Hmbown/CodeWhale`；`bin/codewhale.js` → `scripts/run.js` → 原生二进制。标记是安装证据，不等于运行二进制的版本实测。 |
| CodeWhale Cargo | 另有 `codewhale.exe`、`codewhale-tui.exe`；Cargo 安装记录为 **0.9.1** | 记录来源 `cnb.cool/codewhale.net/codewhale?tag=v0.9.1`，commit `079e686ce067ffe010569e7a44fa41d848d9cb41`。对应 checkout 已无源文件，未证实它与 GitHub 上游是否逐字相同。 |
| DeepSeek Harness | 独立源码 **0.1.0-rc.7**，HEAD `99f6f02fecdb7dff40c3fbc9470f5907c29f74ca`，2026-08-17；工作区干净，HEAD 与已有 `origin/master` 一致 | origin 为 `deepseek-ai/deepseek-harness`，shallow clone，位置属于归档旧克隆；这能证明本地有源码，不能证明它是当前使用的已安装运行时。未发现 `deepseek` / `deepseekharness` 命令入口。 |

复核文件：Pi 的 `package.json`、`node_modules/@earendil-works/pi-agent-core/package.json`、`node_modules/@earendil-works/pi-ai/package.json`；CodeWhale 的 `package.json`、`scripts/run.js`、`scripts/install.js`、`bin/downloads/**.version`，以及 Cargo 的 `.crates.toml` 安装记录；DSH 的 `package.json` 和 Git HEAD、remote、status。

CodeWhale 的本地 README 说明它曾以 `deepseek-tui` 发布，并兼容 `.deepseek` 配置目录。因此 `.deepseek` 目录本身不能证明 DeepSeek Harness 已安装；独立 Harness 的身份由上述官方仓库源码确认。

## Pi 0.84.4：可复用层与需补模块

| 关注点 | 本地代码证据 | NPC 适配含义 |
| --- | --- | --- |
| 嵌入入口 | `pi-agent-core/dist/agent.d.ts` 导出 `Agent`，接收 `initialState` 和 `streamFn`；`pi-ai` 导出 `createModels` 和 provider 子路径 | 在 NPC 服务内创建 Agent，显式注册 provider；依赖锁定 0.84.4，不能照搬旧版全局 `getModel` 示例。 |
| 默认工具 | `dist/agent.js:26-28` 初始化 `tools` 为传入数组的副本，省略时为 `[]` | 只注册游戏查询工具就不会自动获得 coding CLI 的 bash/read/edit/write 工具。包也导出其他 harness/tools 能力，宿主仍需限定所实例化的组件。 |
| 人设与记忆 | `initialState.systemPrompt/messages`；`transformContext`、`convertToLlm`；`state.messages` 维护对话 | 能承载人设和对话历史，但 NPC 可知事实、记忆长度及存档隔离由游戏模块定义。 |
| 工具拦截 | `beforeToolCall` 在参数校验后执行，可拒绝；`afterToolCall` 可处理结果 | 每次调用核验工具名、NPC 身份、参数及查询范围；提供按调用记录的审计数据。 |
| 停止与取消 | `shouldStopAfterTurn`；`abort()` 向活动运行的 AbortController 发信号 | 前者在当前模型及工具结束后阻止下一轮；后者是协作取消。超时、最大模型轮次、工具次数、并发数需要宿主制定并执行。 |
| 会话标识 | `agent.d.ts:49-50` 明确 `sessionId` 转发给 provider 以支持缓存 | 缓存标识不构成 NPC 记忆持久化，不能用它替代存档中的 NPC 对话历史。 |
| 用量 | `pi-ai/dist/types.d.ts` 的 `AssistantMessage.usage` 包含 token 和 cost 字段 | 可采集用量；价格数据及缺失 usage 的行为仍需自己处理，不能据此声称已有限额或准确账单。 |

本地运行环境 Node 24.15.0 满足这些包声明的 `>=22.19.0`。这是版本条件检查，尚未验证 CozyTown 服务的实际 import、构建及模型协议。正式接入应在项目服务目录安装锁定依赖，不引用开发者全局 npm 目录。

证据根：`<pi-install>/node_modules/@earendil-works/pi-agent-core/{README.md,dist/agent.js,dist/agent.d.ts,dist/types.d.ts}`、相邻 `pi-ai/dist/types.d.ts`；第一方架构说明见 [Pi Agent README](https://github.com/earendil-works/pi/blob/main/packages/agent/README.md)。链接为上游 main，版本判断以上述本地 0.84.4 文件为准。

建议在游戏侧保留一个 `NpcConversation` 门面，使 provider、消息格式、工具回合和持久化细节留在门面内部。外部输入可以收敛为 NPC ID、玩家输入、只读世界快照、会话/存档标识和取消信号；输出为经过游戏校验的回复。框架对象不应扩散到经济、背包、种植或 Unity UI 模块。

## DeepSeek Harness rc.7：可装配，但现成客户端有取舍

本地 README 确认这是 DeepSeek AI 的独立开源项目，使用 Cordis 插件。它明确处于 developer preview，并预告破坏兼容的变化。以下行为以本地 commit 为准。[README](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/README.md)

- **同进程构造可行。** `ctx.agents.create({sessionId, agentOptions, setup})` 返回生命周期句柄，`setup` 可在发布前配置该 NPC 的 scoped persona/tools；核心 loop 依赖 agents、sessions、llm、tools、systemPrompt 五个服务。需自己提供 NPC 专用 Cordis 组合。[Agent loop](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/packages/core/agent-loop/README.md)
- **工具隔离有原语。** `ctx.tools.register` 支持 scoped 工具；`guard` 可拒绝执行，`restrict` 只是可见性组合。应从空的 NPC 组合注册查询工具，并使用 guard；不能把工具可见性当作数据访问权限。[Tools](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/packages/core/tools/README.md)
- **现成示例带开发工具。** `agent-spine-demo` 默认组合包括本地 skills、workspace instructions、bash schema、jobs 等；headless 示例还含文件、shell、子代理和 workflow。复用这些整体配置会扩大 NPC 宿主职责。[Spine](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/packages/examples/agent-spine-demo/README.md)、[Headless](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/examples/headless-agent/README.md)
- **取消需选对入口。** 同进程 `agent.cancel` 能取消当前活动，工具需响应 signal；loop 未自带整轮预算。TypeScript `DeepSeekHarness` SDK 却是 stdio JSON-RPC 子进程客户端，本地文档明确没有 mid-turn/per-prompt cancel，放弃任务需关闭 runtime。`run()` 收集到 agent idle 的最终文本，也不保证对应单条 prompt 的因果结果。[源码取消入口](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/packages/core/agent-loop/src/agent.ts#L134)、[SDK](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/packages/sdk/client/README.md)
- **持久化和用量可复用。** 有 event-sourced session、JSONL/SQLite persistence 插件及 token-meter；但会话日志不会自动变成角色记忆，仍需限定 NPC 可见内容、摘要和存档关联。token-meter 的部分数值来自启发式估算，不能用 UI 占用比例充当精确计费上限。[Session persistence](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/packages/session/session-persistence/README.md)、[Token meter](https://github.com/deepseek-ai/deepseek-harness/blob/99f6f02fecdb7dff40c3fbc9470f5907c29f74ca/packages/llm/token-meter/README.md)

适配成本判断：若当前目标是学习插件生命周期、事件溯源和可替换后端，DSH 有研究价值；对四个 NPC 的首版只读对话，需要的装配和版本维护工作多于 Pi `Agent` 子集。本地旧克隆与当日最新上游之间的差异未拉取核对，不应把此结论外推为最新版本缺少同样能力。

## CodeWhale：不要把有 SDK 等同于适合 NPC 内核

本地 npm 包只暴露原生程序启动入口，`scripts/run.js` 会解析/准备二进制并 `spawnSync`。直接调用它意味着运行完整程序；本次未执行 `--version`，因为 wrapper 的路径解析还可能触发下载。

上游当前的 [`@codewhale/runtime-sdk`](https://github.com/Hmbown/CodeWhale/blob/main/npm/runtime-sdk/README.md) 自称 transport-only，操作现有 Rust Runtime 的 Fleet、worker 和事件。它并不绕过 runtime 的权限、provider 和执行账本。文档示例已经采用 v0.9.11 能力，不能认为本机 0.8.57/0.9.1 自动具备它。

上游 [Agent Runtime](https://github.com/Hmbown/CodeWhale/blob/main/docs/AGENT_RUNTIME.md) 描述的外部接入是启动 `codewhale exec`，配独立工作环境和受限工具，观察事件与用量。公开接口的对象是 coding worker；停止 worker、恢复执行账本和 NPC 单次对话取消的语义仍需桥接。

适配成本判断：把 CodeWhale 装成 NPC 服务，需要确认实际二进制版本、独立配置、权限与工具范围、进程控制、事件转换及记忆映射。本地可见源码没有证明存在比 Pi `Agent` 更小的 NPC 专用嵌入入口，因此不推荐作为此阶段的实现起点。这里不对原生工具的默认执行授权作推断；本次未读取个人权限设置，也未检查匹配安装版本的全部 Rust 源码。

## 许可证与版本维护事实

| 项目 | 已读事实 | 本次检查边界 |
| --- | --- | --- |
| Pi | 本地 package 声明 MIT；第一方 [LICENSE](https://github.com/earendil-works/pi/blob/main/LICENSE) 为 MIT，版权 Mario Zechner | 尚未逐项审计锁定依赖许可证。 |
| CodeWhale | 本地 npm package 声明 MIT；第一方 [LICENSE](https://github.com/Hmbown/CodeWhale/blob/main/LICENSE) 为 MIT，版权 DeepSeek CLI Contributors | Cargo 的 CNB 来源没有可用本地源码，未核对其文件差异及许可证文本。 |
| DeepSeek Harness | 本地 LICENSE 为 MIT，版权 2026 DeepSeek；README 链接第三方声明 | 本地 clone 工作区干净且与已存 remote ref 一致；shallow 历史不证明它与当日最新上游一致。 |

这些是软件许可证文件的事实记录，不构成第三方依赖或模型使用条款的完整审计。

## 三个候选都不能替游戏补齐的能力

1. **世界查询契约：** 从游戏投影只读状态，限定角色可见范围；道具数量、价格、时间等事实以游戏数据为准，工具无修改入口。
2. **对话身份与记忆：** 按存档实例、NPC ID、会话隔离；明确加载旧存档时的记忆恢复/失效规则，控制保留长度。
3. **受限循环：** 最大模型轮次、最大查询次数、每次查询返回量、输出 token 和整个请求的截止时间。发生超时或关闭 UI 时取消，并丢弃过期结果。
4. **模型协议与回退：** 输出校验、人设回退、错误分类以及 UI 状态；仅框架成功返回文本不足以通过游戏验收。
5. **可复现验证：** 用假模型/假工具验证工具拒绝、超预算、取消、串 NPC/串存档、只读状态不变和回复来源；再用少量真实模型检查质量与延迟。

建议先以 Pi 0.84.4 完成上述契约的离线原型，再决定持久化后端。首版对话记忆容量尚不要求引入向量库；采用已有存档或独立存储仍需服从游戏的加载与回滚语义。
