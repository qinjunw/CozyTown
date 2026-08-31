# ADR-0009：使用屋顶前景层和 24×32 世界角色收口 Scene-01

- 状态：Accepted
- 日期：2026-09-01
- 决策范围：A1 Scene-01g 世界渲染与角色资源

## 背景

ADR-0008 将四座房屋上方约 2/5 调整为可通行背面区域，但完整建筑仍由一个 `SpriteRenderer` 绘制。玩家进入该区域时会整体绘制在房屋上方，视觉结果像站在屋顶表面。现有 `16×24` 主角和 NPC 世界 Sprite 也不足以稳定表达头像中的发型、帽子和职业服装。

Unity 的 2D Renderer 使用 Sorting Layer 和 Order in Layer 决定渲染优先级；Sprite Sort Point 和自定义轴排序仍以整个 Renderer 为单位。单个完整建筑 Sprite 不能同时让墙体位于角色后方、屋顶位于角色前方。

## 决策

1. 每座房屋保留一个完整建筑底图，并增加一个同位置、同尺寸的 `Roof Foreground` 子对象。底图排序值为 5，NPC 为 15，玩家为 20，屋顶前景为 30。
2. 屋顶前景由既有建筑源稿确定性派生。每个 `64×64` 单元保留顶部 26 行，底部 38 行写入全透明像素；它不包含碰撞体、Trigger、Presenter 或领域依赖。
3. 主角移动图集与四名 NPC 世界图集统一为 `24×32`、BottomCenter Pivot 和 16 PPU。主角继续使用原有 12 帧和动画状态名；NPC 继续使用既有四个稳定 ID、Sprite 名、头像映射和 Presenter。
4. NPC 源稿可以使用内置图像生成器，但必须经过项目编译器的白底移除、色板锁定、二值 Alpha、切片和导入门禁。场景不得直接引用生成源稿。
5. 房屋碰撞、门槽、交互 Trigger、NPC 逻辑和存档格式不变。Scene-01g 不增加动态 Y 排序、室内地图、角色动作、NPC 日程或新玩法。

## 理由

两个 Renderer 是满足当前静态小镇构图的最小方案。它直接表达“墙体在后、屋顶在前”的关系，并复用既有建筑像素，不需要修改移动规则或引入运行时遮罩。`24×32` 为世界角色提供更多身份像素，同时保留 16 PPU、BottomCenter 脚底基线和现有动画接口。

## 备选方案

- 仅使用 Y 轴排序：拒绝。排序作用于整个建筑 Renderer，不能拆分墙体和屋顶。
- 运行时改变整栋房屋排序值：拒绝。角色会在整栋房屋前后跳变，不能形成局部遮挡。
- Shader、Sprite Mask 或动态透明：拒绝。当前静态房屋不需要额外材质与运行时状态，测试和美术维护成本更高。
- 重新绘制四套独立墙体与屋顶：延期。当前确定性派生已满足遮挡；人工验收发现屋檐边界不合适时再调整分割线或独立重绘。

## 后果

- 每座房屋增加一个只负责显示的子 Renderer；A1 Production 批次增加四个屋顶前景 Sprite，并退役旧 `16×24` 世界角色文件。
- 自动化可以验证透明区域、保留像素、渲染顺序、图集尺寸和 NPC 身份映射，但不能替代三档分辨率下的角色比例与屋后遮挡人工检查。
- 若后续地图采用自由移动建筑、动态遮挡或多层室内，需另行决策 Sorting Layer、Y 排序和 Sorting Group，不在本 ADR 中预设。

## 参考

- [Unity 6：2D Renderer sorting](https://docs.unity3d.com/cn/6000.0/Manual/2d-renderer-sorting.html)
- [Unity 6：Sort sprites](https://docs.unity3d.com/cn/6000.0/Manual/sprite/sort-sprites/sort-sprites.html)
- [Unity 6：2D game creation workflow](https://docs.unity3d.com/6000.1/Documentation/Manual/2d-game-creation-wokflow.html)
