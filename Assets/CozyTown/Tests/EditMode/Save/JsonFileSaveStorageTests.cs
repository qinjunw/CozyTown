using System;
using System.IO;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Save;
using NUnit.Framework;

namespace CozyTown.Tests.EditMode.Save
{
    public sealed class JsonFileSaveStorageTests
    {
        private string _testDirectory;
        private string _savePath;

        [SetUp]
        public void SetUp()
        {
            _testDirectory = Path.Combine(
                Path.GetTempPath(),
                "CozyTown.Tests",
                Guid.NewGuid().ToString("N"));
            _savePath = Path.Combine(_testDirectory, "main.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }

        [Test]
        public void SaveAndLoad_CurrentSchema_RoundTripsJsonPayload()
        {
            var storage = new JsonFileSaveStorage(_savePath);
            GameSaveSnapshot snapshot = SaveTestSnapshots.Create();

            Assert.That(storage.Save(JsonFileSaveStorage.MainSlotId, snapshot).IsSuccess, Is.True);

            string json = File.ReadAllText(_savePath);
            Assert.That(json, Does.Contain("\"schemaVersion\":3"));
            Assert.That(json, Does.Contain("\"worldSeed\":"));
            Assert.That(json, Does.Contain("\"characters\":"));
            Assert.That(json, Does.Contain("\"shops\":"));
            Assert.That(json, Does.Contain("\"backpack\":"));
            Assert.That(json, Does.Not.Contain("\"inventory\":"));
            Assert.That(json, Does.Contain("\"wallet\":"));
            Assert.That(json, Does.Contain("\"potato\""));

            var loaded = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(loaded.IsSuccess, Is.True);
            Assert.That(loaded.Value.Characters, Has.Length.EqualTo(1));
            Assert.That(loaded.Value.Shops, Has.Length.EqualTo(1));
            SaveTestSnapshots.AssertEquivalent(snapshot, loaded.Value);
        }

        [Test]
        public void SaveAndLoad_CurrentSchema_RoundTripsEveryCharacterAndShop()
        {
            GameSaveSnapshot baseline = SaveTestSnapshots.Create();
            var snapshot = new GameSaveSnapshot(
                baseline.SchemaVersion,
                baseline.WorldSeed,
                baseline.Clock,
                new[]
                {
                    baseline.Characters[0],
                    new CharacterEconomySnapshot(
                        "character.npc.eli",
                        new InventorySnapshot(
                            new[] { new ItemStack(DefaultMvpIds.Items.Carp, 3) }),
                        new WalletSnapshot(175))
                },
                new[]
                {
                    baseline.Shops[0],
                    new ShopEconomySnapshot(
                        "shop.town.fish",
                        new InventorySnapshot(
                            new[] { new ItemStack(DefaultMvpIds.Items.Trout, 2) }),
                        new WalletSnapshot(8000),
                        baseline.Clock.Day,
                        DeterministicShopStockReplacementPolicy.VersionOne)
                },
                baseline.Farm,
                baseline.Livestock);
            var storage = new JsonFileSaveStorage(_savePath);

            Assert.That(storage.Save(JsonFileSaveStorage.MainSlotId, snapshot).IsSuccess, Is.True);
            OperationResult<GameSaveSnapshot> loaded = storage.Load(
                JsonFileSaveStorage.MainSlotId);

            Assert.That(loaded.IsSuccess, Is.True);
            Assert.That(loaded.Value.Characters, Has.Length.EqualTo(2));
            Assert.That(loaded.Value.Shops, Has.Length.EqualTo(2));
            SaveTestSnapshots.AssertEquivalent(snapshot, loaded.Value);
        }

        [Test]
        public void Load_WhenSlotDoesNotExist_ReturnsMissingSlotError()
        {
            var storage = new JsonFileSaveStorage(_savePath);

            var result = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.slot_missing"));
        }

        [Test]
        public void Load_WhenJsonIsTruncated_ReturnsParseErrorAndPreservesFile()
        {
            Directory.CreateDirectory(_testDirectory);
            const string truncatedJson = "{\"schemaVersion\":1,\"clock\":";
            File.WriteAllText(_savePath, truncatedJson);
            var storage = new JsonFileSaveStorage(_savePath);

            var result = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.json_invalid"));
            Assert.That(File.ReadAllText(_savePath), Is.EqualTo(truncatedJson));
        }

        [TestCase(0)]
        [TestCase(GameSaveSnapshot.CurrentSchemaVersion + 1)]
        public void Load_WhenSchemaIsUnsupported_ReturnsVersionErrorAndPreservesFile(int schemaVersion)
        {
            var storage = new JsonFileSaveStorage(_savePath);
            Assert.That(
                storage.Save(JsonFileSaveStorage.MainSlotId, SaveTestSnapshots.Create()).IsSuccess,
                Is.True);
            string json = File.ReadAllText(_savePath)
                .Replace("\"schemaVersion\":3", $"\"schemaVersion\":{schemaVersion}");
            File.WriteAllText(_savePath, json);

            var result = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.schema_unsupported"));
            Assert.That(File.ReadAllText(_savePath), Is.EqualTo(json));
        }

        [Test]
        public void Load_WhenRequiredSectionIsMissing_ReturnsPayloadError()
        {
            var storage = new JsonFileSaveStorage(_savePath);
            Assert.That(
                storage.Save(JsonFileSaveStorage.MainSlotId, SaveTestSnapshots.Create()).IsSuccess,
                Is.True);
            string json = File.ReadAllText(_savePath);
            string withoutCharacters = json.Replace(
                "\"characters\":",
                "\"omittedCharacters\":");
            File.WriteAllText(_savePath, withoutCharacters);

            var result = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.payload_invalid"));
        }

        [Test]
        public void Load_LegacyV1Fixture_MigratesDeterministicallyWithoutRewritingFile()
        {
            const string legacyJson = "{\n"
                + "  \"schemaVersion\": 1,\n"
                + "  \"clock\": { \"day\": 3, \"minuteOfDay\": 420 },\n"
                + "  \"inventory\": { \"items\": [\n"
                + "    { \"itemId\": \"crop.potato\", \"quantity\": 2 },\n"
                + "    { \"itemId\": \"fish.carp\", \"quantity\": 1 }\n"
                + "  ] },\n"
                + "  \"wallet\": { \"balance\": 425 },\n"
                + "  \"farm\": { \"lastProcessedDay\": 3, \"plots\": [\n"
                + "    { \"plotId\": \"plot.01\", \"cropId\": \"crop_definition.potato\", \"growthProgressDays\": 1, \"wateredToday\": true, \"status\": 1 },\n"
                + "    { \"plotId\": \"plot.02\", \"cropId\": \"\", \"growthProgressDays\": 0, \"wateredToday\": false, \"status\": 0 },\n"
                + "    { \"plotId\": \"plot.03\", \"cropId\": \"\", \"growthProgressDays\": 0, \"wateredToday\": false, \"status\": 0 },\n"
                + "    { \"plotId\": \"plot.04\", \"cropId\": \"\", \"growthProgressDays\": 0, \"wateredToday\": false, \"status\": 0 },\n"
                + "    { \"plotId\": \"plot.05\", \"cropId\": \"\", \"growthProgressDays\": 0, \"wateredToday\": false, \"status\": 0 },\n"
                + "    { \"plotId\": \"plot.06\", \"cropId\": \"\", \"growthProgressDays\": 0, \"wateredToday\": false, \"status\": 0 }\n"
                + "  ] },\n"
                + "  \"livestock\": { \"lastProcessedDay\": 3, \"animals\": [\n"
                + "    { \"animalId\": \"animal.hen_01\", \"speciesId\": \"species.chicken\", \"fedToday\": true, \"productReady\": false }\n"
                + "  ] }\n"
                + "}";
            Directory.CreateDirectory(_testDirectory);
            File.WriteAllText(_savePath, legacyJson);
            byte[] legacyBytes = File.ReadAllBytes(_savePath);
            var storage = new JsonFileSaveStorage(_savePath);

            OperationResult<GameSaveSnapshot> first = storage.Load(
                JsonFileSaveStorage.MainSlotId);
            OperationResult<GameSaveSnapshot> second = storage.Load(
                JsonFileSaveStorage.MainSlotId);

            Assert.That(first.IsSuccess, Is.True);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(first.Value.SchemaVersion, Is.EqualTo(3));
            Assert.That(first.Value.WorldSeed, Is.EqualTo(JsonFileSaveStorage.LegacyV1WorldSeed));
            Assert.That(first.Value.Clock.Day, Is.EqualTo(3));
            Assert.That(first.Value.Clock.MinuteOfDay, Is.EqualTo(420));
            Assert.That(first.Value.Farm.LastProcessedDay, Is.EqualTo(3));
            Assert.That(first.Value.Farm.Plots, Has.Length.EqualTo(6));
            Assert.That(first.Value.Farm.Plots[0], Is.EqualTo(new FarmPlotSnapshot(
                "plot.01", DefaultMvpIds.Crops.Potato, 1, true, FarmPlotStatus.Growing)));
            Assert.That(first.Value.Livestock.LastProcessedDay, Is.EqualTo(3));
            Assert.That(first.Value.Livestock.Animals, Is.EqualTo(new[]
            {
                new AnimalSnapshot("animal.hen_01", "species.chicken", true, false)
            }));
            Assert.That(first.Value.Characters, Has.Length.EqualTo(1));
            Assert.That(
                first.Value.Characters[0].CharacterId,
                Is.EqualTo(DefaultMvpIds.Characters.Player));
            Assert.That(first.Value.Characters[0].Wallet.Balance, Is.EqualTo(425));
            Assert.That(
                first.Value.Characters[0].Backpack.Items,
                Is.EqualTo(new[]
                {
                    new ItemStack(DefaultMvpIds.Items.Potato, 2),
                    new ItemStack(DefaultMvpIds.Items.Carp, 1)
                }));
            Assert.That(first.Value.Shops, Has.Length.EqualTo(1));
            Assert.That(
                first.Value.Shops[0].ShopId,
                Is.EqualTo(DefaultMvpIds.Shops.TownGeneral));
            Assert.That(first.Value.Shops[0].Wallet.Balance, Is.EqualTo(10000));
            Assert.That(first.Value.Shops[0].LastRestockedDay, Is.EqualTo(3));
            Assert.That(
                first.Value.Shops[0].Stock.Items,
                Is.EqualTo(new[]
                {
                    new ItemStack(DefaultMvpIds.Items.CarrotSeed, 5),
                    new ItemStack(DefaultMvpIds.Items.TomatoSeed, 4),
                    new ItemStack(DefaultMvpIds.Items.ChickenFeed, 12),
                    new ItemStack(DefaultMvpIds.Items.Flour, 7)
                }));
            var configuration = DefaultMvpContent.CreateConfiguration();
            var policy = new DeterministicShopStockReplacementPolicy(
                configuration.ShopRestockRules,
                minimumDistinctItems: 4);
            OperationResult<ShopEconomySnapshot> sameDay = policy.CreateCandidate(
                first.Value.WorldSeed,
                first.Value.Shops[0],
                first.Value.Clock.Day);
            Assert.That(sameDay.IsSuccess, Is.True);
            Assert.That(
                sameDay.Value.Stock.Items,
                Is.EqualTo(first.Value.Shops[0].Stock.Items));
            SaveTestSnapshots.AssertEquivalent(first.Value, second.Value);
            CozyTownServices restored = CozyTownCompositionRoot.CreateDefault();
            var coordinator = new GameSaveCoordinator(
                restored.WorldSeed,
                restored.Time,
                restored.EconomyState,
                restored.Farm,
                restored.Livestock,
                storage);
            OperationResult restoredResult = coordinator.Load();
            Assert.That(restoredResult.IsSuccess, Is.True, restoredResult.ErrorCode);
            SaveTestSnapshots.AssertEquivalent(first.Value, SaveTestSnapshots.Capture(restored));
            Assert.That(File.ReadAllBytes(_savePath), Is.EqualTo(legacyBytes));
        }

        [Test]
        public void Load_WhenJsonFieldHasWrongType_ReturnsStableParseError()
        {
            var storage = new JsonFileSaveStorage(_savePath);
            Assert.That(
                storage.Save(JsonFileSaveStorage.MainSlotId, SaveTestSnapshots.Create()).IsSuccess,
                Is.True);
            string invalid = File.ReadAllText(_savePath)
                .Replace("\"quantity\":2", "\"quantity\":\"not-a-number\"");
            File.WriteAllText(_savePath, invalid);

            var result = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.json_invalid"));
            Assert.That(File.ReadAllText(_savePath), Is.EqualTo(invalid));
        }

        [Test]
        public void Load_WhenItemQuantityIsNegative_ReturnsPayloadErrorAndPreservesFile()
        {
            var storage = new JsonFileSaveStorage(_savePath);
            Assert.That(
                storage.Save(JsonFileSaveStorage.MainSlotId, SaveTestSnapshots.Create()).IsSuccess,
                Is.True);
            string invalid = File.ReadAllText(_savePath)
                .Replace("\"quantity\":2", "\"quantity\":-2");
            File.WriteAllText(_savePath, invalid);

            var result = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.payload_invalid"));
            Assert.That(File.ReadAllText(_savePath), Is.EqualTo(invalid));
        }

        [Test]
        public void Save_WhenTemporaryWriteFails_PreservesPreviousValidSave()
        {
            var storage = new JsonFileSaveStorage(_savePath);
            GameSaveSnapshot original = SaveTestSnapshots.Create();
            Assert.That(storage.Save(JsonFileSaveStorage.MainSlotId, original).IsSuccess, Is.True);
            Directory.CreateDirectory(_savePath + ".tmp");
            GameSaveSnapshot replacement = SaveTestSnapshots.Create(walletBalance: 999);

            var saveResult = storage.Save(JsonFileSaveStorage.MainSlotId, replacement);
            var loaded = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(saveResult.IsSuccess, Is.False);
            Assert.That(saveResult.ErrorCode, Is.EqualTo("save.write_failed"));
            Assert.That(loaded.IsSuccess, Is.True);
            SaveTestSnapshots.AssertEquivalent(original, loaded.Value);
        }

        [Test]
        public void Save_WhenReplacingLockedFileFails_PreservesPreviousValidSave()
        {
            var storage = new JsonFileSaveStorage(_savePath);
            GameSaveSnapshot original = SaveTestSnapshots.Create();
            Assert.That(storage.Save(JsonFileSaveStorage.MainSlotId, original).IsSuccess, Is.True);
            GameSaveSnapshot replacement = SaveTestSnapshots.Create(walletBalance: 999);

            OperationResult saveResult;
            using (new FileStream(
                _savePath,
                FileMode.Open,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                saveResult = storage.Save(JsonFileSaveStorage.MainSlotId, replacement);
            }

            var loaded = storage.Load(JsonFileSaveStorage.MainSlotId);
            Assert.That(saveResult.IsSuccess, Is.False);
            Assert.That(saveResult.ErrorCode, Is.EqualTo("save.write_failed"));
            Assert.That(loaded.IsSuccess, Is.True);
            SaveTestSnapshots.AssertEquivalent(original, loaded.Value);
        }

        [Test]
        public void Storage_RejectsAnySlotExceptMain()
        {
            var storage = new JsonFileSaveStorage(_savePath);

            Assert.That(storage.Exists("secondary"), Is.False);
            Assert.That(storage.Save("secondary", SaveTestSnapshots.Create()).ErrorCode,
                Is.EqualTo("save.slot_invalid"));
            Assert.That(storage.Load("secondary").ErrorCode, Is.EqualTo("save.slot_invalid"));
        }
    }
}
