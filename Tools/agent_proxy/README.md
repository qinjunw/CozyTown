# CozyTown 本地决策代理

这个 Python 标准库服务把 Unity 的 NPC 决策上下文转换为 DeepSeek Chat Completions 请求，并把一个 JSON 行动候选交回游戏。身份、日程、路径、会面预约和发言轮次由游戏验证。

## 启动

需要 Python 3.10 或更高版本。私有 JSON 文件中应有且只有一个对象满足 `Todo` 为 `CozyTown`，其 `testingAPIKey` 保存测试密钥。其他字段不控制请求地址或程序行为。密钥文件放在仓库外。

```powershell
python -B Tools/agent_proxy/decision_proxy.py --credential-file '<private-credentials.json>' --port 8766 --max-calls 12 --trace 'Logs/agent-proxy-session.jsonl'
```

代理仅监听 `127.0.0.1`。在启动 Unity 的进程环境中设置 `COZYTOWN_AGENT_PROXY_ENDPOINT=http://127.0.0.1:8766/decide`，再进入游戏。已经运行的编辑器需要重新启动才能继承新环境。普通 batch 测试忽略这个变量。

完整世界快照还要求在 Unity 启动环境设置 `COZYTOWN_DECISION_SNAPSHOT_CONFIGURATION`，声明实际模型、提示词版本及生成参数。字段和示例见[快照配置说明](../../docs/verification/complete-agent-snapshots-2026-09-19.md#配置与旧档)。未声明时可以调用代理，但完整保存会拒绝该决策客户端；声明只记录非秘密配置，不填密钥或代理地址。读取快照要求声明匹配，当前不会向远程代理握手核验声明。

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

可设置 `COZYTOWN_SCENARIO_REPORT_PATH=Logs/agent-resource-preconditions-live.json` 为新版本指定新的报告路径，仍以创建新文件的方式拒绝覆盖。v4 资源场景已有自方条件及允许动作过滤；等待、拒绝属于模型在受限集合中的选择，不能计为不受约束的资源推理能力。

普通测试 `FreshScenes_SeparateReasonableChoicesFromRejectedTrades` 使用固定客户端验证重置、合理拒绝及宿主拦截的分类，不读取密钥。邀约或接受时的宿主拒绝记为 `host_rejected_resource_commitment`，交付时的资源错误记为 `host_rejected_infeasible_trade`；两者均与模型主动等待或拒绝分开。

整个矩阵的 Unity 测试总时限为 25 分钟，覆盖 12 个单轮期限与场景加载。若基础设施中断，先保留原 JSON、XML 和代理日志，再显式设置 `COZYTOWN_SCENARIO_START_ORDINAL=<1..12>` 从指定计划序号重新初始化执行余下轮次。起始序号大于 1 时写入独立的 `agent-resource-scenarios-live-from-<ordinal>.json`，同样拒绝覆盖。补跑不会恢复旧世界或旧会面；应继续使用原代理剩余预算，并在最终报告同时统计中断尝试与补跑，不能删去未完成样本。

每个代理进程最多尝试 `--max-calls` 次提供方请求，失败也计数，同时最多 2 个请求。代理不自行重试；宿主可以在原决策预算内请求一次字段纠正，见下一节。每次最多生成 512 Token、关闭思考模式。请求型号固定为用户选择的 `deepseek-v4-flash`。实际返回的型号、Token 用量、耗时、候选和错误码记录在本地 JSONL，密钥、完整上下文和提供方思考内容不写入记录。`GET /status` 返回本进程已尝试次数、剩余额度和在途数。

记录文件必须尚不存在，避免覆盖上一次证据。重启进程会创建新的调用预算；需要控制一次联调总量时，应扣除之前已经尝试的次数。停止代理使用 Ctrl+C。调用上限耗尽后，游戏继续按宿主日程运行，待处理会面仍受游戏时间期限约束。

## 候选校验与字段纠正

代理检查每项操作的必填顶层字段、字段类型和请求绑定，再筛选返回字段。缺字段时不从上下文代填，也不把嵌套参数提升到顶层。已解析候选不符合合同则返回 HTTP 422、`error: provider.candidate_invalid` 和固定 `candidateErrorCode`，痕迹中保留同一码；普通 JSON 解析失败或提供方故障沿用原失败路径。

Unity 宿主仅对已知必填字段问题允许最多一次纠正，在原 `decisionId` 下增加 `step`、发送非空 `candidateErrorCode`。纠正与按需地点查询共享预算，不增加总调用上限或延长期限。有效但错误的对象 ID、未允许动作、版本不匹配及运行失效不纠正。HTTP 客户端与代理每次仍只发起一个请求，具体字段见[协议](../../docs/NPC_AGENT_PROTOCOL.md#候选字段校验与一次纠正)。

场景中的 `candidateErrorCode` 与失败原文类型记录该次请求，`decisionOutcomeCode` 记录最终决策结果；纠正成功不会覆盖首次失败。`CandidateCorrection_PreservesTheRejectedAttemptAndUsesTheSameOpportunity` 使用固定 HTTP 422 响应覆盖四种初始化场景，不使用真实模型。

## 上下文对照实验

`grounding_experiment.py` 是独立评测入口，复用默认代理的提供方、系统提示词和候选处理。A 保留原上下文；B 加事实范围、未知项与表达指引；C 加自方资源条件；D 再过滤自方不能承担的动作。D 仍实际调用模型，其效果单列为系统限制，不能算作模型理解改善。默认游戏代理不自动启用这些处理。

复现 2026-09-13 的原 A/B/C/D 对照必须检出冻结实验提交 `d40fe70b7d59f7e714156471b51fc5f19b86fa85` 或结果提交 `49733ac4521863d55d56a6b7f3ece2dc5e39580f`。后续 v4 宿主已加入自方条件和动作过滤，当前代理还增加字段拒绝及纠正反馈指引；实验适配器不会撤销这些变化。在新版本上运行旧入口不能当作原对照实验的复测。当前实验入口保留代理拒绝，不会为了旧指标放行缺字段候选。v4 策略验证使用上面的独立多场景入口。

```powershell
python -B Tools/agent_proxy/grounding_experiment.py --mode fixed --credential-file '<private-credentials.json>' --corpus Tools/agent_proxy/grounding_corpus.json --output-dir 'Logs/grounding-fixed-new' --max-calls 160
python -B Tools/agent_proxy/grounding_experiment.py --mode serve --credential-file '<private-credentials.json>' --output-dir 'Logs/grounding-scene-new' --max-calls 192 --base-port 25700
```

两条命令分别运行，合计预算上限 352 次。固定语料包含 8 个有来源记录的请求，每个在四组各运行 5 次；组序轮转、不继承前次输出。语料中的反事实和独立发言探针不是新的 Unity 运行记录。提供方失败仍计费，不自动重试。

场景服务的 A/B/C/D 分别监听从 base port 开始的四个连续回环端口，共用 192 次预算。为 Unity 测试进程设置 `COZYTOWN_RUN_GROUNDING_EXPERIMENT=1`、`COZYTOWN_GROUNDING_BASE_PORT=<base-port>`，运行 `NpcResourceScenarioPlayModeTests.LiveProxy_ComparedGroundingArmsAcrossFreshScenarios`。16 轮覆盖四种初始资源配置和四组，每轮重建世界与双方状态。实际位置、路线和双方到场由测试观察后附加，A 会移除这部分实验元数据；伙伴坐标不进入模型。报告路径为 `Logs/agent-grounding-scene.json`，已存在时拒绝启动。

每个命令要求全新输出目录。`manifest.json` 保存参数和来源哈希，`status.json` 保存预算和在途数；`proxy.jsonl` 保存原代理度量，`contexts.jsonl` 保存原请求、实际模型上下文、过滤动作和参数格式诊断，`provider-responses.jsonl` 保存过滤前的候选正文，不包含密钥或提供方思考内容。以 `decisionId` 和 `step` 关联多步请求；格式诊断不代替 Unity 执行结果。停止服务前确认无在途请求，重启时扣除该实验已用预算。

完整分组、指标与限制见[运行前实验方案](../../docs/verification/npc-grounding-experiment-plan-2026-09-13.md)。

## NPC 表达对照实验

`expression_experiment.py` 为 F（自由台词）与 S（结构化事实句）提供两个回环端口，复用同一个提供方预算。实验代理按端口设置表达模式，同时保留 Unity 原请求；运行后须核对原请求与实际输入一致。场景中的同一世界绑定一个组别，跨组请求会被拒绝。固定和场景阶段各限 48 次提供方尝试，失败同样计数；场景阶段另限四个世界、每世界 12 次。默认游戏仍使用自由台词。

先启动固定阶段代理，在另一个终端运行已配置的 Unity 测试进程：

```powershell
python -B Tools/agent_proxy/expression_experiment.py --mode serve --stage fixed --credential-file '<private-credentials.json>' --corpus Tools/agent_proxy/expression_cases.json --output-dir 'Logs/expression-fixed-new' --base-port 25800
```

为 Unity 进程设置 `COZYTOWN_RUN_EXPRESSION_FIXED=1`、`COZYTOWN_EXPRESSION_BASE_PORT=25800`、`COZYTOWN_EXPRESSION_FIXED_REPORT_PATH=<new-report-file>`，运行 EditMode 测试 `NpcExpressionExperimentTests.LiveProxy_CompareEightFactCasesWithOneCallPerSample`。它从公开 Runtime 入口创建八个固定事实请求，每组各三次；只修改 `expression.mode`，每样本直接调用一次。固定输出经生产编解码与表达器评价，不等同于实际场景中的对话发布。48 个请求完成后代理自动退出。

核查固定阶段报告后，用新的输出目录启动场景代理：

```powershell
python -B Tools/agent_proxy/expression_experiment.py --mode serve --stage scene --credential-file '<private-credentials.json>' --corpus Tools/agent_proxy/expression_cases.json --output-dir 'Logs/expression-scene-new' --fixed-evidence-dir 'Logs/expression-fixed-new' --base-port 25800 --stop-file 'Logs/expression-scene-new.stop'
```

为 Unity 进程设置 `COZYTOWN_RUN_EXPRESSION_EXPERIMENT=1`、`COZYTOWN_EXPRESSION_BASE_PORT=25800`、`COZYTOWN_EXPRESSION_REPORT_PATH=<new-report-file>`，以图形 PlayMode 运行 `NpcResourceScenarioPlayModeTests.LiveProxy_ComparedExpressionModesAcrossFourFreshWorlds`，不加 `-quit`。四个新世界固定为资源充足 F/S、卖方缺鱼 S/F；每轮先核对初始化，再让原调度器驱动模型。

运行结束且没有在途请求后，创建 `--stop-file` 指定的文件或使用 Ctrl+C 正常停止代理。代理等待已开始的请求写完记录。新的输出目录及报告文件必须尚不存在；场景阶段验证固定阶段结束状态、日志用量及源码／提示词／情境哈希，并只允许领取一次剩余场景预算。不要通过重启、改路径或重置世界替换失败样本。

`manifest.json` 保存代码、提示词、情境和参数；`provider-starts.jsonl` 在实际网络调用前落盘；`provider-responses.jsonl` 保留未筛选的候选正文；`proxy.jsonl` 记录提供方型号、token、耗时和协议结果；`contexts.jsonl` 记录原请求、实际输入、组别及规范化哈希。固定重复使用同一决策时，以独立请求序号关联记录。日志不包含密钥或提供方思考内容。完整分组和评分分母见[运行前方案](../../docs/verification/npc-expression-comparison-plan-2026-09-14.md)。

只验证场景接线时，可以显式使用 `--stage scene --standalone-scene` 并省略 `--fixed-evidence-dir`。该入口记录 `standaloneScene: true`，每批仍限四个世界、每世界 12 次、合计 48 次；它不证明 fixed 阶段已完成。先登记批数和研究总预算，再为每批使用新的目录和停止文件。跨进程总预算由方案与证据核账，runner 只限制单批。完整示例：

```powershell
python -B Tools/agent_proxy/expression_experiment.py --stage scene --standalone-scene --credential-file '<private-credentials.json>' --corpus Tools/agent_proxy/expression_cases.json --output-dir 'Logs/expression-standalone-new' --base-port 25800 --stop-file 'Logs/expression-standalone-new.stop' --source-file '<registered-study-plan>'
```

Unity 入口及环境变量同上述场景阶段。默认冻结范围之外的测试文件通过重复 `--source-file` 加入；本轮两批登记及范围见[移动中答复方案](../../docs/verification/npc-moving-observation-plan-2026-09-14.md)。场景调用记录的 `executionObservationJson` 保存本次宿主执行前重读，提前取消时为 `null`；它与原 `contextJson` 分开，不替换模型已收到的事实。

## 四人跨日与读档矩阵

`run_four_agent_integration.py` 与 Unity 的 `AgentIntegrationMatrixPlayModeTests.CoordinatedMatrix_ExecutesOnlyRegisteredFreshWorlds` 配合执行四种资源初态、两次 F/S 配对，共 16 个新世界。协调器先登记清单，每世界初始化通过后才授权模型调用；每世界最多 32 次提供方尝试、600 现实秒，全批最多 512 次。失败尝试计入账本，已有输出目录不能覆盖或续跑。

固定阶段不需要凭据。Live 使用同一 Python 进程依次创建独立代理，以全批账本约束总量；它与手动窗口共用单个代理的使用方式分别记账。完成包在新场景严格回放，初始化、宿主或回放失败会停止后续世界。

冻结版本、启动命令、时间脚本及结果字段见[四人集成验收计划](../../docs/verification/four-agent-integration-plan-2026-09-20.md)。交互式入口和实验包解释见[操作指南](../../docs/AGENT_EXPERIMENT_GUIDE.md)。固定、回放与真实模型的结果分开统计。

## 验证

```powershell
python -B -m unittest discover -s Tools/agent_proxy -p 'test_*.py'
```

这些测试使用假的提供方或本机 HTTP 服务，不读取真实密钥，不消耗模型额度。

协议和游戏运行规则见 [NPC 自主决策协议](../../docs/NPC_AGENT_PROTOCOL.md) 与 [会面 ADR](../../docs/adr/0017-resident-meeting-commitments.md)。
