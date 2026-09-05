using System.Collections.Generic;
using UnityEditor;

namespace CozyTown.Unity.Editor
{
    public static class CozyTownT1PixelArtBatchCatalog
    {
        public static IReadOnlyList<PixelArtBatchDefinition> CurrentT1Batch { get; } =
            new[]
            {
                Npc("shopkeeper_mina"),
                Npc("farmer_eli"),
                Npc("fisher_ren"),
                Npc("cook_sora"),
                Homes(false),
                Homes(true)
            };

        private static PixelArtBatchDefinition Npc(string owner)
        {
            string[] directions = { "down", "left", "right", "up" };
            var names = new string[12];
            var authored = new string[12];
            for (int row = 0; row < directions.Length; row++)
            {
                names[row * 3] = "npc_" + owner + "_idle_" + directions[row];
                names[row * 3 + 1] = "npc_" + owner + "_walk_" + directions[row] + "_00";
                names[row * 3 + 2] = "npc_" + owner + "_walk_" + directions[row] + "_01";
            }
            for (int index = 0; index < names.Length; index++)
                authored[index] = "ArtSource/Authored/T1/Characters/" + owner + "/" + names[index] + ".pixels";

            string fileName = "npc_" + owner + "_move_24x32";
            return new PixelArtBatchDefinition(
                "ArtSource/Generated/T1/npc_" + owner + "_source_v01.png",
                "Assets/CozyTown/Art/Production/Characters/" + fileName + ".png",
                "ArtSource/Previews/T1/" + fileName + "_4x.png",
                3, 4, 24, 32, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.Alpha,
                CozyTownPixelArtPalettes.WarmRural32, names, authoredCellSourcePaths: authored);
        }

        private static PixelArtBatchDefinition Homes(bool roof)
        {
            string[] owners = { "shopkeeper_mina", "fisher_ren", "cook_sora", "farmer_eli" };
            var names = new string[4];
            var authored = new string[4];
            for (int index = 0; index < owners.Length; index++)
            {
                names[index] = "bld_home_" + owners[index] + (roof ? "_roof_foreground" : string.Empty);
                authored[index] = "ArtSource/Authored/T1/Buildings/" + names[index] + ".pixels";
            }
            string fileName = roof ? "bld_npc_home_roofs_64" : "bld_npc_homes_64";
            return new PixelArtBatchDefinition(
                "ArtSource/Generated/T1/npc_homes_source_v01.png",
                "Assets/CozyTown/Art/Production/Buildings/" + fileName + ".png",
                "ArtSource/Previews/T1/" + fileName + "_4x.png",
                2, 2, 64, 64, 0, PixelArtPivotKind.BottomCenter, PixelArtBackgroundMode.Alpha,
                CozyTownPixelArtPalettes.WarmRural32, names, authoredCellSourcePaths: authored);
        }
    }

    public static class CozyTownT1PixelArtBatchCompiler
    {
        [MenuItem("CozyTown/Art/Build Current T1 Pixel Art Batch")]
        public static void Build()
        {
            CozyTownPixelArtBatchCompiler.BuildAll(CozyTownT1PixelArtBatchCatalog.CurrentT1Batch);
        }
    }
}
