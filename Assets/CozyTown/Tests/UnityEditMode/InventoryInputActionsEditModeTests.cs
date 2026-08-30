using System.Linq;
using CozyTown.Unity.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class InventoryInputActionsEditModeTests
    {
        private const string InputActionsPath = "Assets/Settings/InputSystem_Actions.inputactions";

        [Test]
        public void PlayerActions_ExposeBackpackAndFiveExclusiveNumberBindings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Assert.That(asset, Is.Not.Null);
            var player = asset.FindActionMap("Player", true);

            Assert.That(
                player.FindAction("Backpack", true).bindings.Select(binding => binding.path),
                Does.Contain("<Keyboard>/b"));
            for (var slot = 1; slot <= 5; slot++)
            {
                Assert.That(
                    player.FindAction($"Hotbar{slot}", true).bindings.Select(binding => binding.path),
                    Does.Contain($"<Keyboard>/{slot}"));
            }

            Assert.That(
                player.FindAction("Previous", true).bindings.Select(binding => binding.path),
                Does.Not.Contain("<Keyboard>/1"));
            Assert.That(
                player.FindAction("Next", true).bindings.Select(binding => binding.path),
                Does.Not.Contain("<Keyboard>/2"));
            Assert.That(
                typeof(IInventoryUiInputSource).IsAssignableFrom(typeof(InputSystemPlayerInputSource)),
                Is.True);
        }
    }
}
