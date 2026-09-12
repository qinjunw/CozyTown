# CozyTown 本地决策代理

这个 Python 标准库服务把 Unity 的 NPC 决策上下文转换为 DeepSeek Chat Completions 请求，并把一个 JSON 行动候选交回游戏。身份、日程、路径、会面预约和发言轮次由游戏验证。

## 启动

需要 Python 3.10 或更高版本。私有 JSON 文件中应有且只有一个对象满足 `Todo` 为 `CozyTown`，其 `testingAPIKey` 保存测试密钥。其他字段不控制请求地址或程序行为。密钥文件放在仓库外。

```powershell
python -B Tools/agent_proxy/decision_proxy.py --credential-file '<private-credentials.json>' --port 8766 --max-calls 12 --trace 'Logs/agent-proxy-session.jsonl'
```

代理仅监听 `127.0.0.1`。在启动 Unity 的进程环境中设置 `COZYTOWN_AGENT_PROXY_ENDPOINT=http://127.0.0.1:8766/decide`，再进入游戏。已经运行的编辑器需要重新启动才能继承新环境。普通 batch 测试忽略这个变量。

真实场景联调需要为测试进程显式设置 `COZYTOWN_RUN_LIVE_MEETING=1` 和 `COZYTOWN_LIVE_MEETING_ENDPOINT=<loopback-decide-url>`，只运行 `NpcMeetingPlayModeTests.LiveProxy_RealProviderDrivesTheSceneMeetingWithinTheConfiguredBudget`。该测试从 12:15 开始，以正常游戏倍率推进两位居民，记录 `Logs/agent-meetings-live-scene.json`。记录已存在时拒绝再次执行；重跑前应保留旧证据并核对剩余预算。测试不直接读取密钥。

每个代理进程最多尝试 `--max-calls` 次提供方请求，失败也计数，同时最多 2 个请求。没有自动重试；每次最多生成 512 Token、关闭思考模式。请求型号固定为用户选择的 `deepseek-v4-flash`。实际返回的型号、Token 用量、耗时、候选和错误码记录在本地 JSONL，密钥、完整上下文和提供方思考内容不写入记录。`GET /status` 返回本进程已尝试次数、剩余额度和在途数。

记录文件必须尚不存在，避免覆盖上一次证据。重启进程会创建新的调用预算；需要控制一次联调总量时，应扣除之前已经尝试的次数。停止代理使用 Ctrl+C。调用上限耗尽后，游戏继续按宿主日程运行，待处理会面仍受游戏时间期限约束。

## 验证

```powershell
python -B -m unittest discover -s Tools/agent_proxy -p 'test_*.py'
```

这些测试使用假的提供方或本机 HTTP 服务，不读取真实密钥，不消耗模型额度。

协议和游戏运行规则见 [NPC 自主决策协议](../../docs/NPC_AGENT_PROTOCOL.md) 与 [会面 ADR](../../docs/adr/0017-resident-meeting-commitments.md)。
