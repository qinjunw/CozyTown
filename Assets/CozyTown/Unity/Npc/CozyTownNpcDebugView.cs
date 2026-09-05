using System;
using System.Collections.Generic;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Npc
{
    public sealed class CozyTownNpcDebugView : CozyTownModalDebugViewBase
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text feedbackText;
        [SerializeField] private CozyTownUiListRow[] rows = Array.Empty<CozyTownUiListRow>();
        [SerializeField] private Image selectionMarker;
        [SerializeField] private Image portraitImage;
        [SerializeField] private Text dialogueText;
        [SerializeField] private Text metadataText;
        [SerializeField] private Button talkAgainButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private CozyTownUiIconCatalog iconCatalog;

        private IReadOnlyList<NpcDialogueOption> _npcs = Array.Empty<NpcDialogueOption>();

        public event Action TalkRequested;

        public event Action<string> NpcRequested;

        public NpcDialogueViewState State { get; private set; }

        public string CurrentNpcId { get; private set; } = string.Empty;

        public bool IsLoading { get; private set; }

        public int NpcCount => _npcs.Count;

        public void ConfigureUi(
            GameObject targetPanel,
            Text targetFeedbackText,
            CozyTownUiListRow[] targetRows,
            Image targetSelectionMarker,
            Image targetPortraitImage,
            Text targetDialogueText,
            Text targetMetadataText,
            Button targetTalkAgainButton,
            Button targetCloseButton,
            CozyTownUiIconCatalog targetIconCatalog)
        {
            ValidateUi(
                targetPanel,
                targetFeedbackText,
                targetRows,
                targetSelectionMarker,
                targetPortraitImage,
                targetDialogueText,
                targetMetadataText,
                targetTalkAgainButton,
                targetCloseButton,
                targetIconCatalog);

            ClearRows();
            RemoveFixedListeners();
            if (selectionMarker != null)
            {
                selectionMarker.enabled = false;
            }

            panel = targetPanel;
            feedbackText = targetFeedbackText;
            rows = (CozyTownUiListRow[])targetRows.Clone();
            selectionMarker = targetSelectionMarker;
            portraitImage = targetPortraitImage;
            dialogueText = targetDialogueText;
            metadataText = targetMetadataText;
            talkAgainButton = targetTalkAgainButton;
            closeButton = targetCloseButton;
            iconCatalog = targetIconCatalog;

            BindFixedListeners();
            RefreshUi();
        }

        public void ShowLoading(
            IReadOnlyList<NpcDialogueOption> npcs,
            string npcId)
        {
            _npcs = npcs ?? Array.Empty<NpcDialogueOption>();
            CurrentNpcId = npcId ?? string.Empty;
            State = null;
            IsLoading = true;
            ShowBase("Generating dialogue...");
            RefreshUi();
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
            RefreshUi();
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
            RefreshUi();
        }

        public new void Hide()
        {
            base.Hide();
            RefreshUi();
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

        private void OnEnable()
        {
            BindFixedListeners();
            RefreshUi();
        }

        private void OnDisable()
        {
            RequestClose();
            Hide();
            RemoveFixedListeners();
            ClearRows();
            if (panel != null)
            {
                panel.SetActive(false);
            }
        }

        private void RefreshUi()
        {
            ClearRows();

            if (panel != null)
            {
                panel.SetActive(IsVisible);
            }

            if (feedbackText != null)
            {
                feedbackText.text = Feedback;
            }

            if (selectionMarker != null)
            {
                selectionMarker.enabled = false;
            }

            Sprite portrait = iconCatalog?.GetNpcSprite(CurrentNpcId);
            if (portraitImage != null)
            {
                portraitImage.sprite = portrait;
                portraitImage.enabled = portrait != null;
            }

            if (dialogueText != null)
            {
                dialogueText.text = State == null
                    ? string.Empty
                    : $"{State.DisplayName}: {State.Text}";
            }

            if (metadataText != null)
            {
                metadataText.text = State == null
                    ? string.Empty
                    : $"Emotion: {State.EmotionTag}   Action: {State.ActionTag}";
            }

            if (talkAgainButton != null)
            {
                talkAgainButton.interactable = IsVisible && !IsLoading && State != null;
            }

            if (!IsVisible || rows == null || iconCatalog == null)
            {
                return;
            }

            int visibleRowCount = Math.Min(rows.Length, _npcs.Count);
            for (var index = 0; index < visibleRowCount; index++)
            {
                CozyTownUiListRow row = rows[index];
                NpcDialogueOption npc = _npcs[index];
                string npcId = npc.NpcId;
                row.SetContent(npc.DisplayName, iconCatalog.GetNpcSprite(npcId));
                row.SetButton(
                    0,
                    "Talk",
                    !IsLoading,
                    () => RequestNpc(npcId));

                if (selectionMarker != null
                    && string.Equals(npcId, CurrentNpcId, StringComparison.Ordinal))
                {
                    selectionMarker.transform.SetParent(row.transform, false);
                    selectionMarker.enabled = true;
                }
            }
        }

        private void BindFixedListeners()
        {
            if (talkAgainButton != null)
            {
                talkAgainButton.onClick.RemoveListener(RequestTalk);
                talkAgainButton.onClick.AddListener(RequestTalk);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(RequestClose);
                closeButton.onClick.AddListener(RequestClose);
            }
        }

        private void RemoveFixedListeners()
        {
            talkAgainButton?.onClick.RemoveListener(RequestTalk);
            closeButton?.onClick.RemoveListener(RequestClose);
        }

        private void ClearRows()
        {
            if (rows == null)
            {
                return;
            }

            foreach (CozyTownUiListRow row in rows)
            {
                row?.Clear();
            }
        }

        private static void ValidateUi(
            GameObject targetPanel,
            Text targetFeedbackText,
            CozyTownUiListRow[] targetRows,
            Image targetSelectionMarker,
            Image targetPortraitImage,
            Text targetDialogueText,
            Text targetMetadataText,
            Button targetTalkAgainButton,
            Button targetCloseButton,
            CozyTownUiIconCatalog targetIconCatalog)
        {
            if (targetPanel == null)
            {
                throw new ArgumentNullException(nameof(targetPanel));
            }

            if (targetFeedbackText == null)
            {
                throw new ArgumentNullException(nameof(targetFeedbackText));
            }

            if (targetRows == null)
            {
                throw new ArgumentNullException(nameof(targetRows));
            }

            foreach (CozyTownUiListRow row in targetRows)
            {
                if (row == null)
                {
                    throw new ArgumentException(
                        "NPC UI rows must not contain null entries.",
                        nameof(targetRows));
                }

                if (row.Buttons.Count < 1)
                {
                    throw new ArgumentException(
                        "Each NPC UI row must expose at least one Button.",
                        nameof(targetRows));
                }
            }

            if (targetSelectionMarker == null)
            {
                throw new ArgumentNullException(nameof(targetSelectionMarker));
            }

            if (targetPortraitImage == null)
            {
                throw new ArgumentNullException(nameof(targetPortraitImage));
            }

            if (targetDialogueText == null)
            {
                throw new ArgumentNullException(nameof(targetDialogueText));
            }

            if (targetMetadataText == null)
            {
                throw new ArgumentNullException(nameof(targetMetadataText));
            }

            if (targetTalkAgainButton == null)
            {
                throw new ArgumentNullException(nameof(targetTalkAgainButton));
            }

            if (targetCloseButton == null)
            {
                throw new ArgumentNullException(nameof(targetCloseButton));
            }

            if (targetIconCatalog == null)
            {
                throw new ArgumentNullException(nameof(targetIconCatalog));
            }
        }
    }
}
