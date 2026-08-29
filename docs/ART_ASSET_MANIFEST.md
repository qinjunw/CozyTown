# CozyTown MVP Production 美术清单

## 1. 范围与交付门禁

本清单是 `Assets/CozyTown/Art/Production/` 的生产资源契约。资源只覆盖当前 MVP 已有画面：一张小镇地图、商店、NPC、床、农田、鸡舍、池塘和厨房 7 个交互点，3 种作物、3 种鱼、1 只鸡、5 个料理、18 个物品和 4 名 NPC。

本轮交付采用批次级技术与可读性验收。11 个 PNG 源文件可以作为一个批次生成，但只有在文件、切片、导入策略和小尺寸可读性全部通过后，才能接入正式场景。A0 参考图只提供构图、轮廓和色彩关系；不得缩放、裁切或改名后直接作为 Production 资源。

本清单不包含室内地图、野外、季节、天气、工具动作全集、NPC 行走日程、新的交互点、更多内容物或宣传插画。

## 2. 全局导入与切片约定

- 文件格式：PNG；除 `tile_town_base_16.png` 可使用全不透明像素外，其余文件必须使用硬透明 Alpha。
- 透明像素 Alpha 为 `0`，可见像素 Alpha 为 `255`；不允许半透明抗锯齿、烘焙棋盘格、白边或暗边。
- Unity Pixels Per Unit：`16`。
- Filter Mode：Point；Compression：None；Mip Maps：Off；Wrap Mode：Clamp；sRGB：On。
- `Single` 文件使用一个 Sprite；`Multiple` 文件严格按表格切片，不生成空白或未命名 Sprite。
- 表格中的行从源图顶部开始编号，列从左侧开始编号。单元格 `rNcM` 对应 Unity Rect：

```text
x = M * frameWidth
y = (rowCount - 1 - N) * frameHeight
width = frameWidth
height = frameHeight
```

- `Center` Pivot 为 `(0.5, 0.5)`；`BottomCenter` Pivot 为 `(0.5, 0)`。
- Sprite 名使用稳定领域 ID 的资源形式，将领域 ID 中的 `.` 替换为 `_`。显示名称不得作为资源查找键。
- 左上方为统一光源方向。图标、角色和场景物件在原生尺寸及 `2×`、`4×` 整数倍显示时必须保持清楚轮廓。

## 3. Production 源文件总表

| # | 路径 | 画布 | 模式 | 网格 | 单帧 | 切片数 | Pivot | Alpha |
| --- | --- | --- | --- | --- | --- | ---: | --- | --- |
| 1 | `Assets/CozyTown/Art/Production/Environment/Tiles/tile_town_base_16.png` | `64×80` | Multiple | `4×5` | `16×16` | 20 | Center | 全不透明 |
| 2 | `Assets/CozyTown/Art/Production/Props/prop_town_decor_16x32.png` | `64×64` | Multiple | `4×2` | `16×32` | 8 | BottomCenter | 硬透明 |
| 3 | `Assets/CozyTown/Art/Production/Buildings/bld_town_functions_64.png` | `128×128` | Multiple | `2×2` | `64×64` | 4 | BottomCenter | 硬透明 |
| 4 | `Assets/CozyTown/Art/Production/Props/prop_town_functions_96x64.png` | `192×64` | Multiple | `2×1` | `96×64` | 2 | BottomCenter | 硬透明 |
| 5 | `Assets/CozyTown/Art/Production/Props/prop_farm_states_16.png` | `112×32` | Multiple | `7×2` | `16×16` | 14 | Center | 硬透明 |
| 6 | `Assets/CozyTown/Art/Production/Props/prop_hen_states_16.png` | `48×16` | Multiple | `3×1` | `16×16` | 3 | BottomCenter | 硬透明 |
| 7 | `Assets/CozyTown/Art/Production/Characters/chr_player_move_16x24.png` | `48×96` | Multiple | `3×4` | `16×24` | 12 | BottomCenter | 硬透明 |
| 8 | `Assets/CozyTown/Art/Production/Characters/npc_shopkeeper_mina_idle_down.png` | `16×24` | Single | `1×1` | `16×24` | 1 | BottomCenter | 硬透明 |
| 9 | `Assets/CozyTown/Art/Production/Characters/npc_portraits_48.png` | `192×48` | Multiple | `4×1` | `48×48` | 4 | Center | 硬透明 |
| 10 | `Assets/CozyTown/Art/Production/Items/item_mvp_16.png` | `96×48` | Multiple | `6×3` | `16×16` | 18 | Center | 硬透明 |
| 11 | `Assets/CozyTown/Art/Production/UI/ui_mvp_16.png` | `64×48` | Multiple | `4×3` | `16×16` | 12 | Center | 硬透明 |

## 4. Sprite 切片清单

### 4.1 小镇地面 Tile

文件：`Environment/Tiles/tile_town_base_16.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `tile_grass_00` | 小镇草地基础变体 |
| r0c1 | `tile_grass_01` | 小镇草地基础变体 |
| r0c2 | `tile_grass_02` | 小镇草地基础变体 |
| r0c3 | `tile_grass_03` | 小镇草地基础变体 |
| r1c0 | `tile_path_isolated` | 无相邻道路的单格道路 |
| r1c1 | `tile_path_horizontal` | 东西连接道路 |
| r1c2 | `tile_path_vertical` | 南北连接道路 |
| r1c3 | `tile_path_cross` | 四向连接道路 |
| r2c0 | `tile_path_corner_ne` | 北、东连接道路 |
| r2c1 | `tile_path_corner_se` | 南、东连接道路 |
| r2c2 | `tile_path_corner_sw` | 南、西连接道路 |
| r2c3 | `tile_path_corner_nw` | 北、西连接道路 |
| r3c0 | `tile_path_tee_n` | 缺少南向连接的三向道路 |
| r3c1 | `tile_path_tee_e` | 缺少西向连接的三向道路 |
| r3c2 | `tile_path_tee_s` | 缺少北向连接的三向道路 |
| r3c3 | `tile_path_tee_w` | 缺少东向连接的三向道路 |
| r4c0 | `tile_path_end_n` | 仅向北连接的道路端点 |
| r4c1 | `tile_path_end_e` | 仅向东连接的道路端点 |
| r4c2 | `tile_path_end_s` | 仅向南连接的道路端点 |
| r4c3 | `tile_path_end_w` | 仅向西连接的道路端点 |

20 个 Tile 使用 `16×16` 帧和 Center Pivot。4 个草地变体必须能够以 `3×3` 重复铺设；16 个道路 Tile 覆盖四方向邻接的全部组合。

### 4.2 小镇边界与装饰

文件：`Props/prop_town_decor_16x32.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `prop_tree_deciduous` | 地图边界树木 |
| r0c1 | `prop_shrub` | 地图边界灌木 |
| r0c2 | `prop_flower_red` | 非交互红花装饰 |
| r0c3 | `prop_flower_yellow` | 非交互黄花装饰 |
| r1c0 | `prop_fence_horizontal` | 水平边界围栏 |
| r1c1 | `prop_fence_vertical` | 垂直边界围栏 |
| r1c2 | `prop_town_sign` | 非交互小镇路牌 |
| r1c3 | `prop_rock` | 非交互边界石块 |

8 个装饰使用 `16×32` 帧和 BottomCenter Pivot。尺寸较小的围栏、花和石块靠帧底部放置，上部保持透明；这些对象只承担边界与视觉区分，不新增交互。

### 4.3 四个功能建筑

文件：`Buildings/bld_town_functions_64.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `bld_shop` | Shop 交互点与商店交易 |
| r0c1 | `bld_home` | Bed 交互点与睡觉推进日期 |
| r1c0 | `bld_kitchen` | Kitchen 交互点与 5 个固定配方 |
| r1c1 | `bld_coop` | Coop 交互点与母鸡喂食、收蛋 |

4 个建筑使用 `64×64` 帧和 BottomCenter Pivot。正面入口必须在无文字时可辨认，并保留足够对比度供现有交互提示叠加。

### 4.4 农田与池塘

文件：`Props/prop_town_functions_96x64.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `prop_farm` | Farm 交互点与 6 个逻辑地块的底图 |
| r0c1 | `prop_pond` | Pond 交互点与简化钓鱼 |

2 个物件使用 `96×64` 帧和 BottomCenter Pivot。`prop_farm` 只画 6 个可辨认地块位置；地块状态由 4.5 的 Sprite 叠加。池塘保持静态，不增加钓鱼动画或新判定。

### 4.5 农田状态

文件：`Props/prop_farm_states_16.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `farm_plot_soil_dry` | 地块当日未浇水 |
| r0c1 | `farm_plot_soil_watered` | 地块当日已浇水 |
| r0c2 | `crop_potato_stage_00` | 土豆播种，成长进度 0 |
| r0c3 | `crop_potato_stage_01` | 土豆成长进度 1 |
| r0c4 | `crop_potato_stage_02` | 土豆成熟，成长进度 2 |
| r0c5 | `crop_carrot_stage_00` | 胡萝卜播种，成长进度 0 |
| r0c6 | `crop_carrot_stage_01` | 胡萝卜成长进度 1 |
| r1c0 | `crop_carrot_stage_02` | 胡萝卜成长进度 2 |
| r1c1 | `crop_carrot_stage_03` | 胡萝卜成熟，成长进度 3 |
| r1c2 | `crop_tomato_stage_00` | 番茄播种，成长进度 0 |
| r1c3 | `crop_tomato_stage_01` | 番茄成长进度 1 |
| r1c4 | `crop_tomato_stage_02` | 番茄成长进度 2 |
| r1c5 | `crop_tomato_stage_03` | 番茄成长进度 3 |
| r1c6 | `crop_tomato_stage_04` | 番茄成熟，成长进度 4 |

14 个状态使用 `16×16` 帧和 Center Pivot。阶段数量直接对应默认成长天数：土豆 2 天、胡萝卜 3 天、番茄 4 天；不增加季节、枯萎或额外品质状态。

### 4.6 母鸡状态

文件：`Props/prop_hen_states_16.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `animal_hen_idle` | 未喂食且无产物 |
| r0c1 | `animal_hen_fed` | 当日已喂食 |
| r0c2 | `animal_hen_product_ready` | 鸡蛋可收取 |

3 个状态使用 `16×16` 帧和 BottomCenter Pivot。它们映射 `FedToday` 与 `ProductReady` 的当前可观察组合，不添加移动、孵化或第二种动物。

### 4.7 玩家移动

文件：`Characters/chr_player_move_16x24.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `chr_player_idle_down` | 玩家向下静止 |
| r0c1 | `chr_player_walk_down_00` | 玩家向下步行相位 A |
| r0c2 | `chr_player_walk_down_01` | 玩家向下步行相位 B |
| r1c0 | `chr_player_idle_left` | 玩家向左静止 |
| r1c1 | `chr_player_walk_left_00` | 玩家向左步行相位 A |
| r1c2 | `chr_player_walk_left_01` | 玩家向左步行相位 B |
| r2c0 | `chr_player_idle_right` | 玩家向右静止 |
| r2c1 | `chr_player_walk_right_00` | 玩家向右步行相位 A |
| r2c2 | `chr_player_walk_right_01` | 玩家向右步行相位 B |
| r3c0 | `chr_player_idle_up` | 玩家向上静止 |
| r3c1 | `chr_player_walk_up_00` | 玩家向上步行相位 A |
| r3c2 | `chr_player_walk_up_01` | 玩家向上步行相位 B |

12 帧使用 `16×24` 和 BottomCenter Pivot。每个方向只保留 1 帧静止与 2 帧步行；不生产浇水、挥锄、抛竿、睡觉或烹饪动作。

### 4.8 世界 NPC

文件：`Characters/npc_shopkeeper_mina_idle_down.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `npc_shopkeeper_mina_idle_down` | 现有单一 NPC 世界交互点 |

该 Single Sprite 使用 `16×24` 和 BottomCenter Pivot。世界里只显示一个静态 NPC，不增加 4 名 NPC 的独立位置、移动或日程。

### 4.9 NPC 头像

文件：`Characters/npc_portraits_48.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `npc_shopkeeper_mina_portrait` | 商店老板 Mina 的对话选择与文本 |
| r0c1 | `npc_farmer_eli_portrait` | 农夫 Eli 的对话选择与文本 |
| r0c2 | `npc_fisher_ren_portrait` | 渔夫 Ren 的对话选择与文本 |
| r0c3 | `npc_cook_sora_portrait` | 厨师 Sora 的对话选择与文本 |

4 个头像使用 `48×48` 和 Center Pivot。每名 NPC 只交付 1 个中性头像；AI 的 `emotion` 标签继续以文本或现有 UI 状态表达，不生成表情变体。

### 4.10 18 个物品图标

文件：`Items/item_mvp_16.png`

| Cell | Sprite 名 | 稳定物品 ID | 对应玩法对象 |
| --- | --- | --- | --- |
| r0c0 | `item_seed_potato` | `seed.potato` | 商店购买与土豆播种 |
| r0c1 | `item_seed_carrot` | `seed.carrot` | 商店购买与胡萝卜播种 |
| r0c2 | `item_seed_tomato` | `seed.tomato` | 商店购买与番茄播种 |
| r0c3 | `item_crop_potato` | `crop.potato` | 土豆收获、出售与烤土豆 |
| r0c4 | `item_crop_carrot` | `crop.carrot` | 胡萝卜收获、出售与蔬菜汤 |
| r0c5 | `item_crop_tomato` | `crop.tomato` | 番茄收获、出售、蔬菜汤与番茄炒蛋 |
| r1c0 | `item_feed_chicken` | `feed.chicken` | 商店购买与母鸡喂食 |
| r1c1 | `item_animal_product_egg` | `animal_product.egg` | 鸡舍收取、出售和料理输入 |
| r1c2 | `item_fish_carp` | `fish.carp` | 钓鱼结果、出售和烤鱼 |
| r1c3 | `item_fish_trout` | `fish.trout` | 钓鱼结果、出售和鱼肉派 |
| r1c4 | `item_fish_bass` | `fish.bass` | 钓鱼结果与出售 |
| r1c5 | `item_ingredient_salt` | `ingredient.salt` | 商店购买、烤土豆与烤鱼 |
| r2c0 | `item_ingredient_flour` | `ingredient.flour` | 商店购买与鱼肉派 |
| r2c1 | `item_food_baked_potato` | `food.baked_potato` | 烤土豆成品与出售 |
| r2c2 | `item_food_vegetable_soup` | `food.vegetable_soup` | 蔬菜汤成品与出售 |
| r2c3 | `item_food_grilled_fish` | `food.grilled_fish` | 烤鱼成品与出售 |
| r2c4 | `item_food_tomato_egg` | `food.tomato_egg` | 番茄炒蛋成品与出售 |
| r2c5 | `item_food_fish_pie` | `food.fish_pie` | 鱼肉派成品与出售 |

18 个图标使用 `16×16` 和 Center Pivot。每个图标必须依靠轮廓与主色区分，不依赖文字；种子与对应收获物、3 种鱼以及 5 个料理分别需要可辨认的轮廓差异。

### 4.11 UI 皮肤与状态图标

文件：`UI/ui_mvp_16.png`

| Cell | Sprite 名 | 对应玩法对象 |
| --- | --- | --- |
| r0c0 | `ui_panel` | HUD、商店、农田、鸡舍、池塘、厨房、NPC 和存档面板的九宫格底图 |
| r0c1 | `ui_button_normal` | 可用按钮默认状态 |
| r0c2 | `ui_button_hover` | 可用按钮悬停状态 |
| r0c3 | `ui_button_pressed` | 按钮按下状态 |
| r1c0 | `ui_button_disabled` | 余额、库存或食材不足时的禁用按钮 |
| r1c1 | `ui_icon_coin` | 金币余额 |
| r1c2 | `ui_icon_clock` | 天数与时间 |
| r1c3 | `ui_icon_save` | 单槽保存 |
| r2c0 | `ui_icon_load` | 单槽读取 |
| r2c1 | `ui_icon_close` | 关闭模态面板 |
| r2c2 | `ui_marker_selection` | 当前列表或 NPC 选择 |
| r2c3 | `ui_marker_interact` | 靠近既有功能点时的交互提示 |

12 个 UI Sprite 使用 `16×16` 和 Center Pivot。`ui_panel` 与 4 个按钮状态需要保留可配置九宫格边缘；最终文字继续由 Unity 字体渲染，不烘焙进图片。

## 5. 批次验收

### 5.1 自动化可验证项

1. 11 个固定路径全部存在，PNG 画布尺寸与总表一致。
2. Unity 导入后的 Sprite Mode、PPU、Filter、Compression、Mip Map、Wrap、sRGB 和 Alpha 设置符合第 2 节。
3. Multiple 文件按约定行列得到准确切片数；Single 文件只产生一个主 Sprite。
4. 98 个 Sprite 名与本清单逐项一致，没有额外、缺失或重复名称。
5. 每个 Sprite Rect 由第 2 节公式和对应 Cell 唯一确定；Pivot 与源文件总表一致。
6. Production 资源不包含对 Runtime 领域程序集的反向依赖，正式场景接入前不改变既有玩法对象数量。

### 5.2 人工可读性项

1. 所有资源使用统一的 16 px 像素密度、左上光向和锁定色板。
2. 玩家四方向和两帧步行相位的脚底位置、身体高度与轮廓稳定。
3. 4 个功能建筑、农田和池塘在无文字时可以区分；装饰物不表现为可交互目标。
4. 3 种作物在成熟状态下可区分，干地与湿地可区分，6 个地块可重复使用同一状态资源。
5. 母鸡的未喂、已喂和可收蛋状态可区分。
6. 18 个物品图标在 `16×16` 原生尺寸下可区分，尤其是种子与收获物、3 种鱼和 5 个料理。
7. 4 名 NPC 头像在轮廓、发色或服装中至少有两项稳定差异；世界 NPC 与 Mina 头像保持一致特征。
8. UI 的正常、悬停、按下和禁用状态具有可观察差异，图标不依赖文字说明其基本含义。
9. 资源没有抗锯齿、渐变、软阴影、棋盘格背景或现有游戏资产的可识别复制。

## 6. 数量汇总

| 项目 | 数量 |
| --- | ---: |
| Production PNG 源文件 | 11 |
| Environment Tile | 20 |
| 边界与装饰 Sprite | 8 |
| 建筑 Sprite | 4 |
| 农田与池塘 Sprite | 2 |
| 农田状态 Sprite | 14 |
| 母鸡状态 Sprite | 3 |
| 玩家 Sprite | 12 |
| 世界 NPC Sprite | 1 |
| NPC 头像 Sprite | 4 |
| 物品图标 Sprite | 18 |
| UI Sprite | 12 |
| **Sprite 总数** | **98** |
