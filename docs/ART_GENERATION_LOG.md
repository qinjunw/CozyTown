# CozyTown A0/A1 美术生成记录

## 1. 生成方式

A0 锚点使用内置图像生成器创建，并用图像编辑模式收敛到 `ART_DIRECTION.md` 定义的纯像素方向。输出保存在 `Assets/CozyTown/Art/References/A0/`，只作为构图、轮廓、配色和材质参考，不作为可切片 Sprite、无缝 Tile、动画帧或最终 UI。

## 2. 最终提示词集

### `a0_style_town.png`

> Preserve the existing one-screen town composition and only simplify the rendering. Show the current CozyTown MVP landmarks: store, home and bed function, farm plots, chicken coop with one hen, pond, kitchen, player, and one NPC world interaction point. Re-render as coarse 16-bit console pixel art based on a 320 by 180 logical screen, 16 by 16 tile language, hard square pixels, limited warm rural palette, upper-left light, and readable 3/4 top-down silhouettes. Remove soft shading, blur, antialiasing, HD-2D effects, text, labels, combat, wilderness, seasons, weather, and any added gameplay location.

### `a0_palette_board.png`

> Edit this existing pixel-art palette board only. Preserve the board concept, but use exactly 32 large swatches in a clean 8 by 4 grid. Use a cohesive warm rural-town palette covering soil, crops, pond water, wood, stone, roof, skin, UI neutrals, and restrained character accents. Every swatch should be one flat color with hard pixel edges. Do not add labels, numbers, extra swatches, checkerboard, blur, antialiasing, texture, highlights, shadows, or gradients.

### `a0_characters.png`

> Preserve exactly five characters and their left-to-right identities, colors, poses, spacing, order, and shared baseline: player, shopkeeper, farmer, fisher, cook. Re-render each as a coarse logical 16 by 24 pixel top-down RPG sprite enlarged with nearest-neighbor hard pixels. Use no more than 12 flat colors per character, simple silhouettes, upper-left light, minimal shading, and true transparent alpha. Remove soft edges, antialiasing, gradients, blur, painterly detail, cast shadows, text, labels, extra figures, props, and checkerboard.

### `a0_environment.png`

> Preserve the existing categories and layout: grass, dirt path, tilled soil, watered soil, pond edge and water, wooden fence, tree, store, home, kitchen, chicken coop, crop plot, pond, and the existing small rural props. Re-render as coarse 16 by 16 tile-based pure pixel art with hard pixels, flat limited colors, upper-left light, and a plain neutral presentation background. Remove soft shadows, blur, gradients, painterly texture, perspective drift, text, labels, and added objects or locations.

### `a0_ui_items.png`

> Preserve the UI panel and button shapes and exactly 18 item icons in three rows: carrot seed, potato seed, tomato seed, carrot, potato, tomato; chicken feed, egg, three fish, salt; flour, baked potato, vegetable soup, grilled fish, tomato egg dish, vegetable pie. Do not add, remove, reorder, merge, duplicate, or label icons. Re-render each icon as a logical 16 by 16 pure pixel sprite with hard pixels, flat limited colors, minimal upper-left shading, and true transparent alpha. Remove antialiasing, gradients, blur, text, labels, checkerboard, and colored backdrops.

## 3. 输出限制

当前生成文件是高分辨率 RGB 参考图。角色和 UI 图片中的棋盘格已经烘焙进像素，调色板色块仍含明度渐变；它们没有满足生产资源的透明通道、精确网格和锁色要求。上述问题必须在像素编辑器中清理后，才能进入 `Art/Production`。Unity 场景不得直接引用这些 A0 文件。

## 4. A0 胡萝卜像素管线探针

### 4.1 输入与提示词

内置图像生成器生成了 `ArtSource/Generated/A0/item_crop_carrot_source.png`。源文件为 `1254×1254 RGBA`，包含 256 个 Alpha 等级和约 3.3 万种 RGBA 颜色，因此只作为编译输入。

> Use case: stylized-concept. Asset type: source image for a 16×16 game UI item icon pixel-compilation probe. Create one original harvested carrot item icon, not carrot seeds, on a genuinely transparent background. Use a single compact carrot with a tapered orange root, a small separated green leaf crown, logical 16×16 pure pixel-art structure, upper-left highlight, lower-right solid shadow, at most 8 flat opaque colors, hard binary alpha, and a one-pixel-equivalent margin. Do not include antialiasing, blur, gradients, semi-transparent pixels, soft shadows, text, labels, watermarks, backgrounds, multiple carrots, seed packets, or an identifiable existing-game icon.

### 4.2 确定性处理

`CozyTownA0PixelProbeCompiler` 执行透明边界裁切、面积采样、8 色锁色、二值 Alpha 和 `16×16 px` 输出，并生成 `64×64 px` 最近邻预览。Unity 菜单入口为 `CozyTown > Art > Build A0 Carrot Pixel Probe`。

最终文件：

```text
Assets/CozyTown/Art/References/A0/a0_item_crop_carrot.png
ArtSource/Previews/A0/item_crop_carrot_4x.png
```

### 4.3 验证结果

- Unity EditMode 探针测试：`1/1` 通过。
- 输出：`16×16 RGBA`，Alpha 仅 `0`、`255`，非透明颜色 `7`，非透明像素 `67`。
- 预览：`64×64 RGBA`，与 4 倍最近邻结果逐像素一致。
- 独立视觉复核：1×/4×可读性、硬边、左上光向、色板关系和原创性通过。
- 阶段约束：探针留在 A0，不进入 `Production`；A1 使用独立源稿、清单和批次门禁。

## 5. A1 全套资源源稿

### 5.1 生成方式与文件

A1 使用内置图像生成器生成高分辨率栅格源稿，再由 Unity Editor 确定性编译器收敛到原生像素资源。源稿不被 Unity AssetDatabase 或运行时代码引用。

| 源稿 | 网格语义 | 背景处理 |
| --- | --- | --- |
| `ArtSource/Generated/A1/tiles_source.png` | `4×5` 小镇草地与道路 | Alpha；草地保留，16 个道路按连接掩码重建 |
| `ArtSource/Generated/A1/decor_source.png` | `4×2` 树、灌木、花、围栏、路牌、石块 | 连通白底移除 |
| `ArtSource/Generated/A1/buildings_source.png` | `2×2` 商店、住宅、厨房、鸡舍 | Alpha |
| `ArtSource/Generated/A1/town_functions_source.png` | `2×1` 六格农田、池塘 | Alpha |
| `ArtSource/Generated/A1/farm_states_source.png` | `7×2` 土壤与 3 种作物阶段 | Alpha |
| `ArtSource/Generated/A1/hen_states_source.png` | `3×1` 空闲、已喂、产物可收 | Alpha |
| `ArtSource/Generated/A1/player_source.png` | `3×4` 四方向的 idle/walkA/walkB | 连通白底移除 |
| `ArtSource/Generated/A1/mina_source.png` | 单个 Mina 世界 Sprite | 连通白底移除 |
| `ArtSource/Generated/A1/portraits_source.png` | `4×1` Mina、Eli、Ren、Sora | 连通白底移除 |
| `ArtSource/Generated/A1/items_source.png` | `6×3` 18 个 MVP 物品 | 连通白底移除 |
| `ArtSource/Generated/A1/ui_source.png` | `4×3` 面板、按钮状态、图标和标记 | Alpha |

### 5.2 可复现提示约束

以下记录去除了服务参数，保留生成时使用的完整语义约束。各批次共用前缀：

> Create original pure pixel art for a cozy rural top-down RPG. Use hard square pixels, flat limited colors, warm rural palette, upper-left light, dark warm outlines, strict equal grid cells, no text, logo, watermark, blur, antialiasing, gradients, checkerboard, copied game assets, or additional gameplay objects.

- 世界批次：严格生成 `4×5` 草地/道路、`4×2` 非交互装饰、`2×2` 商店/住宅/厨房/鸡舍和 `2×1` 六格农田/池塘；建筑入口和功能轮廓必须无需文字即可区分。
- 玩家批次：严格 `3×4`，行序为 down/left/right/up，列序为 idle/walkA/walkB；棕发、奶油衬衣、蓝色背带裤，四向身份一致，脚底基线稳定。
- NPC 批次：Mina 世界图保持棕发红发带、奶油衬衣和棕色围裙；头像严格单行 Mina/Eli/Ren/Sora，后三者分别以草帽农夫、蓝帽渔夫和橙发厨师区分。
- 物品批次：严格 `6×3`；第一行土豆/胡萝卜/番茄种子和对应作物，第二行鸡饲料、鸡蛋、鲤鱼、鳟鱼、鲈鱼、盐，第三行面粉、烤土豆、蔬菜汤、烤鱼、番茄炒蛋、鱼肉派。
- 生产状态批次：农田严格 `7×2`，包含干/湿土壤、土豆 3 阶段、胡萝卜 4 阶段、番茄 5 阶段；母鸡严格 `3×1`，包含空闲、已喂和鸡蛋可收。
- UI 批次：严格 `4×3`，依次包含 panel、normal/hover/pressed button、disabled button、coin、clock、save、load、close、selection 和 interact；不烘焙文字。

## 6. A1 确定性编译与验证

`CozyTownA1PixelArtBatchCompiler` 读取 11 张源稿，按声明式清单逐格裁切，移除 Alpha 或连通白底，面积采样到目标帧，锁定 32 色板，生成硬透明边缘，并写出 Production PNG、Sprite 切片和 4× 最近邻预览。小镇 Tile 为全不透明输出；4 个草地变体保留编译后的源稿纹理，16 个道路 Tile 根据 N/E/S/W 连接掩码确定性重建为统一 `6 px` 出口，源稿道路只承担草稿与语义输入。UI 前 5 个 Sprite 写入 `3 px` 九宫格 border，所有 BottomCenter Sprite 在编译后统一落到本地 `y=0`。

Unity 菜单入口为 `CozyTown > Art > Build Current A1 Pixel Art Batch`，批处理入口为：

```powershell
Unity.exe -batchmode -nographics -quit `
  -projectPath <project-root> `
  -executeMethod CozyTown.Unity.Editor.CozyTownA1PixelArtBatchCompiler.Build
```

输出位于 `Assets/CozyTown/Art/Production/`，预览位于 `ArtSource/Previews/A1/`。Production 清单测试从 11 个文件缺失的 RED 开始；独立审查再以道路连接、UI border 和玩家脚底基线 `3/3` RED 收紧契约。最终验证 11 个 PNG、98 个 Sprite、32 色、二值 Alpha、固定导入参数、道路连接、脚底基线、九宫格 border、切片、Pivot 和 4× 最近邻预览。全量结果为 EditMode `158/158`、PlayMode `26/26` 通过。

本资源生成批次没有使用 Pixelorama 或其他外部像素编辑器，源稿不得直接被场景引用。后续 Scene-01a/01b 已消费 Production 资源完成世界与运行时状态接线，Scene-01c 已接入 Production UGUI；细节润色和最终人工画面验收仍待完成，进度以 `ART_ACCEPTANCE.md` 为准。

## 7. UI 纯净木框视觉参考

2026-08-30 使用内置图像生成器生成 `ArtSource/Generated/A1/ui_clean_wood_panel_reference.png`。该图只作为面板材质和色彩关系参考，不作为 Production Sprite、场景布局或新增功能清单。Production 实现由 A1 确定性编译器生成，像素契约记录在 `ART_ACCEPTANCE.md` 第 13.2 节。

> Use case: ui-mockup. Asset type: CozyTown Unity 2D farming-life-sim pixel-art UI visual reference board. Design one clean cohesive direction for the existing HUD, save/load panel, interaction strip, and Fishing Pond modal. Use a flat near-black charcoal interior (`#1F1B24`) with a straight minimal farm-wood frame using warm browns (`#5B2E1A`, `#8A3B12`, `#C98256`). Use crisp true pixel art, uniform square corners and high-contrast cream text. Avoid green or beige corner patches, checkerboard or mosaic patterns, ornaments, leaves, flowers, rope, nails, gradients, blur, noisy wood grain, perspective distortion, red annotations, logos and watermarks.

A1 编译定义将参考图收敛为 `16×16 px` 的 `ui_panel`：三层木框依次使用 `#3B1F1B`、`#8A3B12`、`#C98256`，中心使用 `#1F1B24`。参考图中的附加控件和图标未进入 Production；正式图集只改变首个面板单元。

## 8. Scene-01d UI 增量资源

2026-08-30 为 Scene-01d 增加声明式 `16×16 px` 齿轮源稿与独立 Production 文件 `ui_icon_settings.png`，并把 `ui_marker_interact` 的目标单元收敛为带尾部的按键气泡。当前批次总计 12 个 Production PNG、99 个命名 Sprite 和 12 个 4× 最近邻预览；两次构建的 24 个 Production 与预览输出哈希一致。相对前一批次，现有 UI 图集中只允许交互标记目标单元变化，其他单元像素变化为 0。
