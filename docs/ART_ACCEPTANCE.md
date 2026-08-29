# CozyTown 纯像素美术验收标准

## 1. 验收范围

本标准覆盖纯像素资源从 A0 风格锚点到 A1 Unity 生产资源的准入。自动化检查验证文件、Sprite 布局和 Unity 导入结果；人工检查验证像素画的可读性、一致性和原创性。自动化通过不能替代视觉验收。

## 2. TDD 测试缝隙

本阶段只在以下公开缝隙编写自动化测试：

1. Unity 将 `Assets/CozyTown/Art/Production/` 下的 PNG 导入后，通过 `AssetImporter.GetAtPath` 可观察到统一的纯像素设置。
2. A0 像素管线探针通过 `ImageConversion.LoadImage` 和 `AssetDatabase.LoadAssetAtPath` 可观察到源 PNG 尺寸、Alpha、颜色数及实际 Sprite 输出。
3. 生产清单声明 Sprite Sheet 后，通过 `ImageConversion.LoadImage`、`AssetImporter.GetAtPath` 和 `AssetDatabase.LoadAllAssetsAtPath` 可观察到真实 PNG 画布、二值 Alpha、导入参数、切片数量、PPU、Pivot 和名称。
4. 每张生产 PNG 的 4× 预览必须与原图逐像素最近邻扩展结果一致。
5. 正式场景开始消费生产资源后，通过场景中的公开 Camera、SpriteRenderer 和 Tilemap 组件验证引用及 Pixel Perfect Camera。
6. Scene-01c 通过公开的 UGUI `Canvas`、`CanvasScaler`、`Image`、`Button`、`Text` 组件、现有 View 接口和 `Button.onClick` 验证 Production UI 引用与交互接线。

测试不得读取 `.meta` YAML、断言 AssetPostprocessor 私有方法、调用 `OnGUI` 或读取 UI 私有字段。A0 风格锚点不进入 Sprite 布局测试，因为它们不是生产资源。

## 3. A0 自动检查

- 五个约定文件全部存在于 `Assets/CozyTown/Art/References/A0/`。
- 文件为 PNG，能够被 Unity 导入并读取。
- 文件名与 `ART_DIRECTION.md` 一致，不以显示名称替代稳定资源名。
- A0 不被正式场景、Prefab 或运行时代码引用。

## 4. A0 人工验收

| 编号 | 检查项 | 通过条件 |
| --- | --- | --- |
| ART-A0-01 | 纯像素媒介 | 边缘由一致大小的硬像素块组成；没有抗锯齿、模糊、景深或体积光 |
| ART-A0-02 | 视角与光向 | 所有场景与角色保持 3/4 俯视；主要光源来自左上方 |
| ART-A0-03 | 地标可读性 | 商店、床/住宅、农田、鸡舍、池塘和厨房不看文字也能区分 |
| ART-A0-04 | 角色区分 | 玩家与 4 名 NPC 在轮廓、发色或服装中至少有两项稳定差异 |
| ART-A0-05 | 玩法边界 | 图片只表现当前 MVP，不出现野外、战斗、季节或新增可交互系统 |
| ART-A0-06 | 风格一致 | 五张锚点的像素密度、描边、饱和度和材质处理没有明显批次漂移 |
| ART-A0-07 | 原创性 | 不包含现有游戏的可识别角色、地图、建筑、Logo 或图标复制 |
| ART-A0-08 | 小尺寸可读 | 角色、地标和代表图标在目标原生尺寸或整数倍显示时仍可辨认 |

任一检查不通过时，只针对失败项重新生成或编辑。A0 原有软渐变缺陷继续记录，但根据 2026-08-29 的范围决策，不再阻断满足 A1 硬技术门禁的全套资源批次。

## 5. A1 Unity 导入验收

`Assets/CozyTown/Art/Production/` 下的 PNG 必须满足：

- Texture Type：Sprite。
- Sprite Mode：按资产清单声明为 Single 或 Multiple。
- Pixels Per Unit：`16`。
- Filter Mode：Point。
- Compression：None / Uncompressed。
- Generate Mip Maps：Off。
- Wrap Mode：Clamp。
- Non Power of 2：None。
- sRGB：On。
- Alpha Is Transparency：On。

生产 Sprite 的画布尺寸必须是 `16 px` 网格的整数倍。角色帧为 `16×24 px`，脚底中心 Pivot 保持一致；物品图标为 `16×16 px`。所有 Sprite 名称使用稳定 ID 的资源形式，例如 `npc_shopkeeper_mina_idle_down`、`item_seed_potato` 和 `crop_tomato_stage_04`。

## 6. A1 视觉与动画验收

- 草地、道路、土地和水面以 `3×3` 重复铺设时没有可见接缝。
- 透明边缘没有白边、暗边、棋盘格背景或半透明噪点。
- 玩家、NPC、建筑门口、成熟作物和交互提示在相邻背景上保持清楚轮廓。
- 动画各帧的身体高度、头部大小、光向和脚底位置不跳动。
- 原生尺寸及 `2×`、`4×` 整数放大时像素边缘清楚；非整数缩放不作为发布显示方式。
- Unity 导入、场景加载和对应 EditMode/PlayMode 测试没有编译错误、丢失引用或运行时异常。

## 7. 红—绿循环

| 循环 | Red | Green | 后续验收 |
| --- | --- | --- | --- |
| Import-01 | 测试纹理仍以 Bilinear、压缩或错误 PPU 导入 | Production 目录的导入策略统一为 Point、无压缩、16 PPU、无 mipmap | Reviewer 检查测试只通过公开 Importer 结果 |
| Anchor-01 | A0 清单缺少一个或多个锚点 | 五个 A0 PNG 均存在且可导入 | 人工执行 ART-A0-01 至 ART-A0-08 |
| Probe-01 | A0 胡萝卜探针不存在，导入契约测试失败 | 源稿经确定性工具编译为 16×16、二值 Alpha、最多 8 色的 Single Sprite | Reviewer 检查 1×/4×可读性、光向和原创性 |
| Batch-01 | 11 个 Production PNG 均不存在，清单测试列出全部缺失项 | 声明式编译器生成 11 个 PNG、98 个 Sprite 与 11 个 4× 预览，清单测试通过 | Reviewer 按角色、世界、物品、状态和 UI 批次检查可读性 |
| Sprite-01 | 首个角色或 Tile Sheet 的尺寸、Pivot、切片或名称不符合清单 | 对应生产资源通过布局测试 | 在测试场景检查轮廓、接缝和脚底稳定性 |
| Scene-01 | 正式场景仍引用占位方块或缺少 Pixel Perfect Camera | 场景引用验收后的 Production 资源并使用整数像素显示 | PlayMode 验证移动、交互和 UI 不回归 |

先固定清单和可观察契约，再生成对应批次；不得在资源生成后补写更宽松的测试，也不得为了让测试通过而降低像素规格。

### 7.1 Scene-01 场景接线验收

Scene-01 按三个垂直切片验收，任一切片未通过时不得把正式场景标记为完成：

1. **Scene-01a 世界表现**
   - Main Camera 具有 URP `PixelPerfectCamera`，`assetsPPU=16`、参考分辨率 `320×180`，使用点采样整数放大。
   - 小镇地面与道路由 Tilemap 消费 `tile_town_base_16.png`；至少一个 `3×3` 草地重复区和相邻道路组合可见且无接缝。
   - 商店、住宅、厨房、鸡舍、农田、池塘、玩家和世界 NPC 引用对应 Production Sprite；这些对象不再引用 Unity 内置 `UISprite`。
   - 7 个 `TownInteractionPoint2D`、提示文本、触发器与碰撞边界数量保持不变；视觉对象不取得任何领域写接口。
2. **Scene-01b 运行时状态表现**
   - 玩家根据真实移动方向显示四方向静止帧，并在移动时循环对应两帧步行动画；停止后回到最后方向的静止帧。
   - 6 个农田位置把空地、浇水和既有作物成长进度映射到清单中的状态 Sprite。
   - 母鸡把未喂、已喂和产物可收映射到三个既有状态 Sprite；映射只读取现有 ViewState。
3. **Scene-01c UI 皮肤**
   - 正式界面使用 UGUI `Canvas`；`CanvasScaler` 的参考分辨率为 `320×180`。`OnGUI` 调试界面不得作为本切片的验收对象。
   - 正式 HUD、模态面板和按钮消费 `ui_panel` 与四个按钮状态的 `3 px` 九宫格资源；金币、时间、保存、读取、关闭和交互提示使用对应图标。
   - 文字由 Unity 字体渲染；在 `320×180` 参考分辨率及 `2×`、`4×` 整数倍窗口中不裁切、不重叠，按钮仍可操作。

自动化停止线：EditMode 与 PlayMode 全量测试通过，Production UI 引用、文字值、按钮状态与事件、关键布局边界、模态输入恢复和升级幂等均有通过记录，且场景无丢失引用或运行时异常。自动化通过不判定文字肉眼可读性、图标辨识、Tile 接缝、玩家脚底稳定或整体构图。

最终退出条件：人工在 `320×180`、`640×360`、`1280×720` 画面中完成 Tile 接缝、玩家脚底稳定、功能物件辨识、农田和母鸡状态、UI 可读性与按钮可操作性检查。Scene-01 人工验收通过前保持固定 NPC 对话和故障替身，不接入真实 AI 驱动 NPC，也不开始 AI NPC 评测。

## 8. 当前状态

| 项目 | 状态 |
| --- | --- |
| 纯像素方向与规格 | 已锁定 |
| A0 五张风格锚点 | 已生成；自动检查 2/2 通过，人工检查仍有软渐变问题 |
| A0 胡萝卜像素管线探针 | 自动检查 1/1 通过；人工复核通过 |
| A1 Production 清单测试 | Red 精确报告 11 个缺失文件；Green 1/1 通过 |
| A1 可切片资源 | 11 个 PNG、98 个 Sprite、11 个 4× 预览已生成并完成批次目视检查 |
| 正式场景替换 | Scene-01a 世界表现与 Scene-01b 状态表现已完成；UI 与最终人工验收待完成 |

## 9. A0 审查记录（2026-08-29）

| 编号 | 结果 | 证据 |
| --- | --- | --- |
| ART-A0-01 | 未通过 | 五张图仍包含连续渐变和软色阶，像素块大小不一致 |
| ART-A0-02 | 通过 | 小镇和环境保持 3/4 俯视，主要高光在左上方 |
| ART-A0-03 | 通过 | 商店、住宅/床、农田、鸡舍、池塘和厨房不依赖文字即可区分 |
| ART-A0-04 | 通过 | 玩家与 4 名 NPC 在发型、帽型、服装颜色和职业轮廓上可区分 |
| ART-A0-05 | 通过 | 未出现战斗、野外、季节、天气或新增交互系统 |
| ART-A0-06 | 未通过 | 城镇、人物、UI 和调色板的模拟像素密度、描边和材质处理存在批次漂移 |
| ART-A0-07 | 通过 | 视觉检查未发现现有游戏角色、Logo、地图或建筑的可识别复制；该结论不是法律审计 |
| ART-A0-08 | 未通过 | 镇景不是 `320×180` 的整数倍，人物和图标未提供真实 `16×24`、`16×16` 原生样张 |

五张图均为 RGB，不含 Alpha；人物与 UI 图中的棋盘格是烘焙背景。它们继续只作为构图、角色轮廓和暖色关系参考，不得缩小后直接复制到 `Production`。A1 使用独立源稿与确定性编译器解决真实原生尺寸、锁色和硬透明问题。

## 10. A0 胡萝卜像素探针记录（2026-08-29）

`a0_item_crop_carrot.png` 是 `crop.carrot` 的单图标管线探针。自动检查验证了源 PNG 与 Unity Sprite 的 `16×16 px` 尺寸、中心 Pivot、16 PPU、Point、无压缩、无 mipmap、Clamp、Full Rect、真实 Alpha、二值 Alpha 和最多 8 个不透明颜色。

人工复核结果：非透明像素 `67`，不透明颜色 `7`，Alpha 取值为 `0` 和 `255`，`64×64 px` 预览与 4 倍最近邻结果完全一致；1× 可读性、硬像素边缘、左上光向、色板关系和原创性均通过。本结论只批准该图标作为后续物品图标的像素语言基准；A1 使用独立清单和批次门禁。

## 11. A1 全套资源验收记录（2026-08-29）

- 范围：`docs/ART_ASSET_MANIFEST.md` 中 11 个 PNG、98 个命名 Sprite，未增加玩法对象。
- TDD RED：`Logs/A1ProductionManifestCurrentRedTests.xml` 为 1 failed，失败内容仅为 11 个 Production PNG 缺失。
- TDD GREEN：`Logs/A1ProductionManifestGreenTests.xml` 为 1 passed；A0 回归 1 passed。
- 第二轮 RED：道路连接、UI 九宫格 border 和玩家脚底基线 `3/3` 精确失败；修复后目标测试 `3/3` 通过。
- 全量回归：EditMode `158/158`、PlayMode `26/26` 通过；日志中未发现 C# 编译错误、测试失败、未处理异常或 Bootstrap 初始化失败。
- 自动门禁：真实 PNG 尺寸、二值 Alpha、全不透明 Tile、32 色成员、道路连接、BottomCenter 脚底基线、UI border、Sprite 类型、16 PPU、Point、无压缩、无 mipmap、Clamp、Full Rect、切片数量/名称/Rect/Pivot，以及 4× 最近邻预览均通过。
- 目视门禁：建筑、农田、池塘、人物方向、4 名 NPC、18 个物品、作物成长、母鸡三态和 12 个 UI 图标均可辨识；细节润色不阻断本批次。
- 本次资源批次验收当时未覆盖正式场景引用、Tilemap `3×3` 实铺视觉接缝、运行时动画播放和 Pixel Perfect Camera；Scene-01a/01b 的后续接线结果见第 12 节，最终人工接缝检查仍待完成。

## 12. Scene-01a/01b 接线记录（2026-08-29）

- Scene-01a：正式场景已使用 Tilemap、7 个 Production 功能点、Production 玩家与世界 NPC；Main Camera 使用 `assetsPPU=16`、`320×180` 的 URP Pixel Perfect Camera。
- Scene-01b：玩家根据真实 Rigidbody2D 速度和最后方向切换四方向 idle/walk；6 个农田位置使用独立土壤与作物叠层；母鸡显示 idle/fed/product-ready。
- 权限边界：农田和母鸡世界视图只接收 `FarmViewState` 与 `LivestockViewState`；未增加领域写接口、玩法对象或 AI 接口。
- TDD：Scene-01a RED 命中缺少 Pixel Perfect Camera；玩家、农田和母鸡 RED 分别命中静态 idle、缺少 Farm States 和缺少 Hen State。对应 GREEN 均进入正式场景测试。
- 自动回归：EditMode `159/159`、PlayMode `26/26` 通过；日志无 C# 编译错误、测试失败、未处理异常或 Bootstrap 初始化失败。
- 升级幂等：连续两次执行 A1 场景升级后，`CozyTown_Dev.unity` 的两次 SHA-256 比较结果相同。
- 未覆盖：Production UI 皮肤、目标分辨率文字与按钮人工检查、最终 Tile 接缝和整体构图人工检查；因此 Scene-01 尚未完成。

## 13. 可延期润色项

- 道路连接已满足功能契约，但细节密度低于草地；后续可在不改变 6 px 边缘签名的前提下增加纹理。
- 玩家上下方向的正背面特征、左向步行时的头顶高度可以进一步统一；当前脚底基线已通过。
- 已喂母鸡、可收蛋、干地和湿地状态可以增加色相或轮廓差异；当前原生尺寸可辨识。
- 重建前后文件哈希、Catalog 文件拆分和 Unity 新版 Sprite 数据接口属于管线强化，不阻断本批次或正式场景接线。
