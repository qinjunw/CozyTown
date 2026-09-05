# T1 native sprite sources

These 56 `.pixels` files are normalized from the generated artwork recorded in `ArtSource/Generated/T1/GENERATION_LOG.md`. They preserve full native canvases so the Production compiler does not fit individual poses or move roof overlays.

## Reproduce

Run Windows PowerShell 5.1 from the repository root. The script uses its bundled .NET `System.Drawing` implementation; PowerShell 7's type-forwarding behavior is not supported by this script.

```powershell
$owners = @('shopkeeper_mina', 'farmer_eli', 'fisher_ren', 'cook_sora')
foreach ($owner in $owners) {
    $arguments = @('-NoProfile', '-File', 'ArtSource/Authored/T1/NormalizeT1Sprites.ps1',
        '-SourcePath', "ArtSource/Generated/T1/npc_${owner}_source_v01.png",
        '-NpcSuffix', $owner, '-MirrorRightFromLeft')
    if ($owner -eq 'cook_sora') { $arguments += '-RemoveNeutralBackground' }
    else { $arguments += '-PortraitColorNormalization' }
    & powershell.exe @arguments
}
```

Each invocation uses a fresh process because the compiled helper type remains loaded until that process exits.

```powershell
powershell.exe -NoProfile -File ArtSource/Authored/T1/NormalizeT1Sprites.ps1 -SourcePath ArtSource/Generated/T1/npc_homes_source_v01.png -AlternativeSourcePath ArtSource/Generated/T1/npc_homes_source_v02.png -Homes
```

The commands replace the corresponding generated `.pixels` and native/8× PNG review outputs in this directory. They do not write `Assets`, Production textures, or `ArtSource/Previews`.

## Format and geometry

Each file contains one top-to-bottom row per native pixel row, with no header. NPC files have 32 rows of 24 characters; house and roof files have 64 rows of 64 characters. `.` is transparent. `0123456789ABCDEFGHIJKLMNOPQRSTUV` index the existing WarmRural32 palette in order. Opaque pixels have alpha 255.

NPCs use one scale per resident and a complete-foot baseline. Right-facing frames mirror the complete corresponding left-facing native frames. Exterior neutral-background removal is flood-filled from the image edges, preserving enclosed ivory clothing and cream walls.

Mina, Eli and Ren use `PortraitColorNormalization`. Warm-colored sampling blocks use their source RGB mean before palette mapping so intermediate skin, hair and cloth-shadow colors survive native sampling. Dark outlines, blue/green material colors, ivory eye highlights and the gold highlight remain on the majority-color path. Eli's exposed face and hands use the portrait's peach shadow/mid/highlight ramp; his orange hat remains unchanged. Sora does not use this color adjustment. None of these rules changes alpha, cell geometry or grounding.

Ren's idle-down cell restores a continuous rounded cap crown after sampling: five dark outline pixels at x10–14 on top-down row1, with the five pixels directly below changed from outline to the existing navy cap color. This gives that standing pose 31 visible rows while preserving the common scale, feet and the other eleven frames. The script checks the expected empty/cap-outline source cells before applying the correction.

House source choices and measured doorway anchors are explicit in `NormalizeHomes`. All four houses share one scale and place the doorway center at x38. The roof source copies the positioned building's first 26 text rows and clears the remaining 38; it never fits the roof independently.

## Import and acceptance

The separate `CozyTown.Unity.Editor.CozyTownT1PixelArtBatchCompiler.Build` entry point writes the six Production sheets and exact 4× previews. Unity execution is coordinated by the main task. `T1NpcMovementAssetTests` and `T1PixelArtAssetManifestTests` check imported names, rectangles, pivots, PPU, palette, binary alpha, preview equality, body envelopes, complete connected feet, side silhouettes, distinct walk phases and roof correspondence. These checks do not replace visual identity or scene occlusion review.
