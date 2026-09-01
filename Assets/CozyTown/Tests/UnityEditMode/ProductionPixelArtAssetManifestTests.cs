using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProductionPixelArtAssetManifestTests
    {
        private const string TownTilePath =
            "Assets/CozyTown/Art/Production/Environment/Tiles/tile_town_base_16.png";
        private const string UiPath =
            "Assets/CozyTown/Art/Production/UI/ui_mvp_16.png";
        private const string SettingsIconPath =
            "Assets/CozyTown/Art/Production/UI/ui_icon_settings.png";
        private const string PlayerPath =
            "Assets/CozyTown/Art/Production/Characters/chr_player_move_24x32.png";

        private static readonly Color32[] WarmRural32 =
        {
            Rgb(0x1F, 0x1B, 0x24), Rgb(0x3B, 0x1F, 0x1B),
            Rgb(0xFF, 0xF4, 0xD6), Rgb(0xE6, 0xD5, 0xB8),
            Rgb(0xB4, 0x9A, 0x7A), Rgb(0x6F, 0x5A, 0x4A),
            Rgb(0x21, 0x4E, 0x24), Rgb(0x3E, 0x7B, 0x32),
            Rgb(0x76, 0xB9, 0x47), Rgb(0xB7, 0xD6, 0x5C),
            Rgb(0x5B, 0x2E, 0x1A), Rgb(0x8A, 0x3B, 0x12),
            Rgb(0xA9, 0x5A, 0x2A), Rgb(0xD2, 0x8A, 0x48),
            Rgb(0xD7, 0x5B, 0x18), Rgb(0xF6, 0x8B, 0x2B),
            Rgb(0xFF, 0xC0, 0x5A), Rgb(0xF2, 0xD3, 0x5A),
            Rgb(0x8F, 0x2D, 0x2D), Rgb(0xD9, 0x48, 0x3B),
            Rgb(0xF3, 0x6B, 0x4F), Rgb(0x1F, 0x4E, 0x66),
            Rgb(0x2F, 0x78, 0x91), Rgb(0x58, 0xA6, 0xB8),
            Rgb(0x8E, 0xD0, 0xCE), Rgb(0x6A, 0x4C, 0x93),
            Rgb(0x9B, 0x6B, 0xCB), Rgb(0x8C, 0x4F, 0x32),
            Rgb(0xC9, 0x82, 0x56), Rgb(0xF0, 0xB4, 0x7A),
            Rgb(0xFF, 0xD3, 0xA1), Rgb(0xD7, 0xDE, 0xE8)
        };

        private static readonly AssetSpec[] Manifest =
        {
            Multiple(
                TownTilePath,
                4, 5, 16, 16, PivotKind.Center, false,
                "tile_grass_00", "tile_grass_01", "tile_grass_02", "tile_grass_03",
                "tile_path_isolated", "tile_path_horizontal", "tile_path_vertical", "tile_path_cross",
                "tile_path_corner_ne", "tile_path_corner_se", "tile_path_corner_sw", "tile_path_corner_nw",
                "tile_path_tee_n", "tile_path_tee_e", "tile_path_tee_s", "tile_path_tee_w",
                "tile_path_end_n", "tile_path_end_e", "tile_path_end_s", "tile_path_end_w"),
            Multiple(
                "Assets/CozyTown/Art/Production/Props/prop_town_decor_16x32.png",
                4, 2, 16, 32, PivotKind.BottomCenter, true,
                "prop_tree_deciduous", "prop_shrub", "prop_flower_red", "prop_flower_yellow",
                "prop_fence_horizontal", "prop_fence_vertical", "prop_town_sign", "prop_rock"),
            Multiple(
                "Assets/CozyTown/Art/Production/Buildings/bld_town_functions_64.png",
                2, 2, 64, 64, PivotKind.BottomCenter, true,
                "bld_shop", "bld_home", "bld_kitchen", "bld_coop"),
            MultipleOverlay(
                "Assets/CozyTown/Art/Production/Buildings/bld_town_roof_foregrounds_64.png",
                2, 2, 64, 64, PivotKind.BottomCenter,
                "bld_shop_roof_foreground", "bld_home_roof_foreground",
                "bld_kitchen_roof_foreground", "bld_coop_roof_foreground"),
            Multiple(
                "Assets/CozyTown/Art/Production/Props/prop_town_functions_96x64.png",
                2, 1, 96, 64, PivotKind.BottomCenter, true,
                "prop_farm", "prop_pond"),
            Multiple(
                "Assets/CozyTown/Art/Production/Props/prop_farm_states_16.png",
                7, 2, 16, 16, PivotKind.Center, true,
                "farm_plot_soil_dry", "farm_plot_soil_watered",
                "crop_potato_stage_00", "crop_potato_stage_01", "crop_potato_stage_02",
                "crop_carrot_stage_00", "crop_carrot_stage_01", "crop_carrot_stage_02",
                "crop_carrot_stage_03", "crop_tomato_stage_00", "crop_tomato_stage_01",
                "crop_tomato_stage_02", "crop_tomato_stage_03", "crop_tomato_stage_04"),
            Multiple(
                "Assets/CozyTown/Art/Production/Props/prop_hen_states_16.png",
                3, 1, 16, 16, PivotKind.BottomCenter, true,
                "animal_hen_idle", "animal_hen_fed", "animal_hen_product_ready"),
            Multiple(
                PlayerPath,
                3, 4, 24, 32, PivotKind.BottomCenter, true,
                "chr_player_idle_down", "chr_player_walk_down_00", "chr_player_walk_down_01",
                "chr_player_idle_left", "chr_player_walk_left_00", "chr_player_walk_left_01",
                "chr_player_idle_right", "chr_player_walk_right_00", "chr_player_walk_right_01",
                "chr_player_idle_up", "chr_player_walk_up_00", "chr_player_walk_up_01"),
            Multiple(
                "Assets/CozyTown/Art/Production/Characters/npc_townsfolk_idle_down_24x32.png",
                4, 1, 24, 32, PivotKind.BottomCenter, true,
                "npc_shopkeeper_mina_idle_down", "npc_farmer_eli_idle_down",
                "npc_fisher_ren_idle_down", "npc_cook_sora_idle_down"),
            Multiple(
                "Assets/CozyTown/Art/Production/Characters/npc_portraits_48.png",
                4, 1, 48, 48, PivotKind.Center, true,
                "npc_shopkeeper_mina_portrait", "npc_farmer_eli_portrait",
                "npc_fisher_ren_portrait", "npc_cook_sora_portrait"),
            Multiple(
                "Assets/CozyTown/Art/Production/Items/item_mvp_16.png",
                6, 3, 16, 16, PivotKind.Center, true,
                "item_seed_potato", "item_seed_carrot", "item_seed_tomato",
                "item_crop_potato", "item_crop_carrot", "item_crop_tomato",
                "item_feed_chicken", "item_animal_product_egg",
                "item_fish_carp", "item_fish_trout", "item_fish_bass", "item_ingredient_salt",
                "item_ingredient_flour", "item_food_baked_potato",
                "item_food_vegetable_soup", "item_food_grilled_fish",
                "item_food_tomato_egg", "item_food_fish_pie"),
            Multiple(
                UiPath,
                4, 3, 16, 16, PivotKind.Center, true,
                "ui_panel", "ui_button_normal", "ui_button_hover", "ui_button_pressed",
                "ui_button_disabled", "ui_icon_coin", "ui_icon_clock", "ui_icon_save",
                "ui_icon_load", "ui_icon_close", "ui_marker_selection", "ui_marker_interact"),
            Single(
                SettingsIconPath,
                16, 16, PivotKind.Center, true,
                "ui_icon_settings")
        };

        [Test]
        public void ProductionAssetBatch_MatchesApprovedManifest()
        {
            var failures = new List<string>();

            foreach (AssetSpec spec in Manifest)
            {
                ValidateAsset(spec, failures);
            }

            Assert.That(
                failures,
                Is.Empty,
                "Production pixel-art manifest mismatches:\n" + string.Join("\n", failures));
        }

        [Test]
        public void UiPanel_StretchableCenter_IsFullyOpaque()
        {
            Sprite uiPanel = AssetDatabase.LoadAllAssetsAtPath(UiPath)
                .OfType<Sprite>()
                .Single(sprite => sprite.name == "ui_panel");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(UiPath), false),
                    Is.True,
                    $"Could not decode UI PNG: {UiPath}");

                int centerMinX = Mathf.RoundToInt(uiPanel.rect.xMin + uiPanel.border.x);
                int centerMaxX = Mathf.RoundToInt(uiPanel.rect.xMax - uiPanel.border.z);
                int centerMinY = Mathf.RoundToInt(uiPanel.rect.yMin + uiPanel.border.y);
                int centerMaxY = Mathf.RoundToInt(uiPanel.rect.yMax - uiPanel.border.w);
                Color32[] pixels = texture.GetPixels32();
                var nonOpaquePositions = new List<Vector2Int>();
                for (var y = centerMinY; y < centerMaxY; y++)
                {
                    for (var x = centerMinX; x < centerMaxX; x++)
                    {
                        if (pixels[y * texture.width + x].a < 255)
                        {
                            nonOpaquePositions.Add(new Vector2Int(
                                x - Mathf.RoundToInt(uiPanel.rect.xMin),
                                y - Mathf.RoundToInt(uiPanel.rect.yMin)));
                        }
                    }
                }

                Assert.That(
                    nonOpaquePositions,
                    Is.Empty,
                    $"ui_panel stretchable center contains {nonOpaquePositions.Count} "
                    + "non-opaque pixels at local positions: "
                    + string.Join(", ", nonOpaquePositions));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void UiPanel_UsesUniformThreeBandWoodFrame()
        {
            Sprite uiPanel = AssetDatabase.LoadAllAssetsAtPath(UiPath)
                .OfType<Sprite>()
                .Single(sprite => sprite.name == "ui_panel");
            Assert.That(uiPanel.rect.size, Is.EqualTo(new Vector2(16f, 16f)));
            Assert.That(uiPanel.border, Is.EqualTo(new Vector4(3f, 3f, 3f, 3f)));

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(UiPath), false),
                    Is.True,
                    $"Could not decode UI PNG: {UiPath}");

                Color32[] pixels = texture.GetPixels32();
                int spriteX = Mathf.RoundToInt(uiPanel.rect.xMin);
                int spriteY = Mathf.RoundToInt(uiPanel.rect.yMin);
                var failures = new List<string>();
                for (var localY = 0; localY < 16; localY++)
                {
                    for (var localX = 0; localX < 16; localX++)
                    {
                        int edgeDistance = Mathf.Min(
                            Mathf.Min(localX, 15 - localX),
                            Mathf.Min(localY, 15 - localY));
                        Color32 expected = edgeDistance switch
                        {
                            0 => Rgb(0x3B, 0x1F, 0x1B),
                            1 => Rgb(0x8A, 0x3B, 0x12),
                            2 => Rgb(0xC9, 0x82, 0x56),
                            _ => Rgb(0x1F, 0x1B, 0x24)
                        };
                        Color32 actual = pixels[
                            (spriteY + localY) * texture.width + spriteX + localX];
                        if (!actual.Equals(expected))
                        {
                            failures.Add(
                                $"({localX},{localY}) expected #{ColorUtility.ToHtmlStringRGBA(expected)} "
                                + $"but was #{ColorUtility.ToHtmlStringRGBA(actual)}");
                        }
                    }
                }

                Assert.That(
                    failures,
                    Is.Empty,
                    "ui_panel does not match the uniform three-band wood frame contract:\n"
                    + string.Join("\n", failures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void SettingsIcon_UsesApprovedSixteenPixelGearSilhouette()
        {
            AssertSpriteMatchesPattern(
                SettingsIconPath,
                "ui_icon_settings",
                new[]
                {
                    "......0000......",
                    "......0VV0......",
                    "..00.0VVVV0.00..",
                    "..0V00VVVV00V0..",
                    "...0VVVVVVVV0...",
                    "000VVV0000VVV000",
                    "0VVVV000000VVVV0",
                    "0VVVV000000VVVV0",
                    "0VVVV000000VVVV0",
                    "0VVVV000000VVVV0",
                    "000VVV0000VVV000",
                    "...0VVVVVVVV0...",
                    "..0V00VVVV00V0..",
                    "..00.0VVVV0.00..",
                    "......0VV0......",
                    "......0000......"
                });
        }

        [Test]
        public void InteractMarker_UsesApprovedCleanTailedBubble()
        {
            AssertSpriteMatchesPattern(
                UiPath,
                "ui_marker_interact",
                new[]
                {
                    "...1111111111...",
                    ".11SSSSSSSSSS11.",
                    "1SS0000000000SS1",
                    "1S000000000000S1",
                    "1S000000000000S1",
                    "1S000000000000S1",
                    "1S000000000000S1",
                    "1S000000000000S1",
                    "1S000000000000S1",
                    "1S000000000000S1",
                    "1SS0000000000SS1",
                    ".11SSSSSSSSSS11.",
                    "...1111111111...",
                    ".........11.....",
                    "........11......",
                    ".......11......."
                });
        }

        [Test]
        public void TownRoadTiles_MatchDeclaredEdgeConnections()
        {
            var roads = new[]
            {
                Road("tile_path_isolated", RoadConnection.None),
                Road("tile_path_horizontal", RoadConnection.East | RoadConnection.West),
                Road("tile_path_vertical", RoadConnection.North | RoadConnection.South),
                Road("tile_path_cross", RoadConnection.All),
                Road("tile_path_corner_ne", RoadConnection.North | RoadConnection.East),
                Road("tile_path_corner_se", RoadConnection.South | RoadConnection.East),
                Road("tile_path_corner_sw", RoadConnection.South | RoadConnection.West),
                Road("tile_path_corner_nw", RoadConnection.North | RoadConnection.West),
                Road("tile_path_tee_n", RoadConnection.North | RoadConnection.East | RoadConnection.West),
                Road("tile_path_tee_e", RoadConnection.North | RoadConnection.East | RoadConnection.South),
                Road("tile_path_tee_s", RoadConnection.East | RoadConnection.South | RoadConnection.West),
                Road("tile_path_tee_w", RoadConnection.North | RoadConnection.South | RoadConnection.West),
                Road("tile_path_end_n", RoadConnection.North),
                Road("tile_path_end_e", RoadConnection.East),
                Road("tile_path_end_s", RoadConnection.South),
                Road("tile_path_end_w", RoadConnection.West)
            };
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(TownTilePath), false),
                    Is.True,
                    $"Could not decode road tile PNG: {TownTilePath}");
                Assert.That(texture.width, Is.EqualTo(64));
                Assert.That(texture.height, Is.EqualTo(80));

                Color32[] pixels = texture.GetPixels32();
                var failures = new List<string>();
                Color32[] declaredEdgeSignature = null;
                for (var roadIndex = 0; roadIndex < roads.Length; roadIndex++)
                {
                    int manifestIndex = roadIndex + 4;
                    int rowFromTop = manifestIndex / 4;
                    int column = manifestIndex % 4;
                    var tileOrigin = new Vector2Int(
                        column * 16,
                        (5 - 1 - rowFromTop) * 16);
                    ValidateRoadTile(
                        pixels,
                        texture.width,
                        tileOrigin,
                        roads[roadIndex],
                        failures,
                        ref declaredEdgeSignature);
                }

                Assert.That(
                    failures,
                    Is.Empty,
                    "Road connection contract mismatches:\n" + string.Join("\n", failures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PlayerMovementFrames_ShareBottomCenterGroundLine()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(PlayerPath), false),
                    Is.True,
                    $"Could not decode player PNG: {PlayerPath}");
                Assert.That(texture.width, Is.EqualTo(72));
                Assert.That(texture.height, Is.EqualTo(128));

                Color32[] pixels = texture.GetPixels32();
                var failures = new List<string>();
                for (var frameIndex = 0; frameIndex < 12; frameIndex++)
                {
                    int rowFromTop = frameIndex / 3;
                    int column = frameIndex % 3;
                    var origin = new Vector2Int(column * 24, (4 - 1 - rowFromTop) * 32);
                    var minimumOpaqueY = 32;
                    for (var y = 0; y < 32; y++)
                    {
                        for (var x = 0; x < 24; x++)
                        {
                            if (GetPixel(pixels, texture.width, origin, x, y).a == 255)
                            {
                                minimumOpaqueY = Math.Min(minimumOpaqueY, y);
                            }
                        }
                    }

                    if (minimumOpaqueY != 0)
                    {
                        failures.Add(
                            $"frame {frameIndex} has ground line y={minimumOpaqueY}, expected 0.");
                    }
                }

                Assert.That(
                    failures,
                    Is.Empty,
                    "Player BottomCenter ground-line mismatches:\n" + string.Join("\n", failures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PlayerMovementFrames_StayInsideApprovedBodyEnvelopeAndKeepFeetConnected()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(PlayerPath), false),
                    Is.True,
                    $"Could not decode player PNG: {PlayerPath}");

                Color32[] pixels = texture.GetPixels32();
                var failures = new List<string>();
                var visibleHeights = new List<int>();
                for (var frameIndex = 0; frameIndex < 12; frameIndex++)
                {
                    int rowFromTop = frameIndex / 3;
                    int column = frameIndex % 3;
                    var origin = new Vector2Int(column * 24, (4 - 1 - rowFromTop) * 32);
                    int minimumX = 24;
                    int minimumY = 32;
                    var maximumX = -1;
                    var maximumY = -1;
                    var bottomContactPixelCount = 0;
                    var opaquePixelCount = 0;
                    var occupiedRows = new bool[32];
                    var opaquePixels = new bool[24, 32];

                    for (var y = 0; y < 32; y++)
                    {
                        for (var x = 0; x < 24; x++)
                        {
                            if (GetPixel(pixels, texture.width, origin, x, y).a != 255)
                            {
                                continue;
                            }

                            minimumX = Math.Min(minimumX, x);
                            minimumY = Math.Min(minimumY, y);
                            maximumX = Math.Max(maximumX, x);
                            maximumY = Math.Max(maximumY, y);
                            opaquePixelCount++;
                            occupiedRows[y] = true;
                            opaquePixels[x, y] = true;
                            if (y == 0)
                            {
                                bottomContactPixelCount++;
                            }
                        }
                    }

                    if (maximumY < 0)
                    {
                        failures.Add($"frame {frameIndex} contains no visible pixels");
                        continue;
                    }

                    int visibleHeight = maximumY - minimumY + 1;
                    visibleHeights.Add(visibleHeight);
                    float horizontalCenter = (minimumX + maximumX) * 0.5f;
                    if (minimumY != 0)
                    {
                        failures.Add($"frame {frameIndex} ground line is {minimumY}, expected 0");
                    }
                    if (maximumY < 29 || maximumY > 30)
                    {
                        failures.Add(
                            $"frame {frameIndex} top is {maximumY}, expected 29 or 30");
                    }
                    if (minimumX < 2 || maximumX > 21)
                    {
                        failures.Add(
                            $"frame {frameIndex} horizontal bounds are {minimumX}..{maximumX}, "
                            + "expected 2..21 or narrower");
                    }
                    if (Mathf.Abs(horizontalCenter - 11.5f) > 0.5f)
                    {
                        failures.Add(
                            $"frame {frameIndex} center is {horizontalCenter}, expected 11..12");
                    }
                    if (bottomContactPixelCount < 2)
                    {
                        failures.Add(
                            $"frame {frameIndex} has {bottomContactPixelCount} ground pixels, expected at least 2");
                    }

                    var groundedPixelCount = CountGroundedOpaquePixels(opaquePixels);
                    if (groundedPixelCount != opaquePixelCount)
                    {
                        failures.Add(
                            $"frame {frameIndex} has {opaquePixelCount - groundedPixelCount} pixels "
                            + "disconnected from the grounded body");
                    }

                    for (int y = minimumY; y <= maximumY; y++)
                    {
                        if (!occupiedRows[y])
                        {
                            failures.Add(
                                $"frame {frameIndex} has a disconnected transparent row at y={y}");
                        }
                    }
                }

                if (visibleHeights.Count > 0
                    && visibleHeights.Max() - visibleHeights.Min() > 1)
                {
                    failures.Add(
                        $"visible heights range from {visibleHeights.Min()} to {visibleHeights.Max()}, "
                        + "expected at most one pixel of walk bob");
                }

                Assert.That(
                    failures,
                    Is.Empty,
                    "Player movement envelope mismatches:\n" + string.Join("\n", failures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static int CountGroundedOpaquePixels(bool[,] opaquePixels)
        {
            const int width = 24;
            const int height = 32;
            var grounded = new bool[width, height];
            var pending = new Queue<Vector2Int>();
            for (var x = 0; x < width; x++)
            {
                if (opaquePixels[x, 0])
                {
                    grounded[x, 0] = true;
                    pending.Enqueue(new Vector2Int(x, 0));
                }
            }

            var groundedPixelCount = 0;
            var offsets = new[]
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.down,
                Vector2Int.up
            };
            while (pending.Count > 0)
            {
                Vector2Int current = pending.Dequeue();
                groundedPixelCount++;
                foreach (Vector2Int offset in offsets)
                {
                    Vector2Int neighbor = current + offset;
                    if (neighbor.x < 0 || neighbor.x >= width
                        || neighbor.y < 0 || neighbor.y >= height
                        || grounded[neighbor.x, neighbor.y]
                        || !opaquePixels[neighbor.x, neighbor.y])
                    {
                        continue;
                    }

                    grounded[neighbor.x, neighbor.y] = true;
                    pending.Enqueue(neighbor);
                }
            }

            return groundedPixelCount;
        }

        [Test]
        public void PlayerLeftAndRightMovementFrames_HaveEquivalentMirroredSilhouettes()
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(PlayerPath), false),
                    Is.True,
                    $"Could not decode player PNG: {PlayerPath}");

                Color32[] pixels = texture.GetPixels32();
                var failures = new List<string>();
                for (var frame = 0; frame < 3; frame++)
                {
                    var leftOrigin = new Vector2Int(frame * 24, 2 * 32);
                    var rightOrigin = new Vector2Int(frame * 24, 1 * 32);
                    var intersection = 0;
                    var union = 0;
                    var lowerBodyIntersection = 0;
                    var lowerBodyUnion = 0;
                    for (var y = 0; y < 32; y++)
                    {
                        for (var x = 0; x < 24; x++)
                        {
                            bool leftOpaque = GetPixel(
                                pixels,
                                texture.width,
                                leftOrigin,
                                x,
                                y).a == 255;
                            bool mirroredRightOpaque = GetPixel(
                                pixels,
                                texture.width,
                                rightOrigin,
                                23 - x,
                                y).a == 255;
                            if (leftOpaque && mirroredRightOpaque)
                            {
                                intersection++;
                            }
                            if (leftOpaque || mirroredRightOpaque)
                            {
                                union++;
                            }
                            if (y < 8 && leftOpaque && mirroredRightOpaque)
                            {
                                lowerBodyIntersection++;
                            }
                            if (y < 8 && (leftOpaque || mirroredRightOpaque))
                            {
                                lowerBodyUnion++;
                            }
                        }
                    }

                    float silhouetteIou = union == 0 ? 1f : (float)intersection / union;
                    float lowerBodyIou = lowerBodyUnion == 0
                        ? 1f
                        : (float)lowerBodyIntersection / lowerBodyUnion;
                    if (silhouetteIou < 0.9f)
                    {
                        failures.Add(
                            $"side frame {frame} mirrored silhouette IoU is {silhouetteIou:F3}, "
                            + "expected at least 0.900");
                    }
                    if (lowerBodyIou < 0.85f)
                    {
                        failures.Add(
                            $"side frame {frame} lower-body mirrored silhouette IoU is {lowerBodyIou:F3}, "
                            + "expected at least 0.850");
                    }
                }

                Assert.That(
                    failures,
                    Is.Empty,
                    "Player left/right silhouette mismatches:\n" + string.Join("\n", failures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static void ValidateRoadTile(
            IReadOnlyList<Color32> pixels,
            int textureWidth,
            Vector2Int origin,
            RoadSpec road,
            ICollection<string> failures,
            ref Color32[] declaredEdgeSignature)
        {
            foreach (RoadConnection direction in new[]
                     {
                         RoadConnection.North,
                         RoadConnection.East,
                         RoadConnection.South,
                         RoadConnection.West
                     })
            {
                Color32[] signature = GetEdgeSignature(
                    pixels,
                    textureWidth,
                    origin,
                    direction);
                bool isDeclared = (road.Connections & direction) != 0;
                for (var position = 0; position < signature.Length; position++)
                {
                    bool shouldBeRoad = isDeclared && position >= 5 && position <= 10;
                    bool isRoad = !IsGrass(signature[position]);
                    if (isRoad != shouldBeRoad)
                    {
                        failures.Add(
                            $"{road.Name}: {direction} edge position {position} "
                            + $"road={isRoad}, expected {shouldBeRoad}.");
                    }
                }

                if (!isDeclared)
                {
                    continue;
                }

                if (declaredEdgeSignature == null)
                {
                    declaredEdgeSignature = signature;
                }
                else if (!signature.SequenceEqual(declaredEdgeSignature))
                {
                    failures.Add($"{road.Name}: {direction} edge signature is inconsistent.");
                }

                ValidateCenterPath(
                    pixels,
                    textureWidth,
                    origin,
                    road.Name,
                    direction,
                    failures);
            }

            if (road.Connections == RoadConnection.None)
            {
                for (var y = 7; y <= 8; y++)
                {
                    for (var x = 7; x <= 8; x++)
                    {
                        if (IsGrass(GetPixel(pixels, textureWidth, origin, x, y)))
                        {
                            failures.Add($"{road.Name}: isolated road has no center patch.");
                        }
                    }
                }
            }
        }

        private static void ValidateCenterPath(
            IReadOnlyList<Color32> pixels,
            int textureWidth,
            Vector2Int origin,
            string roadName,
            RoadConnection direction,
            ICollection<string> failures)
        {
            for (var step = 7; step < 16; step++)
            {
                for (var lane = 7; lane <= 8; lane++)
                {
                    int x = lane;
                    int y = lane;
                    switch (direction)
                    {
                        case RoadConnection.North:
                            y = step;
                            break;
                        case RoadConnection.East:
                            x = step;
                            break;
                        case RoadConnection.South:
                            y = 15 - step;
                            break;
                        case RoadConnection.West:
                            x = 15 - step;
                            break;
                    }

                    if (IsGrass(GetPixel(pixels, textureWidth, origin, x, y)))
                    {
                        failures.Add(
                            $"{roadName}: {direction} corridor breaks at ({x}, {y}).");
                    }
                }
            }
        }

        private static Color32[] GetEdgeSignature(
            IReadOnlyList<Color32> pixels,
            int textureWidth,
            Vector2Int origin,
            RoadConnection direction)
        {
            var signature = new Color32[16];
            for (var position = 0; position < signature.Length; position++)
            {
                int x = position;
                int y = position;
                switch (direction)
                {
                    case RoadConnection.North:
                        y = 15;
                        break;
                    case RoadConnection.East:
                        x = 15;
                        break;
                    case RoadConnection.South:
                        y = 0;
                        break;
                    case RoadConnection.West:
                        x = 0;
                        break;
                }

                signature[position] = GetPixel(pixels, textureWidth, origin, x, y);
            }

            return signature;
        }

        private static Color32 GetPixel(
            IReadOnlyList<Color32> pixels,
            int textureWidth,
            Vector2Int origin,
            int localX,
            int localY)
        {
            return pixels[(origin.y + localY) * textureWidth + origin.x + localX];
        }

        private static void AssertSpriteMatchesPattern(
            string assetPath,
            string spriteName,
            IReadOnlyList<string> rowsFromTop)
        {
            Assert.That(File.Exists(assetPath), Is.True, $"Missing Production PNG: {assetPath}");
            Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(assetPath)
                .OfType<Sprite>()
                .Single(candidate => candidate.name == spriteName);
            Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(16f, 16f)));
            Assert.That(rowsFromTop.Count, Is.EqualTo(16));

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(
                    ImageConversion.LoadImage(texture, File.ReadAllBytes(assetPath), false),
                    Is.True,
                    $"Could not decode Production PNG: {assetPath}");
                Color32[] pixels = texture.GetPixels32();
                int spriteX = Mathf.RoundToInt(sprite.rect.xMin);
                int spriteY = Mathf.RoundToInt(sprite.rect.yMin);
                var failures = new List<string>();
                for (var rowFromTop = 0; rowFromTop < rowsFromTop.Count; rowFromTop++)
                {
                    string row = rowsFromTop[rowFromTop];
                    Assert.That(row.Length, Is.EqualTo(16), $"Pattern row {rowFromTop} must be 16 pixels.");
                    int localY = 15 - rowFromTop;
                    for (var localX = 0; localX < row.Length; localX++)
                    {
                        Color32 expected = DecodeUiPatternColor(row[localX]);
                        Color32 actual = pixels[
                            (spriteY + localY) * texture.width + spriteX + localX];
                        if (!actual.Equals(expected))
                        {
                            failures.Add(
                                $"({localX},{localY}) expected #{ColorUtility.ToHtmlStringRGBA(expected)} "
                                + $"but was #{ColorUtility.ToHtmlStringRGBA(actual)}");
                        }
                    }
                }

                Assert.That(
                    failures,
                    Is.Empty,
                    $"Sprite '{spriteName}' does not match its approved pixel contract:\n"
                    + string.Join("\n", failures));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static Color32 DecodeUiPatternColor(char code)
        {
            return code switch
            {
                '.' => new Color32(0, 0, 0, 0),
                '0' => Rgb(0x1F, 0x1B, 0x24),
                '1' => Rgb(0x3B, 0x1F, 0x1B),
                'S' => Rgb(0xC9, 0x82, 0x56),
                'V' => Rgb(0xD7, 0xDE, 0xE8),
                _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown UI pattern color.")
            };
        }

        private static bool IsGrass(Color32 color)
        {
            return color.Equals(new Color32(0x21, 0x4E, 0x24, 0xFF))
                   || color.Equals(new Color32(0x3E, 0x7B, 0x32, 0xFF))
                   || color.Equals(new Color32(0x76, 0xB9, 0x47, 0xFF))
                   || color.Equals(new Color32(0xB7, 0xD6, 0x5C, 0xFF));
        }

        private static Color32 Rgb(byte red, byte green, byte blue)
        {
            return new Color32(red, green, blue, 255);
        }

        private static RoadSpec Road(string name, RoadConnection connections)
        {
            return new RoadSpec(name, connections);
        }

        private static void ValidateAsset(AssetSpec spec, ICollection<string> failures)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.Path);
            if (texture == null)
            {
                failures.Add($"{spec.Path}: missing or not importable as Texture2D.");
                return;
            }

            var importer = AssetImporter.GetAtPath(spec.Path) as TextureImporter;
            if (importer == null)
            {
                failures.Add($"{spec.Path}: missing TextureImporter.");
                return;
            }

            Check(texture.width == spec.CanvasWidth, spec, failures,
                $"canvas width {texture.width}, expected {spec.CanvasWidth}");
            Check(texture.height == spec.CanvasHeight, spec, failures,
                $"canvas height {texture.height}, expected {spec.CanvasHeight}");
            ValidateSourcePng(spec, failures);
            Check(importer.textureType == TextureImporterType.Sprite, spec, failures,
                $"texture type {importer.textureType}, expected Sprite");
            Check(importer.spriteImportMode == spec.ImportMode, spec, failures,
                $"sprite mode {importer.spriteImportMode}, expected {spec.ImportMode}");
            Check(Mathf.Approximately(importer.spritePixelsPerUnit, 16f), spec, failures,
                $"PPU {importer.spritePixelsPerUnit}, expected 16");
            Check(importer.filterMode == FilterMode.Point, spec, failures,
                $"filter {importer.filterMode}, expected Point");
            Check(importer.textureCompression == TextureImporterCompression.Uncompressed,
                spec, failures, $"compression {importer.textureCompression}, expected Uncompressed");
            Check(!importer.crunchedCompression, spec, failures,
                "crunched compression must be disabled");
            Check(!importer.mipmapEnabled, spec, failures, "mipmaps must be disabled");
            Check(importer.wrapMode == TextureWrapMode.Clamp, spec, failures,
                $"wrap mode {importer.wrapMode}, expected Clamp");
            Check(importer.npotScale == TextureImporterNPOTScale.None, spec, failures,
                $"NPOT scale {importer.npotScale}, expected None");
            Check(importer.sRGBTexture, spec, failures, "sRGB must be enabled");
            Check(importer.alphaIsTransparency, spec, failures,
                "Alpha Is Transparency must be enabled");
            if (spec.RequiresSourceAlpha)
            {
                Check(importer.DoesSourceTextureHaveAlpha(), spec, failures,
                    "source PNG must contain an alpha channel");
            }

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            Check(settings.spriteMeshType == SpriteMeshType.FullRect, spec, failures,
                $"mesh type {settings.spriteMeshType}, expected FullRect");
            Check(!importer.GetPlatformTextureSettings("Standalone").overridden,
                spec, failures, "Standalone texture override must be disabled");

            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(spec.Path)
                .OfType<Sprite>()
                .ToArray();
            Check(sprites.Length == spec.SpriteNames.Length, spec, failures,
                $"sprite count {sprites.Length}, expected {spec.SpriteNames.Length}");

            var duplicateNames = sprites
                .GroupBy(sprite => sprite.name, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            Check(duplicateNames.Length == 0, spec, failures,
                "duplicate sprite names: " + string.Join(", ", duplicateNames));

            var spritesByName = sprites
                .GroupBy(sprite => sprite.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            foreach (string actualName in spritesByName.Keys)
            {
                Check(spec.SpriteNames.Contains(actualName), spec, failures,
                    $"unexpected sprite '{actualName}'");
            }

            for (var index = 0; index < spec.SpriteNames.Length; index++)
            {
                string spriteName = spec.SpriteNames[index];
                if (!spritesByName.TryGetValue(spriteName, out Sprite sprite))
                {
                    failures.Add($"{spec.Path}: missing sprite '{spriteName}'.");
                    continue;
                }

                int rowFromTop = index / spec.Columns;
                int column = index % spec.Columns;
                var expectedRect = new Rect(
                    column * spec.FrameWidth,
                    (spec.Rows - 1 - rowFromTop) * spec.FrameHeight,
                    spec.FrameWidth,
                    spec.FrameHeight);
                Check(sprite.rect == expectedRect, spec, failures,
                    $"sprite '{spriteName}' rect {sprite.rect}, expected {expectedRect}");
                Check(Mathf.Approximately(sprite.pixelsPerUnit, 16f), spec, failures,
                    $"sprite '{spriteName}' PPU {sprite.pixelsPerUnit}, expected 16");

                Vector2 expectedPivot = spec.Pivot == PivotKind.Center
                    ? new Vector2(spec.FrameWidth * 0.5f, spec.FrameHeight * 0.5f)
                    : new Vector2(spec.FrameWidth * 0.5f, 0f);
                Check(sprite.pivot == expectedPivot, spec, failures,
                    $"sprite '{spriteName}' pivot {sprite.pivot}, expected {expectedPivot}");
                Vector4 expectedBorder = spec.Path == UiPath && index < 5
                    ? new Vector4(3f, 3f, 3f, 3f)
                    : Vector4.zero;
                Check(sprite.border == expectedBorder, spec, failures,
                    $"sprite '{spriteName}' border {sprite.border}, expected {expectedBorder}");
            }
        }

        private static void ValidateSourcePng(AssetSpec spec, ICollection<string> failures)
        {
            var decodedTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(
                        decodedTexture,
                        File.ReadAllBytes(spec.Path),
                        false))
                {
                    failures.Add($"{spec.Path}: source PNG did not decode.");
                    return;
                }

                Check(decodedTexture.width == spec.CanvasWidth, spec, failures,
                    $"source PNG width {decodedTexture.width}, expected {spec.CanvasWidth}");
                Check(decodedTexture.height == spec.CanvasHeight, spec, failures,
                    $"source PNG height {decodedTexture.height}, expected {spec.CanvasHeight}");

                var transparentCount = 0;
                var opaqueCount = 0;
                var partialAlphaCount = 0;
                var offPaletteCount = 0;
                Color32[] sourcePixels = decodedTexture.GetPixels32();
                foreach (Color32 pixel in sourcePixels)
                {
                    if (pixel.a == 0)
                    {
                        transparentCount++;
                    }
                    else if (pixel.a == 255)
                    {
                        opaqueCount++;
                        if (!WarmRural32.Contains(pixel))
                        {
                            offPaletteCount++;
                        }
                    }
                    else
                    {
                        partialAlphaCount++;
                    }
                }

                Check(partialAlphaCount == 0, spec, failures,
                    $"source PNG contains {partialAlphaCount} partial-alpha pixels");
                Check(opaqueCount > 0, spec, failures,
                    "source PNG must contain at least one opaque pixel");
                Check(offPaletteCount == 0, spec, failures,
                    $"source PNG contains {offPaletteCount} opaque pixels outside WarmRural32");
                if (spec.RequiresSourceAlpha)
                {
                    Check(transparentCount > 0, spec, failures,
                        "source PNG must contain at least one transparent pixel");
                }
                else
                {
                    Check(transparentCount == 0, spec, failures,
                        $"opaque source PNG contains {transparentCount} transparent pixels");
                }

                if (spec.Pivot == PivotKind.BottomCenter && spec.RequiresBottomContact)
                {
                    ValidateBottomCenterGroundLines(spec, sourcePixels, failures);
                }

                ValidatePreview(spec, sourcePixels, failures);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decodedTexture);
            }
        }

        private static void ValidateBottomCenterGroundLines(
            AssetSpec spec,
            IReadOnlyList<Color32> pixels,
            ICollection<string> failures)
        {
            for (var frameIndex = 0; frameIndex < spec.SpriteNames.Length; frameIndex++)
            {
                int rowFromTop = frameIndex / spec.Columns;
                int column = frameIndex % spec.Columns;
                var origin = new Vector2Int(
                    column * spec.FrameWidth,
                    (spec.Rows - 1 - rowFromTop) * spec.FrameHeight);
                int minimumOpaqueY = spec.FrameHeight;
                for (var y = 0; y < spec.FrameHeight; y++)
                {
                    for (var x = 0; x < spec.FrameWidth; x++)
                    {
                        if (GetPixel(pixels, spec.CanvasWidth, origin, x, y).a == 255)
                        {
                            minimumOpaqueY = Math.Min(minimumOpaqueY, y);
                        }
                    }
                }

                Check(
                    minimumOpaqueY == 0,
                    spec,
                    failures,
                    $"BottomCenter sprite '{spec.SpriteNames[frameIndex]}' "
                    + $"has ground line y={minimumOpaqueY}, expected 0");
            }
        }

        private static void ValidatePreview(
            AssetSpec spec,
            IReadOnlyList<Color32> sourcePixels,
            ICollection<string> failures)
        {
            if (!File.Exists(spec.PreviewPath))
            {
                failures.Add($"{spec.PreviewPath}: missing 4x nearest-neighbor preview.");
                return;
            }

            var preview = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(
                        preview,
                        File.ReadAllBytes(spec.PreviewPath),
                        false))
                {
                    failures.Add($"{spec.PreviewPath}: preview PNG did not decode.");
                    return;
                }

                const int previewScale = 4;
                Check(preview.width == spec.CanvasWidth * previewScale, spec, failures,
                    $"preview width {preview.width}, expected {spec.CanvasWidth * previewScale}");
                Check(preview.height == spec.CanvasHeight * previewScale, spec, failures,
                    $"preview height {preview.height}, expected {spec.CanvasHeight * previewScale}");
                if (preview.width != spec.CanvasWidth * previewScale
                    || preview.height != spec.CanvasHeight * previewScale)
                {
                    return;
                }

                Color32[] previewPixels = preview.GetPixels32();
                for (var y = 0; y < preview.height; y++)
                {
                    for (var x = 0; x < preview.width; x++)
                    {
                        Color32 expected = sourcePixels[
                            (y / previewScale) * spec.CanvasWidth + x / previewScale];
                        Color32 actual = previewPixels[y * preview.width + x];
                        if (actual.Equals(expected))
                        {
                            continue;
                        }

                        failures.Add(
                            $"{spec.PreviewPath}: pixel ({x}, {y}) is not an exact 4x nearest-neighbor sample.");
                        return;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(preview);
            }
        }

        private static void Check(
            bool condition,
            AssetSpec spec,
            ICollection<string> failures,
            string failure)
        {
            if (!condition)
            {
                failures.Add($"{spec.Path}: {failure}.");
            }
        }

        private static AssetSpec Multiple(
            string path,
            int columns,
            int rows,
            int frameWidth,
            int frameHeight,
            PivotKind pivot,
            bool requiresSourceAlpha,
            params string[] spriteNames)
        {
            return new AssetSpec(
                path,
                columns,
                rows,
                frameWidth,
                frameHeight,
                SpriteImportMode.Multiple,
                pivot,
                requiresSourceAlpha,
                spriteNames);
        }

        private static AssetSpec MultipleOverlay(
            string path,
            int columns,
            int rows,
            int frameWidth,
            int frameHeight,
            PivotKind pivot,
            params string[] spriteNames)
        {
            return new AssetSpec(
                path,
                columns,
                rows,
                frameWidth,
                frameHeight,
                SpriteImportMode.Multiple,
                pivot,
                true,
                spriteNames,
                requiresBottomContact: false);
        }

        private static AssetSpec Single(
            string path,
            int frameWidth,
            int frameHeight,
            PivotKind pivot,
            bool requiresSourceAlpha,
            string spriteName)
        {
            return new AssetSpec(
                path,
                1,
                1,
                frameWidth,
                frameHeight,
                SpriteImportMode.Single,
                pivot,
                requiresSourceAlpha,
                new[] { spriteName });
        }

        private enum PivotKind
        {
            Center,
            BottomCenter
        }

        [Flags]
        private enum RoadConnection
        {
            None = 0,
            North = 1 << 0,
            East = 1 << 1,
            South = 1 << 2,
            West = 1 << 3,
            All = North | East | South | West
        }

        private readonly struct RoadSpec
        {
            public RoadSpec(string name, RoadConnection connections)
            {
                Name = name;
                Connections = connections;
            }

            public string Name { get; }
            public RoadConnection Connections { get; }
        }

        private sealed class AssetSpec
        {
            public AssetSpec(
                string path,
                int columns,
                int rows,
                int frameWidth,
                int frameHeight,
                SpriteImportMode importMode,
                PivotKind pivot,
                bool requiresSourceAlpha,
                string[] spriteNames,
                bool requiresBottomContact = true)
            {
                Path = path;
                Columns = columns;
                Rows = rows;
                FrameWidth = frameWidth;
                FrameHeight = frameHeight;
                ImportMode = importMode;
                Pivot = pivot;
                RequiresSourceAlpha = requiresSourceAlpha;
                RequiresBottomContact = requiresBottomContact;
                SpriteNames = spriteNames;
            }

            public string Path { get; }
            public int Columns { get; }
            public int Rows { get; }
            public int FrameWidth { get; }
            public int FrameHeight { get; }
            public int CanvasWidth => Columns * FrameWidth;
            public int CanvasHeight => Rows * FrameHeight;
            public SpriteImportMode ImportMode { get; }
            public PivotKind Pivot { get; }
            public bool RequiresSourceAlpha { get; }
            public bool RequiresBottomContact { get; }
            public string[] SpriteNames { get; }
            public string PreviewPath =>
                "ArtSource/Previews/A1/"
                + System.IO.Path.GetFileNameWithoutExtension(Path)
                + "_4x.png";
        }
    }
}
