# CozyTown 本地决策代理

这个 Python 标准库服务把 Unity 的 NPC 决策上下文转换为 DeepSeek Chat Completions 请求，并把一个 JSON 行动候选交回游戏。身份、日程、路径、会面预约和发言轮次由游戏验证。

## 启动

需要 Python 3.10 或更高版本。私有 JSON 文件中应有且只有一个对象满足 `Todo` 为 `CozyTown`，其 `testingAPIKey` 保存测试密钥。其他字段不控制请求地址或程序行为。密钥文件放在仓库外。

```powershell
python -B Tools/agent_proxy/decision_proxy.py --credential-file '<private-credentials.json>' --port 8766 --max-calls 12 --trace 'Logs/agent-proxy-session.jsonl'
```

代理仅监听 `127.0.0.1`。在启动 Unity 的进程环境中设置 `COZYTOWN_AGENT_PROXY_ENDPOINT=http://127.0.0.1:8766/decide`，再进入游戏。已经运行的编辑器需要重新启动才能继承新环境。普通 batch 测试忽略这个变量。

真实场景联调需要为测试进程显式设置 `COZYTOWN_RUN_LIVE_MEETING=1` 和 `COZYTOWN_LIVE_MEETING_ENDPOINT=<loopback-decide-url>`，只运行 `NpcMeetingPlayModeTests.LiveProxy_RealProviderDrivesTheSceneMeetingWithinTheConfiguredBudget`。该测试从 12:15 开始，以正常游戏倍率推进两位居民，记录 `Logs/agent-meetings-live-scene.json`。记录已存在时拒绝再次执行；重跑前应保留旧证据并核对剩余预算。测试不直接读取密钥。

资源交付联调使用独立的 `COZYTOWN_RUN_LIVE_RESOURCE=1` 和 `COZYTOWN_LIVE_RESOURCE_ENDPOINT=<loopback-decide-url>`，只运行 `NpcResourcePlayModeTests.LiveProxy_TransfersOwnedResourcesThroughTheActualScene`。启动代理时设置 `--max-calls 10`。场景记录 `Logs/agent-resources-live-scene.json`，包含到场位置、交付结果、双方相关资产及日程恢复；同样拒绝覆盖已有记录。普通游戏通过自主决策端点启用 Sora 缺鱼联系 Ren 的场景，普通散步计划仍可显式配置。

## 多初始场景评测

`NpcResourceScenarioPlayModeTests.LiveProxy_RepeatsFourFreshInitialScenarios` 在实际场景中重复以下四种配置，各三次：

| 场景 | Ren 的鱼 / 金币 | Sora 的鱼 / 金币 | 行为观察目标 |
| --- | --- | --- | --- |
| available | 2 / 0 | 0 / 50 | 自主邀约、赴约、交付与结束 |
| seller_empty | 0 / 0 | 0 / 50 | Ren 根据自己的库存拒绝 |
| buyer_poor | 2 / 0 | 0 / 10 | Sora 根据自己的余额等待 |
| need_satisfied | 2 / 0 | 1 / 50 | 宿主不产生补鱼机会 |

每轮重新加载场景并创建经济服务、世界、会面板和调度器；首个请求前验证双方没有活动、会面、记忆或旧决策，新世界 ID 与前轮不同。Sora 的盐、其他居民、玩家和商店采用默认初始配置。每组重复轮换场景顺序，所有调用共享一个代理预算。普通休息决策也交给真实模型，不强制等待；报告区分普通与社交调用。

启动一个 `--max-calls 96` 的代理，为 Unity 测试进程设置 `COZYTOWN_RUN_LIVE_SCENARIOS=1`、`COZYTOWN_SCENARIO_ENDPOINT=<loopback-decide-url>`。使用图形模式的 batch PlayMode，仅过滤上述方法，不加 `-quit`。每轮最多向代理发送 12 次请求、观察 100 秒；调度器仍使用默认的每分钟 8 次、并发 2、请求超时 8 秒。测试以正常游戏倍率从 12:15 推进；终止模型派发后推进到 16:40 检查日程恢复。恢复检查期间不增加调用。

`Logs/agent-resource-scenarios-live.json` 保存所有轮次的初始化、完整请求上下文、候选、执行结果、资产、到场状态和台词。已有报告会阻止启动，进行中的报告随阶段变化更新。代理 JSONL 以 `decisionId` 对齐真实调用、实际模型、Token 和耗时；单次决策的多步请求还需按 `step` 区分。测试结果通过仅表示初始化、资产门禁及恢复检查通过；模型是否达成目标必须读取各轮 `behavior`，不能用测试绿色代替行为成功率。失败与超时保留在原轮次，重新运行属于另一个实验，需要新的证据目录与单独的预算记录。

普通测试 `FreshScenes_SeparateReasonableChoicesFromRejectedTrades` 使用固定客户端验证重置、合理拒绝及宿主拦截不可行交付的分类，不读取密钥。

每个代理进程最多尝试 `--max-calls` 次提供方请求，失败也计数，同时最多 2 个请求。没有自动重试；每次最多生成 512 Token、关闭思考模式。请求型号固定为用户选择的 `deepseek-v4-flash`。实际返回的型号、Token 用量、耗时、候选和错误码记录在本地 JSONL，密钥、完整上下文和提供方思考内容不写入记录。`GET /status` 返回本进程已尝试次数、剩余额度和在途数。

记录文件必须尚不存在，避免覆盖上一次证据。重启进程会创建新的调用预算；需要控制一次联调总量时，应扣除之前已经尝试的次数。停止代理使用 Ctrl+C。调用上限耗尽后，游戏继续按宿主日程运行，待处理会面仍受游戏时间期限约束。

## 验证

```powershell
python -B -m unittest discover -s Tools/agent_proxy -p 'test_*.py'
```

这些测试使用假的提供方或本机 HTTP 服务，不读取真实密钥，不消耗模型额度。

协议和游戏运行规则见 [NPC 自主决策协议](../../docs/NPC_AGENT_PROTOCOL.md) 与 [会面 ADR](../../docs/adr/0017-resident-meeting-commitments.md)。
