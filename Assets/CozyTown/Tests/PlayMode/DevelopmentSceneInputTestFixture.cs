#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace CozyTown.Tests.PlayMode
{
    internal sealed class DevelopmentSceneInputTestFixture : InputTestFixture
    {
        private InputAction[] _previouslyEnabled = Array.Empty<InputAction>();

        public override void Setup()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/Settings/InputSystem_Actions.inputactions");
            Assert.That(actions, Is.Not.Null);
            _previouslyEnabled = actions.Where(action => action.enabled).ToArray();

            // Release shared scene-action state while its original input runtime is still current.
            // The test runtime must register its own state-change monitors when the scene loads.
            foreach (var map in actions.actionMaps)
                map.Dispose();

            base.Setup();
        }

        public override void TearDown()
        {
            base.TearDown();
            foreach (var action in _previouslyEnabled)
                action.Enable();
            _previouslyEnabled = Array.Empty<InputAction>();
        }
    }
}
#endif
