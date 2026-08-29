# ADR-0006：以 Tilemap 和窄表现组件接入 Production 美术

- 状态：已接受
- 日期：2026-08-29

## 背景

A1 已生成并验收 11 个 Production PNG、98 个命名 Sprite 和 11 个 4× 最近邻预览。正式场景仍使用 Unity 内置 `UISprite` 表示玩家、边界和 7 个交互点，也没有 Tilemap、Pixel Perfect Camera 或运行时 Sprite 状态映射。

美术接线是 M4 与 M5 之间的独立阶段。它必须保留现有领域规则、应用协调器、7 个交互种类、碰撞和输入生命周期；Scene-01 验收前不接入真实 AI 驱动 NPC。

## 评估选项

### 选项 A：继续使用缩放后的单个 SpriteRenderer 方块

改动较少，但不能验证道路连接、重复 Tile 接缝、像素相机或 Production 状态 Sprite，也无法作为最终场景表现。

### 选项 B：用大量 SpriteRenderer 手工铺设所有地图格

可以消费 Tile Sprite，但场景 YAML 会包含大量重复对象，地图连接和重建难以审查，编辑器升级工具也容易遗留旧对象。

### 选项 C：Tilemap 表示地面与道路，SpriteRenderer 表示对象和状态

Tilemap 负责规则网格和道路邻接；建筑、功能物件、角色、农田与母鸡状态保留独立 SpriteRenderer。编辑器场景升级工具负责创建并序列化引用，运行时组件只读取现有移动状态或只读 ViewState。

## 决策

采用选项 C。

- 地面与道路使用 `Grid` 和 `Tilemap`，Tile 资产引用 `tile_town_base_16.png` 的命名 Sprite。
- Main Camera 使用 URP `PixelPerfectCamera`，`assetsPPU=16`、参考分辨率 `320×180`、点采样放大。Production Sprite 不使用非整数 Transform 缩放。
- 建筑、农田、池塘、玩家、世界 NPC 和状态对象使用 SpriteRenderer；排序顺序与碰撞、交互逻辑分离。
- 玩家表现组件只读取 `PlayerMovement2D.LastMoveDirection` 和 Rigidbody2D 速度，映射四方向静止与两帧步行 Sprite。
- 农田和母鸡表现组件只接收应用层只读 ViewState。状态变化继续由现有 Presenter 和应用协调器产生，表现组件不取得领域服务或写接口。
- Scene-01 分成世界表现、运行时状态表现和 UI 皮肤三个垂直切片。现有 `OnGUI` 调试界面可以在 UI 切片完成前保留，但不能作为 Scene-01 最终 UI 验收证据。
- Scene-01c 使用 UGUI `Canvas`、`CanvasScaler`、`Image`、`Button` 和 `Text` 接入 Production 九宫格、图标与 Unity 字体。现有 Presenter 和 View 的公开状态与事件继续作为 UI 接线边界，不为皮肤接入增加领域写接口。
- 编辑器升级入口必须可重复执行：按固定对象名更新 A1 管理的视觉节点，不删除或重建 Bootstrap、Presenter、交互点和碰撞对象。
- 运行时代码不得使用 `AssetDatabase`、文件路径或 Sprite 名称动态查找资源；所有引用由场景或 Prefab 序列化。

## 验证规则

- EditMode 场景测试验证 Pixel Perfect Camera 参数、Tilemap、Production Sprite 引用、7 个交互点和内置占位 Sprite 清除结果。
- Scene-01c 测试只通过公开 UGUI 组件、现有 View 接口和 `Button.onClick` 验证 Production UI 引用、文字值、按钮状态、事件接线与布局边界；不调用 `OnGUI`，不读取私有字段或布局子对象层级。
- PlayMode 测试验证玩家方向与步行动画、状态表现刷新、移动、碰撞、交互、模态输入和生产经济闭环不回归。
- 人工检查以 `320×180` 原生参考分辨率及 `2×`、`4×` 整数倍验证 Tile 接缝、脚底稳定、功能物件辨识和 UI 可读性。
- 自动化默认不联网、不调用计费模型、不读写玩家正式存档。
- EditMode 与 PlayMode 全量通过、场景无丢失引用或异常且升级结果幂等后，自动化在人工场景验收前停止；截图黄金图不替代可读性、接缝、脚底稳定和整体构图的人工判断。

## 后果

Tilemap 和序列化 Sprite 引用使场景资源消费可被 Unity 场景测试直接验证。视觉状态与确定性状态仍由窄接口隔离，A1 接线不会获得新的游戏写权限。

场景升级工具需要维护 Tile 资产、固定视觉节点和 Production UI 引用；UI 皮肤保持独立切片，不能由世界场景接线自动完成。真实 AI 驱动 NPC、AI 评测、Windows 发布构建和最终录屏只在 Scene-01 人工验收后进行。

## 复审条件

只有在小镇地图扩展为多场景、需要运行时地图流送，或 Tile 规则无法由当前 20 个固定 Sprite 表达时，才复审地图表现方案。复审不得改变领域模块、交互权限或 AI 状态隔离边界。
