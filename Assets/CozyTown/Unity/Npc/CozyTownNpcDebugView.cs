using System;
using System.Collections.Generic;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    public sealed class CozyTownNpcDebugView : CozyTownModalDebugViewBase
    {
        private IReadOnlyList<NpcDialogueOption> _npcs = Array.Empty<NpcDialogueOption>();

        public event Action TalkRequested;

        public event Action<string> NpcRequested;

        public NpcDialogueViewState State { get; private set; }

        public string CurrentNpcId { get; private set; } = string.Empty;

        public bool IsLoading { get; private set; }

        public int NpcCount => _npcs.Count;

        public void ShowLoading(
            IReadOnlyList<NpcDialogueOption> npcs,
            string npcId)
        {
            _npcs = npcs ?? Array.Empty<NpcDialogueOption>();
            CurrentNpcId = npcId ?? string.Empty;
            State = null;
            IsLoading = true;
            ShowBase("Generating dialogue...");
        }

        public void Show(
            NpcDialogueViewState state,
            IReadOnlyList<NpcDialogueOption> npcs = null)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            if (npcs != null)
            {
                _npcs = npcs;
            }

            CurrentNpcId = state.NpcId ?? string.Empty;
            IsLoading = false;
            string feedback = state.IsFallback
                ? $"Fallback: {state.FallbackReason}"
                : string.Empty;
            ShowBase(feedback);
        }

        public void ShowFailure(
            IReadOnlyList<NpcDialogueOption> npcs,
            string npcId)
        {
            _npcs = npcs ?? Array.Empty<NpcDialogueOption>();
            CurrentNpcId = npcId ?? string.Empty;
            State = null;
            IsLoading = false;
            ShowBase("Dialogue unavailable.");
        }

        public void RequestTalk()
        {
            if (IsVisible && !IsLoading)
            {
                TalkRequested?.Invoke();
            }
        }

        public void RequestNpc(string npcId)
        {
            if (IsVisible && !IsLoading && !string.IsNullOrWhiteSpace(npcId))
            {
                NpcRequested?.Invoke(npcId);
            }
        }

        private void OnGUI()
        {
            if (!BeginPanel("Town NPC Dialogue"))
            {
                return;
            }

            foreach (NpcDialogueOption npc in _npcs)
            {
                if (GUILayout.Button(
                    $"Talk to {npc.DisplayName}",
                    ButtonStyle,
                    GUILayout.Height(40f)))
                {
                    RequestNpc(npc.NpcId);
                }
            }

            if (State != null)
            {
                GUILayout.Space(12f);
                GUILayout.Label($"{State.DisplayName}: {State.Text}", LabelStyle);
                GUILayout.Label(
                    $"Emotion: {State.EmotionTag}   Action: {State.ActionTag}",
                    LabelStyle);
                if (GUILayout.Button("Talk again", ButtonStyle, GUILayout.Height(40f)))
                {
                    RequestTalk();
                }
            }

            EndPanel();
        }
    }
}
