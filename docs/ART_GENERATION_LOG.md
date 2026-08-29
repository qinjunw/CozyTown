# CozyTown A0 美术生成记录

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
