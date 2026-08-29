using System;
using CozyTown.Runtime.Application;
using UnityEngine;

namespace CozyTown.Unity.Coop
{
    public sealed class CozyTownCoopWorldView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer henRenderer;
        [SerializeField] private Sprite idleSprite;
        [SerializeField] private Sprite fedSprite;
        [SerializeField] private Sprite productReadySprite;

        public void Configure(
            SpriteRenderer renderer,
            Sprite idle,
            Sprite fed,
            Sprite productReady)
        {
            henRenderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            idleSprite = idle ?? throw new ArgumentNullException(nameof(idle));
            fedSprite = fed ?? throw new ArgumentNullException(nameof(fed));
            productReadySprite = productReady
                ?? throw new ArgumentNullException(nameof(productReady));
        }

        public void Show(LivestockViewState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (henRenderer == null
                || idleSprite == null
                || fedSprite == null
                || productReadySprite == null
                || state.Animals.Count != 1)
            {
                throw new InvalidOperationException(
                    "Coop world view requires one animal and three configured state Sprites.");
            }

            var animal = state.Animals[0];
            henRenderer.sprite = animal.ProductReady
                ? productReadySprite
                : animal.FedToday
                    ? fedSprite
                    : idleSprite;
        }
    }
}
