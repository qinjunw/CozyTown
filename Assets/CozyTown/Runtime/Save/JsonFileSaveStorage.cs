using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Xml;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Save
{
    public sealed class JsonFileSaveStorage : ISaveStorage
    {
        public const string MainSlotId = "main";
        public const int LegacyV1WorldSeed = 1;

        private readonly string _filePath;
        private readonly string _temporaryPath;
        private readonly IShopStockReplacementPolicy _legacyRestockPolicy;

        public JsonFileSaveStorage(string filePath)
            : this(filePath, CreateDefaultLegacyRestockPolicy())
        {
        }

        public JsonFileSaveStorage(
            string filePath,
            IShopStockReplacementPolicy legacyRestockPolicy)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A save file path is required.", nameof(filePath));
            }

            _filePath = Path.GetFullPath(filePath);
            _temporaryPath = _filePath + ".tmp";
            _legacyRestockPolicy = legacyRestockPolicy
                ?? throw new ArgumentNullException(nameof(legacyRestockPolicy));
        }

        public bool Exists(string slotId)
        {
            return IsMainSlot(slotId) && File.Exists(_filePath);
        }

        public OperationResult Save(string slotId, GameSaveSnapshot snapshot)
        {
            if (!IsMainSlot(slotId))
            {
                return OperationResult.Failure("save.slot_invalid");
            }

            OperationResult validation = GameSaveSnapshotValidator.Validate(snapshot);
            if (!validation.IsSuccess)
            {
                return validation;
            }

            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                WriteSnapshot(_temporaryPath, snapshot);
                OperationResult<GameSaveSnapshot> written = ReadSnapshot(_temporaryPath);
                if (!written.IsSuccess)
                {
                    return OperationResult.Failure(written.ErrorCode);
                }

                if (File.Exists(_filePath))
                {
                    File.Replace(_temporaryPath, _filePath, null);
                }
                else
                {
                    File.Move(_temporaryPath, _filePath);
                }

                return OperationResult.Success();
            }
            catch (IOException)
            {
                return OperationResult.Failure("save.write_failed");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.Failure("save.write_failed");
            }
            catch (SerializationException)
            {
                return OperationResult.Failure("save.write_failed");
            }
            finally
            {
                TryDeleteTemporaryFile();
            }
        }

        public OperationResult<GameSaveSnapshot> Load(string slotId)
        {
            if (!IsMainSlot(slotId))
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.slot_invalid");
            }

            if (!File.Exists(_filePath))
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.slot_missing");
            }

            return ReadSnapshot(_filePath);
        }

        private static bool IsMainSlot(string slotId)
        {
            return string.Equals(slotId, MainSlotId, StringComparison.Ordinal);
        }

        private static void WriteSnapshot(string path, GameSaveSnapshot snapshot)
        {
            V2SaveFileData data = V2SaveFileData.FromSnapshot(snapshot);
            var serializer = new DataContractJsonSerializer(typeof(V2SaveFileData));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.WriteObject(stream, data);
                stream.Flush(true);
            }
        }

        private OperationResult<GameSaveSnapshot> ReadSnapshot(string path)
        {
            SchemaProbe probe;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(SchemaProbe));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    probe = serializer.ReadObject(stream) as SchemaProbe;
                }
            }
            catch (Exception exception) when (IsJsonReadFailure(exception))
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.json_invalid");
            }
            catch (IOException)
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.read_failed");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.read_failed");
            }

            if (probe == null || !probe.SchemaVersion.HasValue)
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.payload_invalid");
            }

            OperationResult<GameSaveSnapshot> converted;
            if (probe.SchemaVersion.Value == 1)
            {
                OperationResult<V1SaveFileData> data = ReadData<V1SaveFileData>(path);
                if (!data.IsSuccess)
                {
                    return OperationResult<GameSaveSnapshot>.Failure(data.ErrorCode);
                }

                converted = data.Value.ToSnapshot(_legacyRestockPolicy);
            }
            else if (probe.SchemaVersion.Value == GameSaveSnapshot.CurrentSchemaVersion)
            {
                OperationResult<V2SaveFileData> data = ReadData<V2SaveFileData>(path);
                if (!data.IsSuccess)
                {
                    return OperationResult<GameSaveSnapshot>.Failure(data.ErrorCode);
                }

                converted = data.Value.ToSnapshot();
            }
            else
            {
                return OperationResult<GameSaveSnapshot>.Failure("save.schema_unsupported");
            }

            if (!converted.IsSuccess)
            {
                return converted;
            }

            OperationResult validation = GameSaveSnapshotValidator.Validate(converted.Value);
            return validation.IsSuccess
                ? converted
                : OperationResult<GameSaveSnapshot>.Failure(validation.ErrorCode);
        }

        private static OperationResult<T> ReadData<T>(string path)
            where T : class
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    T data = serializer.ReadObject(stream) as T;
                    return data == null
                        ? OperationResult<T>.Failure("save.payload_invalid")
                        : OperationResult<T>.Success(data);
                }
            }
            catch (Exception exception) when (IsJsonReadFailure(exception))
            {
                return OperationResult<T>.Failure("save.json_invalid");
            }
            catch (IOException)
            {
                return OperationResult<T>.Failure("save.read_failed");
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult<T>.Failure("save.read_failed");
            }
        }

        private static IShopStockReplacementPolicy CreateDefaultLegacyRestockPolicy()
        {
            var configuration = DefaultMvpContent.CreateConfiguration();
            return new DeterministicShopStockReplacementPolicy(
                configuration.ShopRestockRules,
                minimumDistinctItems: 4);
        }

        private void TryDeleteTemporaryFile()
        {
            try
            {
                if (File.Exists(_temporaryPath))
                {
                    File.Delete(_temporaryPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool IsJsonReadFailure(Exception exception)
        {
            if (exception is SerializationException
                || exception is XmlException
                || exception is FormatException
                || exception is OverflowException)
            {
                return true;
            }

            return exception is TargetInvocationException invocation
                && invocation.InnerException != null
                && IsJsonReadFailure(invocation.InnerException);
        }

        [DataContract]
        private sealed class SchemaProbe
        {
            [DataMember(Name = "schemaVersion", Order = 0, EmitDefaultValue = false)]
            public int? SchemaVersion { get; set; }
        }

        [DataContract]
        private sealed class V1SaveFileData
        {
            [DataMember(Name = "schemaVersion", Order = 0, EmitDefaultValue = false)]
            public int? SchemaVersion { get; set; }

            [DataMember(Name = "clock", Order = 1, EmitDefaultValue = false)]
            public ClockData Clock { get; set; }

            [DataMember(Name = "inventory", Order = 2, EmitDefaultValue = false)]
            public InventoryData Inventory { get; set; }

            [DataMember(Name = "wallet", Order = 3, EmitDefaultValue = false)]
            public WalletData Wallet { get; set; }

            [DataMember(Name = "farm", Order = 4, EmitDefaultValue = false)]
            public FarmData Farm { get; set; }

            [DataMember(Name = "livestock", Order = 5, EmitDefaultValue = false)]
            public LivestockData Livestock { get; set; }

            public OperationResult<GameSaveSnapshot> ToSnapshot(
                IShopStockReplacementPolicy restockPolicy)
            {
                if (!SchemaVersion.HasValue
                    || Clock == null
                    || Inventory == null
                    || Wallet == null
                    || Farm == null
                    || Livestock == null)
                {
                    return OperationResult<GameSaveSnapshot>.Failure("save.payload_invalid");
                }

                OperationResult<GameClockSnapshot> clock = Clock.ToSnapshot();
                OperationResult<InventorySnapshot> inventory = Inventory.ToSnapshot();
                OperationResult<WalletSnapshot> wallet = Wallet.ToSnapshot();
                OperationResult<FarmSnapshot> farm = Farm.ToSnapshot();
                OperationResult<LivestockSnapshot> livestock = Livestock.ToSnapshot();
                if (!clock.IsSuccess
                    || !inventory.IsSuccess
                    || !wallet.IsSuccess
                    || !farm.IsSuccess
                    || !livestock.IsSuccess)
                {
                    return OperationResult<GameSaveSnapshot>.Failure("save.payload_invalid");
                }

                var player = new CharacterEconomySnapshot(
                    DefaultMvpIds.Characters.Player,
                    inventory.Value,
                    wallet.Value);
                var sourceShop = new ShopEconomySnapshot(
                    DefaultMvpIds.Shops.TownGeneral,
                    new InventorySnapshot(Array.Empty<ItemStack>()),
                    new WalletSnapshot(DefaultMvpContent.DefaultShopStartingBalance),
                    clock.Value.Day - 1,
                    DeterministicShopStockReplacementPolicy.VersionOne);
                OperationResult<ShopEconomySnapshot> shop =
                    restockPolicy.CreateCandidate(
                        LegacyV1WorldSeed,
                        sourceShop,
                        clock.Value.Day);
                if (!shop.IsSuccess)
                {
                    return OperationResult<GameSaveSnapshot>.Failure(
                        "save.migration_failed");
                }

                return OperationResult<GameSaveSnapshot>.Success(
                    new GameSaveSnapshot(
                        GameSaveSnapshot.CurrentSchemaVersion,
                        LegacyV1WorldSeed,
                        clock.Value,
                        new[] { player },
                        new[] { shop.Value },
                        farm.Value,
                        livestock.Value));
            }
        }

        [DataContract]
        private sealed class V2SaveFileData
        {
            [DataMember(Name = "schemaVersion", Order = 0, EmitDefaultValue = false)]
            public int? SchemaVersion { get; set; }

            [DataMember(Name = "worldSeed", Order = 1, EmitDefaultValue = false)]
            public int? WorldSeed { get; set; }

            [DataMember(Name = "clock", Order = 2, EmitDefaultValue = false)]
            public ClockData Clock { get; set; }

            [DataMember(Name = "characters", Order = 3, EmitDefaultValue = false)]
            public CharacterData[] Characters { get; set; }

            [DataMember(Name = "shops", Order = 4, EmitDefaultValue = false)]
            public ShopData[] Shops { get; set; }

            [DataMember(Name = "farm", Order = 5, EmitDefaultValue = false)]
            public FarmData Farm { get; set; }

            [DataMember(Name = "livestock", Order = 6, EmitDefaultValue = false)]
            public LivestockData Livestock { get; set; }

            public static V2SaveFileData FromSnapshot(GameSaveSnapshot snapshot)
            {
                CharacterEconomySnapshot[] characters = snapshot.Characters;
                ShopEconomySnapshot[] shops = snapshot.Shops;
                var characterData = new CharacterData[characters.Length];
                var shopData = new ShopData[shops.Length];
                for (int index = 0; index < characterData.Length; index++)
                {
                    characterData[index] = CharacterData.FromSnapshot(characters[index]);
                }

                for (int index = 0; index < shopData.Length; index++)
                {
                    shopData[index] = ShopData.FromSnapshot(shops[index]);
                }

                return new V2SaveFileData
                {
                    SchemaVersion = snapshot.SchemaVersion,
                    WorldSeed = snapshot.WorldSeed,
                    Clock = ClockData.FromSnapshot(snapshot.Clock),
                    Characters = characterData,
                    Shops = shopData,
                    Farm = FarmData.FromSnapshot(snapshot.Farm),
                    Livestock = LivestockData.FromSnapshot(snapshot.Livestock)
                };
            }

            public OperationResult<GameSaveSnapshot> ToSnapshot()
            {
                if (!SchemaVersion.HasValue
                    || !WorldSeed.HasValue
                    || Clock == null
                    || Characters == null
                    || Shops == null
                    || Farm == null
                    || Livestock == null)
                {
                    return OperationResult<GameSaveSnapshot>.Failure(
                        "save.payload_invalid");
                }

                OperationResult<GameClockSnapshot> clock = Clock.ToSnapshot();
                OperationResult<FarmSnapshot> farm = Farm.ToSnapshot();
                OperationResult<LivestockSnapshot> livestock = Livestock.ToSnapshot();
                if (!clock.IsSuccess || !farm.IsSuccess || !livestock.IsSuccess)
                {
                    return OperationResult<GameSaveSnapshot>.Failure(
                        "save.payload_invalid");
                }

                var characters = new CharacterEconomySnapshot[Characters.Length];
                for (int index = 0; index < characters.Length; index++)
                {
                    if (Characters[index] == null
                        || !Characters[index].TryToSnapshot(out characters[index]))
                    {
                        return OperationResult<GameSaveSnapshot>.Failure(
                            "save.payload_invalid");
                    }
                }

                var shops = new ShopEconomySnapshot[Shops.Length];
                for (int index = 0; index < shops.Length; index++)
                {
                    if (Shops[index] == null
                        || !Shops[index].TryToSnapshot(out shops[index]))
                    {
                        return OperationResult<GameSaveSnapshot>.Failure(
                            "save.payload_invalid");
                    }
                }

                return OperationResult<GameSaveSnapshot>.Success(
                    new GameSaveSnapshot(
                        SchemaVersion.Value,
                        WorldSeed.Value,
                        clock.Value,
                        characters,
                        shops,
                        farm.Value,
                        livestock.Value));
            }
        }

        [DataContract]
        private sealed class CharacterData
        {
            [DataMember(Name = "characterId", Order = 0, EmitDefaultValue = false)]
            public string CharacterId { get; set; }

            [DataMember(Name = "backpack", Order = 1, EmitDefaultValue = false)]
            public InventoryData Backpack { get; set; }

            [DataMember(Name = "wallet", Order = 2, EmitDefaultValue = false)]
            public WalletData Wallet { get; set; }

            public static CharacterData FromSnapshot(CharacterEconomySnapshot snapshot)
            {
                return new CharacterData
                {
                    CharacterId = snapshot.CharacterId,
                    Backpack = InventoryData.FromSnapshot(snapshot.Backpack),
                    Wallet = WalletData.FromSnapshot(snapshot.Wallet)
                };
            }

            public bool TryToSnapshot(out CharacterEconomySnapshot snapshot)
            {
                snapshot = null;
                if (CharacterId == null || Backpack == null || Wallet == null)
                {
                    return false;
                }

                OperationResult<InventorySnapshot> backpack = Backpack.ToSnapshot();
                OperationResult<WalletSnapshot> wallet = Wallet.ToSnapshot();
                if (!backpack.IsSuccess || !wallet.IsSuccess)
                {
                    return false;
                }

                snapshot = new CharacterEconomySnapshot(
                    CharacterId,
                    backpack.Value,
                    wallet.Value);
                return true;
            }
        }

        [DataContract]
        private sealed class ShopData
        {
            [DataMember(Name = "shopId", Order = 0, EmitDefaultValue = false)]
            public string ShopId { get; set; }

            [DataMember(Name = "stock", Order = 1, EmitDefaultValue = false)]
            public InventoryData Stock { get; set; }

            [DataMember(Name = "wallet", Order = 2, EmitDefaultValue = false)]
            public WalletData Wallet { get; set; }

            [DataMember(Name = "lastRestockedDay", Order = 3, EmitDefaultValue = false)]
            public int? LastRestockedDay { get; set; }

            [DataMember(Name = "restockAlgorithmVersion", Order = 4, EmitDefaultValue = false)]
            public int? RestockAlgorithmVersion { get; set; }

            public static ShopData FromSnapshot(ShopEconomySnapshot snapshot)
            {
                return new ShopData
                {
                    ShopId = snapshot.ShopId,
                    Stock = InventoryData.FromSnapshot(snapshot.Stock),
                    Wallet = WalletData.FromSnapshot(snapshot.Wallet),
                    LastRestockedDay = snapshot.LastRestockedDay,
                    RestockAlgorithmVersion = snapshot.RestockAlgorithmVersion
                };
            }

            public bool TryToSnapshot(out ShopEconomySnapshot snapshot)
            {
                snapshot = null;
                if (ShopId == null
                    || Stock == null
                    || Wallet == null
                    || !LastRestockedDay.HasValue
                    || !RestockAlgorithmVersion.HasValue)
                {
                    return false;
                }

                OperationResult<InventorySnapshot> stock = Stock.ToSnapshot();
                OperationResult<WalletSnapshot> wallet = Wallet.ToSnapshot();
                if (!stock.IsSuccess || !wallet.IsSuccess)
                {
                    return false;
                }

                snapshot = new ShopEconomySnapshot(
                    ShopId,
                    stock.Value,
                    wallet.Value,
                    LastRestockedDay.Value,
                    RestockAlgorithmVersion.Value);
                return true;
            }
        }

        [DataContract]
        private sealed class ClockData
        {
            [DataMember(Name = "day", Order = 0, EmitDefaultValue = false)]
            public int? Day { get; set; }

            [DataMember(Name = "minuteOfDay", Order = 1, EmitDefaultValue = false)]
            public int? MinuteOfDay { get; set; }

            public static ClockData FromSnapshot(GameClockSnapshot snapshot)
            {
                return new ClockData { Day = snapshot.Day, MinuteOfDay = snapshot.MinuteOfDay };
            }

            public OperationResult<GameClockSnapshot> ToSnapshot()
            {
                return Day.HasValue && MinuteOfDay.HasValue
                    ? OperationResult<GameClockSnapshot>.Success(
                        new GameClockSnapshot(Day.Value, MinuteOfDay.Value))
                    : OperationResult<GameClockSnapshot>.Failure("save.payload_invalid");
            }
        }

        [DataContract]
        private sealed class WalletData
        {
            [DataMember(Name = "balance", Order = 0, EmitDefaultValue = false)]
            public int? Balance { get; set; }

            public static WalletData FromSnapshot(WalletSnapshot snapshot)
            {
                return new WalletData { Balance = snapshot.Balance };
            }

            public OperationResult<WalletSnapshot> ToSnapshot()
            {
                return Balance.HasValue
                    ? OperationResult<WalletSnapshot>.Success(new WalletSnapshot(Balance.Value))
                    : OperationResult<WalletSnapshot>.Failure("save.payload_invalid");
            }
        }

        [DataContract]
        private sealed class InventoryData
        {
            [DataMember(Name = "items", Order = 0, EmitDefaultValue = false)]
            public ItemData[] Items { get; set; }

            public static InventoryData FromSnapshot(InventorySnapshot snapshot)
            {
                var items = new ItemData[snapshot.Items.Length];
                for (int index = 0; index < items.Length; index++)
                {
                    items[index] = ItemData.FromSnapshot(snapshot.Items[index]);
                }

                return new InventoryData { Items = items };
            }

            public OperationResult<InventorySnapshot> ToSnapshot()
            {
                if (Items == null)
                {
                    return OperationResult<InventorySnapshot>.Failure("save.payload_invalid");
                }

                var items = new ItemStack[Items.Length];
                for (int index = 0; index < items.Length; index++)
                {
                    if (Items[index] == null || !Items[index].TryToSnapshot(out items[index]))
                    {
                        return OperationResult<InventorySnapshot>.Failure("save.payload_invalid");
                    }
                }

                return OperationResult<InventorySnapshot>.Success(new InventorySnapshot(items));
            }
        }

        [DataContract]
        private sealed class ItemData
        {
            [DataMember(Name = "itemId", Order = 0, EmitDefaultValue = false)]
            public string ItemId { get; set; }

            [DataMember(Name = "quantity", Order = 1, EmitDefaultValue = false)]
            public int? Quantity { get; set; }

            public static ItemData FromSnapshot(ItemStack snapshot)
            {
                return new ItemData { ItemId = snapshot.ItemId, Quantity = snapshot.Quantity };
            }

            public bool TryToSnapshot(out ItemStack snapshot)
            {
                snapshot = default;
                if (ItemId == null || !Quantity.HasValue)
                {
                    return false;
                }

                snapshot = new ItemStack(ItemId, Quantity.Value);
                return true;
            }
        }

        [DataContract]
        private sealed class FarmData
        {
            [DataMember(Name = "lastProcessedDay", Order = 0, EmitDefaultValue = false)]
            public int? LastProcessedDay { get; set; }

            [DataMember(Name = "plots", Order = 1, EmitDefaultValue = false)]
            public PlotData[] Plots { get; set; }

            public static FarmData FromSnapshot(FarmSnapshot snapshot)
            {
                var plots = new PlotData[snapshot.Plots.Length];
                for (int index = 0; index < plots.Length; index++)
                {
                    plots[index] = PlotData.FromSnapshot(snapshot.Plots[index]);
                }

                return new FarmData
                {
                    LastProcessedDay = snapshot.LastProcessedDay,
                    Plots = plots
                };
            }

            public OperationResult<FarmSnapshot> ToSnapshot()
            {
                if (!LastProcessedDay.HasValue || Plots == null)
                {
                    return OperationResult<FarmSnapshot>.Failure("save.payload_invalid");
                }

                var plots = new FarmPlotSnapshot[Plots.Length];
                for (int index = 0; index < plots.Length; index++)
                {
                    if (Plots[index] == null || !Plots[index].TryToSnapshot(out plots[index]))
                    {
                        return OperationResult<FarmSnapshot>.Failure("save.payload_invalid");
                    }
                }

                return OperationResult<FarmSnapshot>.Success(
                    new FarmSnapshot(LastProcessedDay.Value, plots));
            }
        }

        [DataContract]
        private sealed class PlotData
        {
            [DataMember(Name = "plotId", Order = 0, EmitDefaultValue = false)]
            public string PlotId { get; set; }

            [DataMember(Name = "cropId", Order = 1, EmitDefaultValue = false)]
            public string CropId { get; set; }

            [DataMember(Name = "growthProgressDays", Order = 2, EmitDefaultValue = false)]
            public int? GrowthProgressDays { get; set; }

            [DataMember(Name = "wateredToday", Order = 3, EmitDefaultValue = false)]
            public bool? WateredToday { get; set; }

            [DataMember(Name = "status", Order = 4, EmitDefaultValue = false)]
            public int? Status { get; set; }

            public static PlotData FromSnapshot(FarmPlotSnapshot snapshot)
            {
                return new PlotData
                {
                    PlotId = snapshot.PlotId,
                    CropId = snapshot.CropId,
                    GrowthProgressDays = snapshot.GrowthProgressDays,
                    WateredToday = snapshot.WateredToday,
                    Status = (int)snapshot.Status
                };
            }

            public bool TryToSnapshot(out FarmPlotSnapshot snapshot)
            {
                snapshot = default;
                if (PlotId == null
                    || CropId == null
                    || !GrowthProgressDays.HasValue
                    || !WateredToday.HasValue
                    || !Status.HasValue)
                {
                    return false;
                }

                snapshot = new FarmPlotSnapshot(
                    PlotId,
                    CropId,
                    GrowthProgressDays.Value,
                    WateredToday.Value,
                    (FarmPlotStatus)Status.Value);
                return true;
            }
        }

        [DataContract]
        private sealed class LivestockData
        {
            [DataMember(Name = "lastProcessedDay", Order = 0, EmitDefaultValue = false)]
            public int? LastProcessedDay { get; set; }

            [DataMember(Name = "animals", Order = 1, EmitDefaultValue = false)]
            public AnimalData[] Animals { get; set; }

            public static LivestockData FromSnapshot(LivestockSnapshot snapshot)
            {
                var animals = new AnimalData[snapshot.Animals.Length];
                for (int index = 0; index < animals.Length; index++)
                {
                    animals[index] = AnimalData.FromSnapshot(snapshot.Animals[index]);
                }

                return new LivestockData
                {
                    LastProcessedDay = snapshot.LastProcessedDay,
                    Animals = animals
                };
            }

            public OperationResult<LivestockSnapshot> ToSnapshot()
            {
                if (!LastProcessedDay.HasValue || Animals == null)
                {
                    return OperationResult<LivestockSnapshot>.Failure("save.payload_invalid");
                }

                var animals = new AnimalSnapshot[Animals.Length];
                for (int index = 0; index < animals.Length; index++)
                {
                    if (Animals[index] == null || !Animals[index].TryToSnapshot(out animals[index]))
                    {
                        return OperationResult<LivestockSnapshot>.Failure("save.payload_invalid");
                    }
                }

                return OperationResult<LivestockSnapshot>.Success(
                    new LivestockSnapshot(LastProcessedDay.Value, animals));
            }
        }

        [DataContract]
        private sealed class AnimalData
        {
            [DataMember(Name = "animalId", Order = 0, EmitDefaultValue = false)]
            public string AnimalId { get; set; }

            [DataMember(Name = "speciesId", Order = 1, EmitDefaultValue = false)]
            public string SpeciesId { get; set; }

            [DataMember(Name = "fedToday", Order = 2, EmitDefaultValue = false)]
            public bool? FedToday { get; set; }

            [DataMember(Name = "productReady", Order = 3, EmitDefaultValue = false)]
            public bool? ProductReady { get; set; }

            public static AnimalData FromSnapshot(AnimalSnapshot snapshot)
            {
                return new AnimalData
                {
                    AnimalId = snapshot.AnimalId,
                    SpeciesId = snapshot.SpeciesId,
                    FedToday = snapshot.FedToday,
                    ProductReady = snapshot.ProductReady
                };
            }

            public bool TryToSnapshot(out AnimalSnapshot snapshot)
            {
                snapshot = default;
                if (AnimalId == null
                    || SpeciesId == null
                    || !FedToday.HasValue
                    || !ProductReady.HasValue)
                {
                    return false;
                }

                snapshot = new AnimalSnapshot(
                    AnimalId,
                    SpeciesId,
                    FedToday.Value,
                    ProductReady.Value);
                return true;
            }
        }
    }
}
