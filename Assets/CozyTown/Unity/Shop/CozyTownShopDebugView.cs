using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Shop
{
    public sealed class CozyTownShopDebugView : MonoBehaviour, ICozyTownShopDebugView
    {
        private Vector2 _scrollPosition;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private int _fontSize;

        public event Action<string> BuyRequested;

        public event Action<string> SellRequested;

        public event Action CloseRequested;

        public bool IsVisible { get; private set; }

        public ShopViewState State { get; private set; }

        public string Feedback { get; private set; } = string.Empty;

        public void Show(ShopViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Feedback = feedback ?? string.Empty;
            IsVisible = true;
        }

        public void Hide()
        {
            IsVisible = false;
        }

        public void RequestBuy(string itemId)
        {
            if (IsVisible && !string.IsNullOrWhiteSpace(itemId))
            {
                BuyRequested?.Invoke(itemId);
            }
        }

        public void RequestSell(string itemId)
        {
            if (IsVisible && !string.IsNullOrWhiteSpace(itemId))
            {
                SellRequested?.Invoke(itemId);
            }
        }

        public void RequestClose()
        {
            if (IsVisible)
            {
                CloseRequested?.Invoke();
            }
        }

        private void OnGUI()
        {
            if (!IsVisible || State == null)
            {
                return;
            }

            EnsureStyles();
            var width = Mathf.Min(820f, Screen.width - 32f);
            var height = Mathf.Min(700f, Screen.height - 64f);
            var left = (Screen.width - width) * 0.5f;
            var top = (Screen.height - height) * 0.5f;

            GUILayout.BeginArea(new Rect(left, top, width, height), _boxStyle);
            GUILayout.Label($"Town Shop — Coins: {State.Balance}", _labelStyle);
            if (!string.IsNullOrWhiteSpace(Feedback))
            {
                GUILayout.Label(Feedback, _labelStyle);
            }

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            foreach (var item in State.Items)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{item.DisplayName}  Owned: {item.OwnedQuantity}",
                    _labelStyle,
                    GUILayout.ExpandWidth(true));
                if (item.BuyPrice > 0
                    && GUILayout.Button(
                        $"Buy 1 ({item.BuyPrice})",
                        _buttonStyle,
                        GUILayout.Width(180f)))
                {
                    RequestBuy(item.ItemId);
                }

                if (item.SellPrice > 0
                    && GUILayout.Button(
                        $"Sell 1 ({item.SellPrice})",
                        _buttonStyle,
                        GUILayout.Width(180f)))
                {
                    RequestSell(item.ItemId);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            if (GUILayout.Button("Close", _buttonStyle, GUILayout.Height(_fontSize + 18f)))
            {
                RequestClose();
            }

            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            var fontSize = CozyTownDebugGuiStyles.CalculateFontSize(Screen.height);
            if (_labelStyle != null && _fontSize == fontSize)
            {
                return;
            }

            _fontSize = fontSize;
            _labelStyle = CozyTownDebugGuiStyles.CreateLabelStyle(fontSize);
            _boxStyle = CozyTownDebugGuiStyles.CreateBoxStyle();
            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold
            };
        }
    }
}
