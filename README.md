# CozyTown

CozyTown 是一个用于测试 AI Agent 在游戏场景中实际落地的 Unity 2D Demo。

项目提供了一套可运行、可存档、可自动化验证的小镇生活循环：玩家可以种植、养鸡、钓鱼、烹饪、交易，并与场景中的 NPC 对话。当前 AI 接入从 NPC 对话开始，模型只生成候选文本和表现标签；金币、物品、时间、生产进度与存档仍由确定性游戏代码维护。

项目仍在持续开发。

<img width="1700" height="966" alt="image" src="https://github.com/user-attachments/assets/c89afe68-34fd-4315-bc9d-3fa93dad01de" />


## 这个 Demo 验证什么

- **有限上下文**：Agent 只接收 NPC 身份、人设、游戏时间和经过筛选的只读状态。
- **结构化响应**：代理返回对话文本、情绪标签和动作标签，客户端在展示前执行格式与允许列表校验。
- **故障可降级**：超时、网络错误、服务异常或非法响应会切换到对应 NPC 的固定文本，交互流程可以继续。
- **状态有边界**：AI 适配器不持有钱包、背包、时间、农田、畜牧或存档的写接口。
- **测试不依赖线上模型**：领域规则、AI 回退和场景接线可以使用固定实现或测试替身运行。

当前实现是一条受约束的 NPC 对话链路，还不是具备自主规划和工具调用能力的完整 Agent。后续实验会在现有权限边界内增加上下文、评测和诊断能力。

## 当前可玩内容

- 32×22 单场景小镇、北侧四户 NPC 灰盒住宅与像素跟随相机
- 种植、浇水、收获、养鸡、钓鱼和烹饪
- 商店购买、出售与再次投入的经济闭环
- 五格快捷栏、只读背包和交互面板
- Mina、Eli、Ren、Sora 四名独立 NPC
- 单槽位本地存档，覆盖时间、金币、背包、农田和畜牧状态
- 固定 NPC 对话，以及可选的 HTTP(S) AI 代理对话

T1-1 已接入住宅归属和可达道路；四名 NPC 当前仍在原职业地点静态站立。自动走时、按日程往返、差异化住宅美术和 NPC 行走帧属于 T1-2～T1-4，尚未实现。扩大版 Scene-01/Town-01 人工验收通过前，保持真实 AI 端点关闭。

## AI 接入边界

```text
玩家与 NPC 交互
    -> NpcDialogueCoordinator 生成只读上下文
    -> HTTP(S) 代理请求模型
    -> 客户端解析并校验候选响应
    -> UI 展示对话与表现标签
             \
              -> 超时或校验失败时返回固定文本
```

Unity 客户端只访问代理端点，不保存模型服务密钥。代理响应使用以下结构：

```json
{
  "text": "今天的风很适合去池塘边走走。",
  "emotion": "happy",
  "action": "smile"
}
```

`text` 最长 500 个字符；`emotion` 必须是 `neutral`、`happy`、`concerned`、`excited` 或 `thoughtful`；`action` 可省略，也可以是 `idle`、`nod`、`wave` 或 `smile`。响应不会直接转换为游戏状态变更。

## 运行项目

### 环境

- Unity Editor `6000.5.5f1`
- Universal Render Pipeline 2D
- Input System `1.19.0`
- Git LFS

Unity 包版本以 [`Packages/manifest.json`](Packages/manifest.json) 为准，Editor 版本以 [`ProjectSettings/ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt) 为准。

### 启动

1. 克隆仓库，并确认 Git LFS 已拉取 PNG 等二进制资源。
2. 在 Unity Hub 中选择 **Add project from disk**，打开仓库根目录。
3. 等待 Package Manager 完成依赖解析和脚本编译。
4. 打开 [`Assets/CozyTown/Scenes/CozyTown_Dev.unity`](Assets/CozyTown/Scenes/CozyTown_Dev.unity)。
5. 进入 Play Mode。

已提交的场景包含住宅街，向北移动即可查看。只有需要重建标准世界布局时才使用非 Play 模式下的 `CozyTown > Upgrade Development Scene for T1 Town Life`；该菜单会按标准布局重设世界地标、住宅、道路和边界，不用于保留自定义场景布局。

### 操作

| 输入 | 行为 |
| --- | --- |
| `WASD` / 方向键 | 移动 |
| `E` | 与附近的建筑、NPC、农田或池塘交互 |
| `B` | 打开或关闭背包 |
| `1`–`5` | 选择快捷栏槽位 |
| 右上角齿轮 | 打开保存、读取和系统菜单 |

可以按以下路线检查完整闭环：在商店购买种子、饲料和盐；完成播种、浇水、喂鸡与钓鱼；睡眠推进日期；收获作物和鸡蛋；在厨房制作料理；出售产物后再次购买生产资料。

## 使用 AI 代理

场景中的 `CozyTownBootstrap` 默认将 **Ai Proxy Endpoint** 留空，因此 NPC 使用固定对话。运行时优先读取以下进程环境变量，未设置时才使用 Inspector 中的值：

| 环境变量 | 说明 |
| --- | --- |
| `COZYTOWN_AI_PROXY_ENDPOINT` | 绝对 HTTP(S) 代理地址；留空时使用固定 NPC 对话 |
| `COZYTOWN_AI_PROXY_TIMEOUT_SECONDS` | 请求超时秒数，必须不小于 `0.1`；默认值为 `8` |

PowerShell 示例：

```powershell
$env:COZYTOWN_AI_PROXY_ENDPOINT = 'https://<proxy-host>/npc-dialogue'
$env:COZYTOWN_AI_PROXY_TIMEOUT_SECONDS = '8'
```

设置变量后，从继承这些变量的进程启动 Unity Editor 或构建。仓库根目录的 [`.env.example`](.env.example) 只提供变量名和值格式，项目不会自动加载 `.env` 文件。

接入模型服务时：

1. 准备一个接收 JSON `POST` 请求的 HTTP(S) 代理。
2. 由代理持有模型服务凭据并返回上文所示的响应结构。
3. 通过进程环境变量配置代理地址和超时。
4. 保持被 Git 跟踪的开发场景不含环境专用地址和模型服务凭据。

请求字段包括 `npcId`、`displayName`、`persona`、`day`、`minuteOfDay`、`affinity`、`recentActivities` 和 `memories`。字段定义与序列化实现见 [`Assets/CozyTown/Unity/Npc/ProxyNpcDialogueJsonCodec.cs`](Assets/CozyTown/Unity/Npc/ProxyNpcDialogueJsonCodec.cs)。

## 测试

在 Unity Editor 中打开 **Window > General > Test Runner**：

- EditMode 覆盖领域规则、应用协调、存档、AI 响应校验和 Unity 场景契约。
- PlayMode 覆盖角色移动、物理交互、输入生命周期、UI 接线和完整经济闭环。
- AI 相关自动化使用固定实现或测试替身，不访问计费模型服务。

测试程序集、批处理命令和验收范围见 [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md)。

## 代码结构

```text
Assets/CozyTown/Runtime/          领域模块、应用用例、公共接口和组合根
Assets/CozyTown/Unity/            Unity 生命周期、输入、场景表现和外部适配器
Assets/CozyTown/Scenes/           可运行的开发场景
Assets/CozyTown/Art/              游戏内美术资源
Assets/CozyTown/Tests/EditMode/   纯 C# 与应用层测试
Assets/CozyTown/Tests/UnityEditMode/
                                 Unity 适配与场景契约测试
Assets/CozyTown/Tests/PlayMode/   运行时场景和交互测试
ArtSource/                        美术源文件与预览
docs/                             产品、架构、测试和决策记录
```

`CozyTown.Runtime` 不引用 `UnityEngine`。Unity 组件通过窄接口调用应用用例，`CozyTownCompositionRoot` 负责装配对象图；场景脚本不直接取得完整服务集合。详细边界见 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。

## 当前阶段

游戏闭环、存档、受约束 AI 对话边界和自动化测试已经接入开发场景。默认配置仍使用固定 NPC 对话，真实模型服务、离线评测结果、延迟与成本诊断尚未作为仓库基线启用。

接下来的开发内容：

1. 完成开发场景的人工画面与交互验收。
2. 接入真实代理服务并运行不少于 30 条离线对话评测。
3. 记录结构有效率、回退原因、延迟和调用成本。
4. 生成 Windows 演示构建并录制完整玩法链路。

## 相关文档

- [`docs/PRD.md`](docs/PRD.md)：产品范围、用例和验收条件
- [`docs/TOWN_LIFE_PLAN.md`](docs/TOWN_LIFE_PLAN.md)：T1 扩镇与日常规划；T1-1 灰盒场景已实现，作息切片待实施
- [`CONTEXT.md`](CONTEXT.md)：领域词汇
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)：模块边界与依赖规则
- [`docs/TEST_PLAN.md`](docs/TEST_PLAN.md)：测试矩阵与人工验证步骤
- [`docs/ART_DIRECTION.md`](docs/ART_DIRECTION.md)：像素美术方向
- [`docs/adr`](docs/adr)：架构决策记录
