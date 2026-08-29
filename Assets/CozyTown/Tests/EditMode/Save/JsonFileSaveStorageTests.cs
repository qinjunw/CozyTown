using System;
using System.IO;
using CozyTown.Runtime.Core;
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
            Assert.That(json, Does.Contain("\"schemaVersion\":1"));
            Assert.That(json, Does.Contain("\"potato\""));

            var loaded = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(loaded.IsSuccess, Is.True);
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
                .Replace("\"schemaVersion\":1", $"\"schemaVersion\":{schemaVersion}");
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
            int inventoryStart = json.IndexOf(",\"inventory\":", StringComparison.Ordinal);
            int walletStart = json.IndexOf(",\"wallet\":", StringComparison.Ordinal);
            string withoutInventory = json.Remove(inventoryStart, walletStart - inventoryStart);
            File.WriteAllText(_savePath, withoutInventory);

            var result = storage.Load(JsonFileSaveStorage.MainSlotId);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("save.payload_invalid"));
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
