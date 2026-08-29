# CozyTown

CozyTown 是一个 Unity 2D 小镇生活模拟项目。MVP 由种植、养鸡、钓鱼、商店交易、背包、烹饪、天数推进、单存档和 NPC 对话组成。NPC 可以通过受约束的 AI 适配器生成对话；金币、物品、时间和生产状态只由确定性游戏代码修改。

## 当前状态

M4“持久化与 AI”已经完成。玩家可在单一小镇场景中完成生产经济闭环，并通过右上角调试面板保存或读取一个本地存档。存档覆盖时间、金币、背包、农田和畜牧状态，使用 schema v1 JSON；写入先生成并验证临时文件，再替换正式槽位，损坏内容、缺失存档和不支持版本会返回不同错误。NPC 交互点可选择 4 名 NPC；固定对话始终可用，也可通过不含客户端密钥的 HTTP(S) 代理适配器请求 AI 对话。超时、传输异常、空文本、结构错误或非法标签都会返回对应 NPC 的固定文本。

纯像素美术已完成 A0 参考与首批 A1 Production 资源。A1 包含 11 个 PNG、98 个命名 Sprite 和 11 个严格 4× 最近邻预览，覆盖小镇地面与装饰、4 个功能建筑、农田与池塘、作物和母鸡状态、玩家四向移动、NPC、18 个物品及 UI；正式场景尚未替换占位图形。

默认配置包含 3 种作物、3 种鱼、5 个料理配方、1 只鸡和 4 名 NPC；组合根通过 `CreateDefault()` 创建同一对象图。`CozyTown.Runtime` 设置 `noEngineReferences: true`，不引用 UnityEngine。Unity 组件位于独立的 `CozyTown.Unity` 程序集；Bootstrap 私有持有完整对象图，并只向各 Presenter 注入对应的交易、生产、跨日、对话或存档用例接口。

Unity Editor `6000.5.5f1` 已完成包解析、资源导入和六个 CozyTown 程序集的脚本编译。2026-08-29 的 A1 批处理运行得到 158 passed、0 failed、0 skipped 的 EditMode 结果，以及 26 passed、0 failed、0 skipped 的 PlayMode 结果；日志未出现 C# 编译错误、测试失败、未处理异常或运行态装配错误。批处理测试使用内存存档，不读写玩家的正式槽位。

产品范围和验收条件见 [PRD](docs/PRD.md)，领域词汇见 [CONTEXT](CONTEXT.md)，模块边界和依赖规则见 [架构说明](docs/ARCHITECTURE.md)，组件与集成用例见 [测试计划](docs/TEST_PLAN.md)。纯像素规格、准入条件和 A0 生成记录分别见 [美术方向](docs/ART_DIRECTION.md)、[美术验收](docs/ART_ACCEPTANCE.md) 和 [生成记录](docs/ART_GENERATION_LOG.md)。架构决策记录位于 [`docs/adr`](docs/adr)。

## 技术基线

- Unity Editor `6000.5.5f1`
- Universal Render Pipeline 2D
- Unity 6000.5 兼容 2D 包：Animation `15.1.0`、PSD Importer `14.0.3`、SpriteShape `15.0.3`、Tilemap Extras `8.0.3`、Aseprite `5.0.3`、2D Tooling `3.0.1`
- Input System `1.19.0`
- Unity Test Framework：`manifest.json` 请求 `1.4.5`，Unity `6000.5.5f1` 实际解析内置 `1.7.0`
- C# Runtime 程序集：`CozyTown.Runtime`
- Unity 适配程序集：`CozyTown.Unity`、`CozyTown.Unity.Editor`
- EditMode 测试程序集：`CozyTown.Tests.EditMode`、`CozyTown.Tests.UnityEditMode`
- PlayMode 测试程序集：`CozyTown.Tests.PlayMode`

版本以 `ProjectSettings/ProjectVersion.txt` 和 `Packages/manifest.json` 为准。

## 仓库结构

```text
Assets/CozyTown/Runtime/       游戏模块、公共接口和组合根
Assets/CozyTown/Unity/         Unity 生命周期、输入、移动、交互、HUD 和生产玩法适配
Assets/CozyTown/Scenes/        M2-M4 开发场景
Assets/CozyTown/Art/           A0 风格参考与验收后的生产美术
Assets/CozyTown/Tests/EditMode/纯 C# 组件与模块协作测试
Assets/CozyTown/Tests/UnityEditMode/Unity 适配层 EditMode 测试
Assets/CozyTown/Tests/PlayMode/场景、物理和交互生命周期测试
docs/                          PRD、架构说明和 ADR
Packages/                      Unity 包清单
ProjectSettings/               Unity 项目设置
```

Runtime 按 `Application`、`Content`、`Core`、`Time`、`Inventory`、`Economy`、`Farming`、`Livestock`、`Fishing`、`Cooking`、`Npc` 和 `Save` 分区。`Core/CozyTownCompositionRoot.cs` 通过 `CreateDefault()`、`Create(configuration)` 或 `CreateEmpty()` 创建实现。Unity 场景脚本不得取得完整 `CozyTownServices`；Bootstrap 负责把用例所需接口显式注入适配组件。

## 打开项目

1. 在 Unity Hub 中选择 **Add project from disk**。
2. 选择仓库根目录。
3. 使用 Unity `6000.5.5f1` 打开项目。
4. 等待 Package Manager 导入依赖并完成脚本编译。

打开 `Assets/CozyTown/Scenes/CozyTown_Dev.unity` 运行当前切片。使用 WASD 或方向键移动，靠近功能色块后按 E 打开对应面板；按钮执行一次操作，点击 **Close** 返回移动。可按以下路线验证完整闭环：商店购买土豆种子、鸡饲料和两份盐；在农田播种并浇水；在鸡舍喂鸡；在池塘钓鱼；睡到第 2 天后收鸡蛋并再次浇水；睡到第 3 天后收获；在厨房制作烤土豆和烤鱼；回商店出售剩余土豆、两份料理和鸡蛋，再购买一份土豆种子。

右上角 **Save / Load** 面板操作逻辑槽位 `main`；默认 Bootstrap 在常规 Editor Play 和构建中写入 `<Application.persistentDataPath>/CozyTown/main.json`。靠近蓝色 NPC 点按 E 可在 4 名 NPC 之间选择并请求对话。Bootstrap 的 **Ai Proxy Endpoint** 默认留空，因此使用固定回退；要联调线上模型，应填入由开发者控制的绝对 HTTP(S) 代理地址。代理成功响应为 `{"text":"...","emotion":"...","action":"..."}`，客户端不保存模型服务密钥。

Editor 菜单 **CozyTown > Create Development Scene** 只在固定路径不存在时创建场景，不覆盖已有资产；创建前若当前活动场景尚未保存，菜单会先要求保存。**CozyTown > Upgrade Development Scene for M4** 可重复执行 M4 接线；执行前应先保存正在编辑的其他场景。

## 运行测试

在 Unity Editor 中打开 **Window > General > Test Runner**，分别运行 EditMode 下的 `CozyTown.Tests.EditMode`、`CozyTown.Tests.UnityEditMode`，以及 PlayMode 下的 `CozyTown.Tests.PlayMode`。覆盖范围见 [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md)。

批处理命令模板：

```powershell
& '<unity-editor>/Editor/Unity.exe' `
  -batchmode `
  -nographics `
  -projectPath <project-root> `
  -runTests `
  -testPlatform EditMode `
  -testResults <project-root>/Logs/EditModeTests.xml `
  -logFile <project-root>/Logs/EditModeTests.log
```

运行 PlayMode 时，将 `-testPlatform` 改为 `PlayMode`，并使用 `PlayModeTests.xml`、`PlayModeTests.log`，避免覆盖 EditMode 结果。

命令不附加 `-quit`，由 Unity Test Framework 在测试完成后结束进程；提前传入 `-quit` 可能在测试运行器生成 XML 前关闭 Editor。验证必须同时检查本次生成的 XML 和日志，不能只用 PowerShell 的 `$LASTEXITCODE` 判断测试通过。

## 开发约束

- 通过模块接口协作，不从场景脚本直接修改其他模块的内部集合或状态。
- 新的运行时依赖在 `CozyTownCompositionRoot` 中显式装配，不使用全局静态服务定位器。
- 物品、作物、鱼、配方和 NPC 使用稳定 ID；显示名称不作为存档键或逻辑分支条件。
- AI 输出只作为对话候选数据。AI 适配器不得持有 `IWallet`、`IInventory`、时间、农田或存档写接口。
- 默认测试使用固定实现或测试替身，不调用计费或联网的模型服务。
- M4 只实现 AI 请求边界、结构校验和固定回退；30 条离线评测、延迟/成本汇总、Windows 构建与录屏属于 M5。

## 当前不包含

MVP 不包含战斗、野外地图、季节、天气、婚恋、建筑升级、多人同步和复杂 NPC 日程。完整范围边界记录在 [PRD](docs/PRD.md#范围)。
