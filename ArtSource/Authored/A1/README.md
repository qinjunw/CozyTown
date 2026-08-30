# A1 确定性像素源

本目录保存无需生成式图像模型的原生像素源。`.pixels` 文件忽略空行和以 `#` 开头的说明行；其余行从画面顶部开始，必须保持相同宽度。

`.` 表示透明像素。`0-9A-V` 按顺序映射 `WarmRural32` 的 32 个颜色；编译器拒绝未知符号、非矩形数据、错误的单元尺寸、半透明像素和色板外颜色。

- `ui_icon_settings.pixels`：独立 `16×16 px` 齿轮图标源。
- `ui_marker_interact.pixels`：覆盖 `ui_mvp_16.png` 中 `ui_marker_interact` 单元的 `16×16 px` 带尾气泡源；字母 `E` 由 Unity `Text` 叠加，不烘焙进 Sprite。
