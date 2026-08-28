# CozyTown

CozyTown 是一个 Unity 2D 小镇生活模拟项目。MVP 由种植、养鸡、钓鱼、商店交易、背包、烹饪、天数推进、单存档和 NPC 对话组成。NPC 可以通过受约束的 AI 适配器生成对话；金币、物品、时间和生产状态只由确定性游戏代码修改。

## 当前状态

M2“可行走小镇切片”已经完成，M3“生产经济闭环”尚未开始。项目当前提供默认 MVP 内容、跨日事务协调器、Unity Bootstrap、Input System 移动、Physics2D 碰撞、交互提示，以及商店、NPC、床和农田四类浅交互点。四类交互目前只显示提示与触发反馈，不修改金币、背包、时间或生产状态；生产经济 UI、正式美术、磁盘存档适配器和线上 AI 服务仍未实现。

`Assets/CozyTown/Runtime` 包含 45 个 C# 文件和 11 个公共服务接口。默认配置包含 3 种作物、3 种鱼、5 个料理配方、1 只鸡和 4 名 NPC；组合根通过 `CreateDefault()` 创建同一对象图。`CozyTown.Runtime` 设置 `noEngineReferences: true`，不引用 UnityEngine。Unity 组件位于独立的 `CozyTown.Unity` 程序集，Bootstrap 私有持有完整对象图，只向 HUD 等场景组件注入所需的窄接口。

Unity Editor `6000.5.5f1` 已完成包解析、资源导入和六个 CozyTown 程序集的脚本编译。2026-08-28 的隔离批处理运行得到 57 passed、0 failed、0 skipped 的 EditMode 结果，以及 8 passed、0 failed、0 skipped 的 PlayMode 结果；日志未出现 C# 编译错误、测试失败、运行态装配错误或许可证错误。

产品范围和验收条件见 [PRD](docs/PRD.md)，模块边界和依赖规则见 [架构说明](docs/ARCHITECTURE.md)，组件与集成用例见 [测试计划](docs/TEST_PLAN.md)。架构决策记录位于 [`docs/adr`](docs/adr)。

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
Assets/CozyTown/Unity/         Unity 生命周期、输入、移动、交互和 HUD 适配
Assets/CozyTown/Scenes/        M2 开发场景
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

打开 `Assets/CozyTown/Scenes/CozyTown_Dev.unity` 运行 M2 切片。使用 WASD 或方向键移动，靠近彩色交互点后按 E；HUD 会显示当前提示和最近一次触发反馈。Editor 菜单 **CozyTown > Create Development Scene** 只在固定路径不存在时创建场景，不覆盖已有资产；若当前活动场景尚未保存，菜单会先要求保存，避免丢失编辑内容。

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

## 当前不包含

MVP 不包含战斗、野外地图、季节、天气、婚恋、建筑升级、多人同步和复杂 NPC 日程。完整范围边界记录在 [PRD](docs/PRD.md#范围)。
