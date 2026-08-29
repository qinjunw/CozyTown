using CozyTown.Runtime.Application;
using CozyTown.Runtime.Npc;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class NpcDebugViewEditModeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("NPC View");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void TalkAgain_IsRaisedOnlyWhileVisible()
        {
            var view = _root.AddComponent<CozyTownNpcDebugView>();
            var calls = 0;
            view.TalkRequested += () => calls++;

            view.RequestTalk();
            Assert.That(calls, Is.Zero);

            view.Show(new NpcDialogueViewState(
                "npc.farmer_eli",
                "Eli",
                "Good morning.",
                "happy",
                "wave",
                false,
                "request-1",
                NpcDialogueFallbackReason.None));
            view.RequestTalk();

            Assert.That(calls, Is.EqualTo(1));
        }
    }
}
