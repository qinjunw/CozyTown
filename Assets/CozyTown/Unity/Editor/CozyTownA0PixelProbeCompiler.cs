using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownPixelArtBatchCatalog
    {
        private static readonly Color32[] A0CarrotPalette =
        {
            new Color32(59, 31, 27, 255),
            new Color32(33, 78, 36, 255),
            new Color32(62, 123, 50, 255),
            new Color32(118, 185, 71, 255),
            new Color32(138, 59, 18, 255),
            new Color32(215, 91, 24, 255),
            new Color32(246, 139, 43, 255),
            new Color32(255, 192, 90, 255)
        };

        public static readonly PixelArtBatchDefinition A0CarrotProbe =
            new PixelArtBatchDefinition(
                "ArtSource/Generated/A0/item_crop_carrot_source.png",
                "Assets/CozyTown/Art/References/A0/a0_item_crop_carrot.png",
                "ArtSource/Previews/A0/item_crop_carrot_4x.png",
                1,
                1,
                16,
                16,
                1,
                PixelArtPivotKind.Center,
                PixelArtBackgroundMode.Alpha,
                A0CarrotPalette,
                new[] { "a0_item_crop_carrot" });

        public static readonly PixelArtBatchDefinition A1Items =
            new PixelArtBatchDefinition(
                "ArtSource/Generated/A1/items_source.png",
                "Assets/CozyTown/Art/Production/Items/item_mvp_16.png",
                "ArtSource/Previews/A1/item_mvp_16_4x.png",
                6,
                3,
                16,
                16,
                1,
                PixelArtPivotKind.Center,
                PixelArtBackgroundMode.WhiteBackground,
                CozyTownPixelArtPalettes.WarmRural32,
                new[]
                {
                    "item_seed_potato", "item_seed_carrot", "item_seed_tomato",
                    "item_crop_potato", "item_crop_carrot", "item_crop_tomato",
                    "item_feed_chicken", "item_animal_product_egg",
                    "item_fish_carp", "item_fish_trout", "item_fish_bass",
                    "item_ingredient_salt", "item_ingredient_flour",
                    "item_food_baked_potato", "item_food_vegetable_soup",
                    "item_food_grilled_fish", "item_food_tomato_egg",
                    "item_food_fish_pie"
                });

        public static readonly PixelArtBatchDefinition A1TownTiles =
            new PixelArtBatchDefinition(
                "ArtSource/Generated/A1/tiles_source.png",
                "Assets/CozyTown/Art/Production/Environment/Tiles/tile_town_base_16.png",
                "ArtSource/Previews/A1/tile_town_base_16_4x.png",
                4,
                5,
                16,
                16,
                0,
                PixelArtPivotKind.Center,
                PixelArtBackgroundMode.Alpha,
                CozyTownPixelArtPalettes.WarmRural32,
                new[]
                {
                    "tile_grass_00", "tile_grass_01", "tile_grass_02", "tile_grass_03",
                    "tile_path_isolated", "tile_path_horizontal", "tile_path_vertical", "tile_path_cross",
                    "tile_path_corner_ne", "tile_path_corner_se", "tile_path_corner_sw", "tile_path_corner_nw",
                    "tile_path_tee_n", "tile_path_tee_e", "tile_path_tee_s", "tile_path_tee_w",
                    "tile_path_end_n", "tile_path_end_e", "tile_path_end_s", "tile_path_end_w"
                },
                opaqueOutput: true,
                opaqueFillColor: new Color32(0x76, 0xB9, 0x47, 0xFF),
                sourceCellIndices: new[]
                {
                    0, 1, 2, 3,
                    4, 5, 6, 7,
                    8, 9, 10, 11,
                    12, 13, 14, 15,
                    18, 19, 16, 17
                },
                roadConnections: new[]
                {
                    PixelArtRoadConnection.NotRoad,
                    PixelArtRoadConnection.NotRoad,
                    PixelArtRoadConnection.NotRoad,
                    PixelArtRoadConnection.NotRoad,
                    PixelArtRoadConnection.None,
                    PixelArtRoadConnection.East | PixelArtRoadConnection.West,
                    PixelArtRoadConnection.North | PixelArtRoadConnection.South,
                    PixelArtRoadConnection.All,
                    PixelArtRoadConnection.North | PixelArtRoadConnection.East,
                    PixelArtRoadConnection.South | PixelArtRoadConnection.East,
                    PixelArtRoadConnection.South | PixelArtRoadConnection.West,
                    PixelArtRoadConnection.North | PixelArtRoadConnection.West,
                    PixelArtRoadConnection.North | PixelArtRoadConnection.East | PixelArtRoadConnection.West,
                    PixelArtRoadConnection.North | PixelArtRoadConnection.East | PixelArtRoadConnection.South,
                    PixelArtRoadConnection.East | PixelArtRoadConnection.South | PixelArtRoadConnection.West,
                    PixelArtRoadConnection.North | PixelArtRoadConnection.South | PixelArtRoadConnection.West,
                    PixelArtRoadConnection.North,
                    PixelArtRoadConnection.East,
                    PixelArtRoadConnection.South,
                    PixelArtRoadConnection.West
                });

        public static readonly PixelArtBatchDefinition A1TownDecor = A1(
            "decor_source.png",
            "Props/prop_town_decor_16x32.png",
            4, 2, 16, 32, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.WhiteBackground,
            "prop_tree_deciduous", "prop_shrub", "prop_flower_red", "prop_flower_yellow",
            "prop_fence_horizontal", "prop_fence_vertical", "prop_town_sign", "prop_rock");

        public static readonly PixelArtBatchDefinition A1Buildings = A1(
            "buildings_source.png",
            "Buildings/bld_town_functions_64.png",
            2, 2, 64, 64, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.Alpha,
            "bld_shop", "bld_home", "bld_kitchen", "bld_coop");

        public static readonly PixelArtBatchDefinition A1TownFunctions = A1(
            "town_functions_source.png",
            "Props/prop_town_functions_96x64.png",
            2, 1, 96, 64, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.Alpha,
            "prop_farm", "prop_pond");

        public static readonly PixelArtBatchDefinition A1FarmStates = A1(
            "farm_states_source.png",
            "Props/prop_farm_states_16.png",
            7, 2, 16, 16, 0, PixelArtPivotKind.Center, PixelArtBackgroundMode.Alpha,
            "farm_plot_soil_dry", "farm_plot_soil_watered",
            "crop_potato_stage_00", "crop_potato_stage_01", "crop_potato_stage_02",
            "crop_carrot_stage_00", "crop_carrot_stage_01", "crop_carrot_stage_02",
            "crop_carrot_stage_03", "crop_tomato_stage_00", "crop_tomato_stage_01",
            "crop_tomato_stage_02", "crop_tomato_stage_03", "crop_tomato_stage_04");

        public static readonly PixelArtBatchDefinition A1HenStates = A1(
            "hen_states_source.png",
            "Props/prop_hen_states_16.png",
            3, 1, 16, 16, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.Alpha,
            "animal_hen_idle", "animal_hen_fed", "animal_hen_product_ready");

        public static readonly PixelArtBatchDefinition A1Player = A1(
            "player_source.png",
            "Characters/chr_player_move_16x24.png",
            3, 4, 16, 24, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.WhiteBackground,
            "chr_player_idle_down", "chr_player_walk_down_00", "chr_player_walk_down_01",
            "chr_player_idle_left", "chr_player_walk_left_00", "chr_player_walk_left_01",
            "chr_player_idle_right", "chr_player_walk_right_00", "chr_player_walk_right_01",
            "chr_player_idle_up", "chr_player_walk_up_00", "chr_player_walk_up_01");

        public static readonly PixelArtBatchDefinition A1MinaWorld = A1(
            "mina_source.png",
            "Characters/npc_shopkeeper_mina_idle_down.png",
            1, 1, 16, 24, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.WhiteBackground,
            "npc_shopkeeper_mina_idle_down");

        public static readonly PixelArtBatchDefinition A1NpcWorld =
            new PixelArtBatchDefinition(
                "ArtSource/Generated/A1/portraits_source.png",
                "Assets/CozyTown/Art/Production/Characters/npc_townsfolk_idle_down_16x24.png",
                "ArtSource/Previews/A1/npc_townsfolk_idle_down_16x24_4x.png",
                4,
                1,
                16,
                24,
                0,
                PixelArtPivotKind.BottomCenter,
                PixelArtBackgroundMode.Alpha,
                CozyTownPixelArtPalettes.WarmRural32,
                new[]
                {
                    "npc_shopkeeper_mina_idle_down",
                    "npc_farmer_eli_idle_down",
                    "npc_fisher_ren_idle_down",
                    "npc_cook_sora_idle_down"
                },
                authoredCellSourcePaths: new[]
                {
                    "ArtSource/Authored/A1/npc_shopkeeper_mina_idle_down.pixels",
                    "ArtSource/Authored/A1/npc_farmer_eli_idle_down.pixels",
                    "ArtSource/Authored/A1/npc_fisher_ren_idle_down.pixels",
                    "ArtSource/Authored/A1/npc_cook_sora_idle_down.pixels"
                });

        public static readonly PixelArtBatchDefinition A1NpcPortraits = A1(
            "portraits_source.png",
            "Characters/npc_portraits_48.png",
            4, 1, 48, 48, 1, PixelArtPivotKind.Center, PixelArtBackgroundMode.WhiteBackground,
            "npc_shopkeeper_mina_portrait", "npc_farmer_eli_portrait",
            "npc_fisher_ren_portrait", "npc_cook_sora_portrait");

        public static readonly PixelArtBatchDefinition A1Ui =
            new PixelArtBatchDefinition(
                "ArtSource/Generated/A1/ui_source.png",
                "Assets/CozyTown/Art/Production/UI/ui_mvp_16.png",
                "ArtSource/Previews/A1/ui_mvp_16_4x.png",
                4,
                3,
                16,
                16,
                0,
                PixelArtPivotKind.Center,
                PixelArtBackgroundMode.Alpha,
                CozyTownPixelArtPalettes.WarmRural32,
                new[]
                {
                    "ui_panel", "ui_button_normal", "ui_button_hover", "ui_button_pressed",
                    "ui_button_disabled", "ui_icon_coin", "ui_icon_clock", "ui_icon_save",
                    "ui_icon_load", "ui_icon_close", "ui_marker_selection", "ui_marker_interact"
                },
                spriteBorders: new[]
                {
                    new Vector4(3f, 3f, 3f, 3f),
                    new Vector4(3f, 3f, 3f, 3f),
                    new Vector4(3f, 3f, 3f, 3f),
                    new Vector4(3f, 3f, 3f, 3f),
                    new Vector4(3f, 3f, 3f, 3f),
                    Vector4.zero,
                    Vector4.zero,
                    Vector4.zero,
                    Vector4.zero,
                    Vector4.zero,
                    Vector4.zero,
                    Vector4.zero
                },
                insetBandColors: new Color32[][]
                {
                    new[]
                    {
                        new Color32(0x3B, 0x1F, 0x1B, 0xFF),
                        new Color32(0x8A, 0x3B, 0x12, 0xFF),
                        new Color32(0xC9, 0x82, 0x56, 0xFF),
                        new Color32(0x1F, 0x1B, 0x24, 0xFF)
                    },
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                },
                authoredCellSourcePaths: new[]
                {
                    null, null, null, null,
                    null, null, null, null,
                    null, null, null,
                    "ArtSource/Authored/A1/ui_marker_interact.pixels"
                });

        public static readonly PixelArtBatchDefinition A1SettingsIcon =
            new PixelArtBatchDefinition(
                "ArtSource/Authored/A1/ui_icon_settings.pixels",
                "Assets/CozyTown/Art/Production/UI/ui_icon_settings.png",
                "ArtSource/Previews/A1/ui_icon_settings_4x.png",
                1,
                1,
                16,
                16,
                0,
                PixelArtPivotKind.Center,
                PixelArtBackgroundMode.Alpha,
                CozyTownPixelArtPalettes.WarmRural32,
                new[] { "ui_icon_settings" });

        public static IReadOnlyList<PixelArtBatchDefinition> CurrentA1Batch { get; } =
            new[]
            {
                A1TownTiles,
                A1TownDecor,
                A1Buildings,
                A1TownFunctions,
                A1FarmStates,
                A1HenStates,
                A1Player,
                A1MinaWorld,
                A1NpcWorld,
                A1NpcPortraits,
                A1Items,
                A1Ui,
                A1SettingsIcon
            };

        private static PixelArtBatchDefinition A1(
            string sourceFileName,
            string outputRelativePath,
            int columns,
            int rows,
            int frameWidth,
            int frameHeight,
            int contentPadding,
            PixelArtPivotKind pivot,
            PixelArtBackgroundMode backgroundMode,
            params string[] spriteNames)
        {
            string outputFileName = System.IO.Path.GetFileNameWithoutExtension(outputRelativePath);
            return new PixelArtBatchDefinition(
                "ArtSource/Generated/A1/" + sourceFileName,
                "Assets/CozyTown/Art/Production/" + outputRelativePath,
                "ArtSource/Previews/A1/" + outputFileName + "_4x.png",
                columns,
                rows,
                frameWidth,
                frameHeight,
                contentPadding,
                pivot,
                backgroundMode,
                CozyTownPixelArtPalettes.WarmRural32,
                spriteNames);
        }
    }

    public static class CozyTownA0PixelProbeCompiler
    {
        [MenuItem("CozyTown/Art/Build A0 Carrot Pixel Probe")]
        public static void Build()
        {
            CozyTownPixelArtBatchCompiler.BuildAll(
                new[] { CozyTownPixelArtBatchCatalog.A0CarrotProbe });
        }
    }

    public static class CozyTownA1PixelArtBatchCompiler
    {
        [MenuItem("CozyTown/Art/Build Current A1 Pixel Art Batch")]
        public static void Build()
        {
            CozyTownPixelArtBatchCompiler.BuildAll(
                CozyTownPixelArtBatchCatalog.CurrentA1Batch);
        }
    }
}
