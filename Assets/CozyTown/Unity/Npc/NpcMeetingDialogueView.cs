using System;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using UnityEngine;
using UnityEngine.UI;

namespace CozyTown.Unity.Npc
{
    public sealed class NpcMeetingDialogueView : MonoBehaviour
    {
        private NpcWorldResident2D[] _residents;
        private Dictionary<string, string> _names;
        private GameObject _bubble;
        private Text _text;
        private NpcConversationLine _lastLine;
        private double _visibleUntil;

        public string VisibleText => _bubble != null && _bubble.activeSelf ? _text.text : string.Empty;

        public void Configure(NpcWorldResident2D[] residents, IEnumerable<NpcDefinition> profiles)
        {
            _residents = residents;
            _names = profiles.ToDictionary(item => item.Id, item => item.DisplayName);
            if (_bubble != null) Destroy(_bubble);
            _bubble = new GameObject("Resident conversation", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            _bubble.transform.SetParent(transform, false);
            var canvas = _bubble.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = _bubble.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(960, 540);
            scaler.matchWidthOrHeight = 0.5f;
            var panel = new GameObject("Conversation panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_bubble.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0);
            rect.pivot = new Vector2(0.5f, 0);
            rect.anchoredPosition = new Vector2(0, 24);
            rect.sizeDelta = new Vector2(560, 104);
            var background = panel.GetComponent<Image>();
            background.color = new Color(0.08f, 0.12f, 0.13f, 0.95f);
            background.raycastTarget = false;
            var label = new GameObject("Speech", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(panel.transform, false);
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(14, 8); labelRect.offsetMax = new Vector2(-14, -8);
            _text = label.GetComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = 19;
            _text.resizeTextForBestFit = true;
            _text.resizeTextMinSize = 14;
            _text.resizeTextMaxSize = 19;
            _text.color = new Color(1f, 0.96f, 0.84f);
            _text.alignment = TextAnchor.MiddleLeft;
            _text.supportRichText = false;
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _lastLine = null;
            _bubble.SetActive(false);
        }

        public void Present(NpcMeetingBoard board, double realSeconds)
        {
            if (board == null || _bubble == null) return;
            var line = _residents.Select(item => board.GetLatest(item.NpcId)).Where(item => item != null)
                .SelectMany(item => item.Transcript).OrderBy(item => item.TotalMinutes).LastOrDefault();
            if (line == null) { _bubble.SetActive(false); _lastLine = null; return; }
            if (!ReferenceEquals(line, _lastLine))
            {
                _lastLine = line;
                _visibleUntil = realSeconds + 6;
                string name = _names.TryGetValue(line.SpeakerId, out var display) ? display : line.SpeakerId;
                _text.text = name + ": " + line.Text;
            }
            var speaker = _residents.First(item => item.NpcId == line.SpeakerId);
            _bubble.SetActive(realSeconds < _visibleUntil && speaker.isActiveAndEnabled && !speaker.IsHome);
        }

        private void OnDestroy()
        {
            if (_bubble != null) Destroy(_bubble);
        }
    }
}
