using System;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Farming;
using CozyTown.Runtime.Livestock;
using CozyTown.Runtime.Time;

namespace CozyTown.Runtime.Application
{
    public sealed class WorldTimeCoordinator : IWorldTimeCoordinator
    {
        internal const int MaximumAdvanceMinutes = 7 * InMemoryTimeService.MinutesPerDay;

        private readonly ITimeService _time;
        private readonly IFarmService _farm;
        private readonly ILivestockService _livestock;
        private readonly IEconomyStateStore _economyState;
        private readonly IWorldSeedState _worldSeed;
        private readonly IShopStockReplacementPolicy _shopRestock;
        private readonly WorldTimeFlow _timeFlow;

        public WorldTimeCoordinator(
            ITimeService time,
            IFarmService farm,
            ILivestockService livestock,
            IEconomyStateStore economyState,
            IWorldSeedState worldSeed,
            IShopStockReplacementPolicy shopRestock = null,
            WorldTimeFlow timeFlow = null)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _farm = farm ?? throw new ArgumentNullException(nameof(farm));
            _livestock = livestock ?? throw new ArgumentNullException(nameof(livestock));
            _economyState = economyState
                ?? throw new ArgumentNullException(nameof(economyState));
            _worldSeed = worldSeed ?? throw new ArgumentNullException(nameof(worldSeed));
            _shopRestock = shopRestock;
            _timeFlow = timeFlow;
        }

        public GameClockSnapshot Current => _time.Current;

        public OperationResult<GameClockSnapshot> AdvanceMinutes(int gameMinutes)
        {
            if (gameMinutes > MaximumAdvanceMinutes)
            {
                return OperationResult<GameClockSnapshot>.Failure("world_time.request_too_large");
            }

            if (!(_time is InMemoryTimeService time)
                || !(_farm is InMemoryFarmService farm)
                || !(_livestock is InMemoryLivestockService livestock)
                || !(_economyState is InMemoryEconomyStateStore economy))
            {
                return OperationResult<GameClockSnapshot>.Failure(
                    "world_time.preparation_unsupported");
            }

            OperationResult<GameClockSnapshot> clockCandidate = time.CreateAdvanceCandidate(gameMinutes);
            if (!clockCandidate.IsSuccess)
            {
                return clockCandidate;
            }

            FarmSnapshot farmCandidate = farm.CaptureSnapshot();
            LivestockSnapshot livestockCandidate = livestock.CaptureSnapshot();
            EconomyStateSnapshot economyBefore = economy.CaptureSnapshot();
            ShopEconomySnapshot[] shops = economyBefore.Shops;
            int completedDay = farmCandidate.LastProcessedDay;
            if (!DailySettlementSchedule.IsValidProgress(Current, completedDay)
                || livestockCandidate.LastProcessedDay != completedDay
                || Array.Exists(shops, shop => shop.LastRestockedDay != completedDay))
            {
                return OperationResult<GameClockSnapshot>.Failure("world_time.state_misaligned");
            }

            GameClockSnapshot targetClock = clockCandidate.Value;
            int lastDueDay = targetClock.MinuteOfDay >= DailySettlementSchedule.SettlementMinuteOfDay
                ? targetClock.Day
                : targetClock.Day - 1;
            Action commitEconomy = null;
            for (long day = (long)completedDay + 1; day <= lastDueDay; day++)
            {
                OperationResult<FarmSnapshot> nextFarm = farm.CreateDayCandidate(farmCandidate, (int)day);
                if (!nextFarm.IsSuccess)
                {
                    return OperationResult<GameClockSnapshot>.Failure(nextFarm.ErrorCode);
                }

                OperationResult<LivestockSnapshot> nextLivestock =
                    livestock.CreateDayCandidate(livestockCandidate, (int)day);
                if (!nextLivestock.IsSuccess)
                {
                    return OperationResult<GameClockSnapshot>.Failure(nextLivestock.ErrorCode);
                }

                OperationResult<ShopEconomySnapshot[]> nextShops = CreateShopCandidates(shops, (int)day);
                if (!nextShops.IsSuccess)
                {
                    return OperationResult<GameClockSnapshot>.Failure(nextShops.ErrorCode);
                }

                OperationResult<Action> preparedEconomy = economy.PrepareRestore(
                    new EconomyStateSnapshot(economyBefore.Characters, nextShops.Value));
                if (!preparedEconomy.IsSuccess)
                {
                    return OperationResult<GameClockSnapshot>.Failure(preparedEconomy.ErrorCode);
                }

                farmCandidate = nextFarm.Value;
                livestockCandidate = nextLivestock.Value;
                shops = nextShops.Value;
                commitEconomy = preparedEconomy.Value;
            }

            if (farmCandidate.LastProcessedDay != completedDay)
            {
                OperationResult<Action> preparedFarm = farm.PrepareRestore(farmCandidate);
                if (!preparedFarm.IsSuccess)
                {
                    return OperationResult<GameClockSnapshot>.Failure(preparedFarm.ErrorCode);
                }

                OperationResult<Action> preparedLivestock = livestock.PrepareRestore(livestockCandidate);
                if (!preparedLivestock.IsSuccess)
                {
                    return OperationResult<GameClockSnapshot>.Failure(preparedLivestock.ErrorCode);
                }

                // Prepared commits only assign owned state; they do not validate or call external code.
                preparedFarm.Value();
                preparedLivestock.Value();
                commitEconomy();
            }

            double fraction = _timeFlow?.Current.FractionalMinute ?? 0;
            double startMinute = ((long)Current.Day - 1) * InMemoryTimeService.MinutesPerDay + Current.MinuteOfDay + fraction;
            time.CommitPrepared(targetClock);
            if (gameMinutes > 0) _timeFlow?.Publish(targetClock, fraction, advanceFromTotalMinutes: startMinute);
            return OperationResult<GameClockSnapshot>.Success(targetClock);
        }

        private OperationResult<ShopEconomySnapshot[]> CreateShopCandidates(
            ShopEconomySnapshot[] current,
            int targetDay)
        {
            if (current.Length > 0 && _shopRestock == null)
            {
                return OperationResult<ShopEconomySnapshot[]>.Failure(
                    "world_time.shop_restock_missing");
            }

            var candidates = new ShopEconomySnapshot[current.Length];
            for (int index = 0; index < current.Length; index++)
            {
                ShopEconomySnapshot shop = current[index];
                OperationResult<ShopEconomySnapshot> candidate =
                    _shopRestock.CreateCandidate(_worldSeed.Value, shop, targetDay);
                if (!candidate.IsSuccess
                    || candidate.Value == null
                    || !string.Equals(candidate.Value.ShopId, shop.ShopId, StringComparison.Ordinal)
                    || candidate.Value.LastRestockedDay != targetDay
                    || candidate.Value.RestockAlgorithmVersion != shop.RestockAlgorithmVersion
                    || candidate.Value.Wallet.Balance != shop.Wallet.Balance)
                {
                    return OperationResult<ShopEconomySnapshot[]>.Failure(
                        "world_time.shop_restock_failed");
                }

                candidates[index] = candidate.Value;
            }

            return OperationResult<ShopEconomySnapshot[]>.Success(candidates);
        }
    }
}
