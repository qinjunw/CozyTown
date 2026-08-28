using System;
using CozyTown.Runtime.Application;
using CozyTown.Unity.Hud;
using UnityEngine;

namespace CozyTown.Unity.Kitchen
{
    public sealed class CozyTownKitchenDebugView : CozyTownModalDebugViewBase
    {
        public event Action<string> CookRequested;
        public CookingViewState State { get; private set; }

        public void Show(CookingViewState state, string feedback)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            ShowBase(feedback);
        }

        public void RequestCook(string recipeId)
        {
            if (IsVisible)
            {
                CookRequested?.Invoke(recipeId);
            }
        }

        private void OnGUI()
        {
            if (!BeginPanel("Kitchen") || State == null)
            {
                return;
            }
            foreach (var recipe in State.Recipes)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{recipe.OutputDisplayName} x{recipe.OutputQuantity} Ready:{recipe.HasIngredients}",
                    LabelStyle);
                if (GUILayout.Button("Cook", ButtonStyle))
                {
                    RequestCook(recipe.RecipeId);
                }
                GUILayout.EndHorizontal();
            }
            EndPanel();
        }
    }
}
