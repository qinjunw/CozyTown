# A1 确定性像素源

本目录保存无需生成式图像模型的原生像素源。`.pixels` 文件忽略空行和以 `#` 开头的说明行；其余行从画面顶部开始，必须保持相同宽度。

`.` 表示透明像素。`0-9A-V` 按顺序映射 `WarmRural32` 的 32 个颜色；编译器拒绝未知符号、非矩形数据、错误的单元尺寸、半透明像素和色板外颜色。

Scene-01g 的四名 NPC 由 `ArtSource/Generated/A1/npc_world_source.png` 编译为 `npc_townsfolk_idle_down_24x32.png` 世界角色图集；输出保持 `24×32`、BottomCenter Pivot、二值透明和 `WarmRural32` 色板。本目录不再保留已停用的 `16×24` NPC 像素单元。

- `ui_icon_settings.pixels`：独立 `16×16 px` 齿轮图标源。
- `ui_marker_interact.pixels`：覆盖 `ui_mvp_16.png` 中 `ui_marker_interact` 单元的 `16×16 px` 带尾气泡源；字母 `E` 由 Unity `Text` 叠加，不烘焙进 Sprite。
