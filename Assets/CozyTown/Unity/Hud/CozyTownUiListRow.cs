using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CozyTown.Unity.Hud
{
    public sealed class CozyTownUiListRow : MonoBehaviour
    {
        [SerializeField] private Text label;
        [SerializeField] private Image icon;
        [SerializeField] private Button[] buttons = Array.Empty<Button>();
        [SerializeField] private Text[] buttonLabels = Array.Empty<Text>();

        public Text Label => label;

        public Image Icon => icon;

        public IReadOnlyList<Button> Buttons => Array.AsReadOnly(buttons);

        public IReadOnlyList<Text> ButtonLabels => Array.AsReadOnly(buttonLabels);

        public void Configure(
            Text configuredLabel,
            Image configuredIcon,
            Button[] configuredButtons,
            Text[] configuredButtonLabels)
        {
            label = configuredLabel != null
                ? configuredLabel
                : throw new ArgumentNullException(nameof(configuredLabel));
            icon = configuredIcon != null
                ? configuredIcon
                : throw new ArgumentNullException(nameof(configuredIcon));

            if (configuredButtons == null)
            {
                throw new ArgumentNullException(nameof(configuredButtons));
            }

            if (configuredButtonLabels == null)
            {
                throw new ArgumentNullException(nameof(configuredButtonLabels));
            }

            if (configuredButtons.Length != configuredButtonLabels.Length)
            {
                throw new ArgumentException("Button and label arrays must have the same length.");
            }

            if (Array.Exists(configuredButtons, value => value == null)
                || Array.Exists(configuredButtonLabels, value => value == null))
            {
                throw new ArgumentException("Button and label entries must not be null.");
            }

            buttons = (Button[])configuredButtons.Clone();
            buttonLabels = (Text[])configuredButtonLabels.Clone();
        }

        public void SetContent(string text, Sprite sprite)
        {
            EnsureConfigured();
            gameObject.SetActive(true);
            label.text = text ?? string.Empty;
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        public void SetButton(
            int index,
            string text,
            bool interactable,
            UnityAction listener)
        {
            EnsureConfigured();
            if (index < 0 || index >= buttons.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var button = buttons[index];
            button.onClick.RemoveAllListeners();
            if (listener != null)
            {
                button.onClick.AddListener(listener);
            }

            button.gameObject.SetActive(true);
            button.interactable = interactable;
            buttonLabels[index].text = text ?? string.Empty;
        }

        public void HideUnusedButtons(int usedButtonCount)
        {
            EnsureConfigured();
            if (usedButtonCount < 0 || usedButtonCount > buttons.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(usedButtonCount));
            }

            for (var index = usedButtonCount; index < buttons.Length; index++)
            {
                buttons[index].onClick.RemoveAllListeners();
                buttons[index].interactable = false;
                buttons[index].gameObject.SetActive(false);
                buttonLabels[index].text = string.Empty;
            }
        }

        public void Clear()
        {
            if (label != null)
            {
                label.text = string.Empty;
            }

            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            for (var index = 0; index < buttons.Length; index++)
            {
                if (buttons[index] != null)
                {
                    buttons[index].onClick.RemoveAllListeners();
                    buttons[index].interactable = false;
                    buttons[index].gameObject.SetActive(false);
                }

                if (index < buttonLabels.Length && buttonLabels[index] != null)
                {
                    buttonLabels[index].text = string.Empty;
                }
            }

            gameObject.SetActive(false);
        }

        private void EnsureConfigured()
        {
            if (label == null
                || icon == null
                || buttons == null
                || buttonLabels == null
                || buttons.Length != buttonLabels.Length)
            {
                throw new InvalidOperationException("UI list row has not been configured.");
            }
        }
    }
}
