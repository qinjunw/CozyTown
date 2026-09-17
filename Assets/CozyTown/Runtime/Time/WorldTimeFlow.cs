using System;
using System.Collections.Generic;
using CozyTown.Runtime.Core;

namespace CozyTown.Runtime.Time
{
    public sealed class WorldTimeFlow : IWorldTimeFlow
    {
        private bool _coordinatedAdvance;
        public WorldTimeFlow(GameClockSnapshot initialClock)
        {
            Current = new WorldTimeProgress(initialClock, 0, true, 1);
        }

        public WorldTimeProgress Current { get; private set; }
        public WorldTimeFlowState State { get; private set; }
        public IReadOnlyList<string> NotificationFailures { get; private set; } = Array.Empty<string>();
        public event Action<WorldTimeProgress> Changed;
        public event Action<WorldTimeProgress> PresentationChanged;

        internal WorldTimeFlowState BeginRestore()
        {
            var previous = State;
            State = WorldTimeFlowState.Publishing;
            return previous;
        }

        internal void RestoreRejected(WorldTimeFlowState previous) => State = previous;

        internal void RequireRecovery(string failure)
        {
            State = WorldTimeFlowState.RecoveryRequired;
            NotificationFailures = Array.AsReadOnly(new[] { failure });
        }

        internal void BeginCoordinatedAdvance() => _coordinatedAdvance = true;

        internal void CancelCoordinatedAdvance() => _coordinatedAdvance = false;

        internal OperationResult CompleteExplicitAdvance(GameClockSnapshot clock, int minutes)
        {
            _coordinatedAdvance = false;
            double start = ((long)clock.Day - 1) * 1440 + clock.MinuteOfDay - minutes;
            return Publish(clock, advanceFromTotalMinutes: start);
        }

        internal OperationResult CompleteElapsedAdvance(GameClockSnapshot clock, double fractionalMinute)
        {
            _coordinatedAdvance = false;
            return Publish(clock, fractionalMinute);
        }

        internal OperationResult Publish(GameClockSnapshot clock, double fractionalMinute = 0, bool isRebuild = false,
            double? advanceFromTotalMinutes = null)
        {
            if (_coordinatedAdvance) return OperationResult.Success();
            var next = new WorldTimeProgress(clock, fractionalMinute, isRebuild,
                Current.RebuildVersion + (isRebuild ? 1 : 0),
                advanceFromTotalMinutes ?? Current.TotalMinutes);
            if (!isRebuild && next.TotalMinutes == Current.TotalMinutes) return OperationResult.Success();
            Current = next;
            State = WorldTimeFlowState.Publishing;
            var failures = new List<string>();
            Notify(Changed, next, "synchronization", failures);
            if (failures.Count > 0)
            {
                NotificationFailures = failures.AsReadOnly();
                State = WorldTimeFlowState.RecoveryRequired;
                return OperationResult.Failure("world_time.synchronization_failed");
            }
            Notify(PresentationChanged, next, "presentation", failures);
            NotificationFailures = failures.AsReadOnly();
            State = WorldTimeFlowState.Ready;
            return failures.Count == 0 ? OperationResult.Success()
                : OperationResult.Failure("world_time.presentation_failed");
        }

        private static void Notify(Action<WorldTimeProgress> handlers, WorldTimeProgress progress,
            string phase, List<string> failures)
        {
            if (handlers == null) return;
            foreach (Action<WorldTimeProgress> handler in handlers.GetInvocationList())
            {
                try { handler(progress); }
                catch (Exception error)
                {
                    failures.Add($"{phase}:{handler.Method.DeclaringType?.FullName}.{handler.Method.Name}:{error.GetType().Name}");
                }
            }
        }
    }
}
