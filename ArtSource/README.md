# CozyTown 美术源文件

`Generated/A0` 保存内置图像生成器产生的原始 PNG。`Previews/A0` 保存由 Unity Editor 工具生成的整数倍预览。这两个目录不由 Unity AssetDatabase 导入，也不得被运行时代码引用。

当前可复现切片是胡萝卜物品图标探针：

```text
Generated/A0/item_crop_carrot_source.png
    ↓ CozyTownA0PixelProbeCompiler
Assets/CozyTown/Art/References/A0/a0_item_crop_carrot.png
    └─ Previews/A0/item_crop_carrot_4x.png
```

可在 Unity 菜单执行 `CozyTown > Art > Build A0 Carrot Pixel Probe`，或在批处理环境执行：

```powershell
Unity.exe -batchmode -nographics -quit `
  -projectPath <project-root> `
  -executeMethod CozyTown.Unity.Editor.CozyTownA0PixelProbeCompiler.Build
```

编译器裁切源图透明边界，将内容采样到 `16×16 px` 画布，输出二值 Alpha 和不超过 8 个不透明颜色，并生成严格最近邻的 `64×64 px` 预览。输出仍属于 A0 参考资产；A0 总门禁通过前不得复制到 `Production`。
