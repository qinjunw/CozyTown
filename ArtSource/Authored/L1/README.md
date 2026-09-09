# Town lamp source

The lamp is authored directly in the two `16 × 32` `.pixels` files. Each nontransparent symbol indexes the existing `WarmRural32` palette. No generated reference image supplies pixels to this asset.

`CozyTown/Art/Build Town Lighting Pixel Art` compiles both cells into `Assets/CozyTown/Art/Production/Props/prop_town_lamp_16x32.png` and writes a 4× nearest-neighbor preview under `ArtSource/Previews/L1/`. Both slices use 16 PPU and the same BottomCenter pivot.

The pole uses the scene's lit sprite material. The glass overlay uses the unlit sprite material, with its opacity driven by the same lamp strength as the local 2D light. The source uses binary alpha; runtime fading changes the renderer opacity without changing the source pixels.

Run `CozyTown/Upgrade Development Scene Lighting` after compiling the art. The upgrader adds or updates its named lighting objects under `World/Town Lighting`; it leaves the town roads, homes, interaction points and collision geometry in place. An existing `DefaultTownLighting.asset` retains its authored time and color settings.
