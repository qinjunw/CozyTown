# CozyTown 美术源文件

`Generated/A0`、`Generated/A1` 保存内置图像生成器产生的原始 PNG；`Previews/A0`、`Previews/A1` 保存 Unity Editor 工具生成的整数倍预览。这些目录不由 Unity AssetDatabase 导入，也不得被运行时代码引用。

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

编译器裁切源图透明边界，将内容采样到 `16×16 px` 画布，输出二值 Alpha 和不超过 8 个不透明颜色，并生成严格最近邻的 `64×64 px` 预览。该输出仍属于 A0 参考资产，不得复制到 `Production`；A1 使用下面的独立源稿与批次门禁。

A1 使用 11 张源稿生成完整 MVP 美术包：

```text
Generated/A1/*.png
    ↓ CozyTownA1PixelArtBatchCompiler
Assets/CozyTown/Art/Production/**/*.png
    └─ Previews/A1/*_4x.png
```

可在 Unity 菜单执行 `CozyTown > Art > Build Current A1 Pixel Art Batch`，或在批处理环境执行：

```powershell
Unity.exe -batchmode -nographics -quit `
  -projectPath <project-root> `
  -executeMethod CozyTown.Unity.Editor.CozyTownA1PixelArtBatchCompiler.Build
```

A1 编译器按 `docs/ART_ASSET_MANIFEST.md` 生成 11 个 Production PNG、98 个 Sprite 和 11 个 4× 最近邻预览。生成结果仍需通过 Production 清单测试和批次目视检查；源稿不得直接进入正式场景。
