# CozyTown Art

`References/A0` 保存已选纯像素方向的风格锚点，不被运行时代码、Prefab 或正式场景引用。

`Production` 只保存通过 [`docs/ART_ACCEPTANCE.md`](../../../docs/ART_ACCEPTANCE.md) 技术与视觉验收的可切片资源。生产 PNG 由 Unity Editor 导入策略统一设置为 Sprite、16 PPU、Point Filter、无 mipmap 和无压缩。

项目根目录的 [`ArtSource`](../../../ArtSource/README.md) 保存生成式源图和派生预览。当前 `a0_item_crop_carrot.png` 是自动化像素管线探针，不是 A1 生产资源。

```text
Art/
├─ References/A0/
└─ Production/
   ├─ Characters/
   ├─ Environment/Tiles/
   ├─ Buildings/
   ├─ Props/
   ├─ Items/
   └─ UI/
```

文件名使用稳定 ID 的资源形式，不使用显示名称作为逻辑键。生成式图片必须先进入 References；未经逐像素清理、网格校正和切片验收，不得移动到 Production。
