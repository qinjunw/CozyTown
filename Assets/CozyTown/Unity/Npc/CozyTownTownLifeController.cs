using System;
using CozyTown.Runtime.Time;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    [DisallowMultipleComponent]
    public sealed class CozyTownTownLifeController : MonoBehaviour
    {
        [SerializeField] private NpcWorldResident2D[] residents = Array.Empty<NpcWorldResident2D>();
        private IWorldTimeFlow _timeFlow;
        private WorldTimeProgress _last;
        private bool _hasState;

        public void Configure(params NpcWorldResident2D[] actors)
        {
            residents = (NpcWorldResident2D[])actors.Clone();
        }

        public void Bind(IWorldTimeFlow timeFlow)
        {
            if (timeFlow == null) throw new ArgumentNullException(nameof(timeFlow));
            foreach (var resident in residents) resident.ValidateConfiguration();
            if (_hasState && ReferenceEquals(_timeFlow, timeFlow)) return;
            if (_timeFlow != null) _timeFlow.Changed -= Apply;
            _timeFlow = timeFlow;
            _hasState = false;
            Apply(_timeFlow.Current);
            _timeFlow.Changed += Apply;
        }

        // The world clock owns pause; this subscription follows the bound session,
        // including explicit sleep/load while the presentation is disabled.
        private void OnDestroy()
        {
            if (_timeFlow != null) _timeFlow.Changed -= Apply;
        }

        private void Apply(WorldTimeProgress progress)
        {
            var candidates = new NpcWorldResident2D.Journey[residents.Length];
            bool rebuild = !_hasState || progress.RebuildVersion != _last.RebuildVersion;
            for (int i = 0; i < residents.Length; i++)
            {
                candidates[i] = rebuild
                    ? residents[i].Reconstruct(progress.Clock.MinuteOfDay)
                    : residents[i].Advance(progress.AdvanceFromTotalMinutes, progress.TotalMinutes);
            }
            for (int i = 0; i < residents.Length; i++)
            {
                if (rebuild) residents[i].CancelInteraction();
                residents[i].Commit(candidates[i]);
            }
            _last = progress;
            _hasState = true;
            Physics2D.SyncTransforms();
        }
    }
}
