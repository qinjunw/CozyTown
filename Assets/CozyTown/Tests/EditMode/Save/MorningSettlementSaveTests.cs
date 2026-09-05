using System;
using System.IO;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Save;
using CozyTown.Runtime.Time;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    public sealed class MorningSettlementSaveTests
    {
        private const string LegacyV2BeforeMorning = @"{
  ""schemaVersion"": 2,
  ""worldSeed"": 12345,
  ""clock"": { ""day"": 2, ""minuteOfDay"": 60 },
  ""characters"": [{
    ""characterId"": ""character.player"",
    ""backpack"": { ""items"": [{ ""itemId"": ""crop.potato"", ""quantity"": 2 }] },
    ""wallet"": { ""balance"": 425 }
  }],
  ""shops"": [{
    ""shopId"": ""shop.town.general"",
    ""stock"": { ""items"": [{ ""itemId"": ""fish.carp"", ""quantity"": 7 }] },
    ""wallet"": { ""balance"": 9000 },
    ""lastRestockedDay"": 2,
    ""restockAlgorithmVersion"": 1
  }],
  ""farm"": { ""lastProcessedDay"": 2, ""plots"": [
    { ""plotId"": ""plot.01"", ""cropId"": ""crop_definition.potato"", ""growthProgressDays"": 1, ""wateredToday"": true, ""status"": 1 },
    { ""plotId"": ""plot.02"", ""cropId"": """", ""growthProgressDays"": 0, ""wateredToday"": false, ""status"": 0 },
    { ""plotId"": ""plot.03"", ""cropId"": """", ""growthProgressDays"": 0, ""wateredToday"": false, ""status"": 0 },
    { ""plotId"": ""plot.04"", ""cropId"": """", ""growthProgressDays"": 0, ""wateredToday"": false, ""status"": 0 },
    { ""plotId"": ""plot.05"", ""cropId"": """", ""growthProgressDays"": 0, ""wateredToday"": false, ""status"": 0 },
    { ""plotId"": ""plot.06"", ""cropId"": """", ""growthProgressDays"": 0, ""wateredToday"": false, ""status"": 0 }
  ] },
  ""livestock"": { ""lastProcessedDay"": 2, ""animals"": [
    { ""animalId"": ""animal.hen_01"", ""speciesId"": ""species.chicken"", ""fedToday"": true, ""productReady"": false }
  ] }
}";

        [Test]
        public void SaveAndLoad_BeforeMorningSettlement_PreservesPreviousDayProgress()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CozyTown.Tests",
                Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(directory, "main.json");
                CozyTownServices source = CozyTownCompositionRoot.CreateDefault();
                Assert.That(source.Time.Restore(new GameClockSnapshot(2, 10)).IsSuccess, Is.True);
                GameSaveSnapshot expected = SaveTestSnapshots.Capture(source);
                var storage = new JsonFileSaveStorage(path);
                var sourceCoordinator = CreateCoordinator(source, storage);

                OperationResult saved = sourceCoordinator.Save();

                Assert.That(saved.IsSuccess, Is.True, saved.ErrorCode);
                Assert.That(File.ReadAllText(path), Does.Contain("\"schemaVersion\":3"));
                CozyTownServices restored = CozyTownCompositionRoot.CreateDefault();
                OperationResult loaded = CreateCoordinator(restored, storage).Load();
                Assert.That(loaded.IsSuccess, Is.True, loaded.ErrorCode);
                SaveTestSnapshots.AssertEquivalent(expected, SaveTestSnapshots.Capture(restored));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public void Load_LegacyV2BeforeMorning_PreservesCompletedDayAndActualAssets()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "CozyTown.Tests",
                Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "main.json");
                File.WriteAllText(path, LegacyV2BeforeMorning);
                byte[] originalBytes = File.ReadAllBytes(path);
                CozyTownServices services = CozyTownCompositionRoot.CreateDefault();
                var coordinator = CreateCoordinator(services, new JsonFileSaveStorage(path));

                OperationResult loaded = coordinator.Load();

                Assert.That(loaded.IsSuccess, Is.True, loaded.ErrorCode);
                GameSaveSnapshot first = SaveTestSnapshots.Capture(services);
                Assert.That(first.SchemaVersion, Is.EqualTo(3));
                Assert.That(first.WorldSeed, Is.EqualTo(12345));
                Assert.That(first.Clock.Day, Is.EqualTo(2));
                Assert.That(first.Clock.MinuteOfDay, Is.EqualTo(60));
                Assert.That(first.Farm.LastProcessedDay, Is.EqualTo(2));
                Assert.That(first.Livestock.LastProcessedDay, Is.EqualTo(2));
                Assert.That(first.Shops[0].LastRestockedDay, Is.EqualTo(2));
                Assert.That(first.Characters[0].Wallet.Balance, Is.EqualTo(425));
                Assert.That(first.Characters[0].Backpack.Items,
                    Is.EqualTo(new[] { new ItemStack(DefaultMvpIds.Items.Potato, 2) }));
                Assert.That(first.Shops[0].Wallet.Balance, Is.EqualTo(9000));
                Assert.That(first.Shops[0].Stock.Items,
                    Is.EqualTo(new[] { new ItemStack(DefaultMvpIds.Items.Carp, 7) }));
                Assert.That(first.Farm.Plots[0], Is.EqualTo(new FarmPlotSnapshot(
                    "plot.01", DefaultMvpIds.Crops.Potato, 1, true, FarmPlotStatus.Growing)));
                Assert.That(first.Livestock.Animals[0].FedToday, Is.True);
                Assert.That(first.Livestock.Animals[0].ProductReady, Is.False);
                Assert.That(coordinator.Load().IsSuccess, Is.True);
                SaveTestSnapshots.AssertEquivalent(first, SaveTestSnapshots.Capture(services));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(originalBytes));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static GameSaveCoordinator CreateCoordinator(
            CozyTownServices services,
            ISaveStorage storage)
        {
            return new GameSaveCoordinator(
                services.WorldSeed,
                services.Time,
                services.EconomyState,
                services.Farm,
                services.Livestock,
                storage);
        }
    }
}
