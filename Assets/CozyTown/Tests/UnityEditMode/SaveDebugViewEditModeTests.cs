using CozyTown.Unity.Save;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class SaveDebugViewEditModeTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Save View");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void Requests_AreVisibleGatedAndLoadRequiresExistingSave()
        {
            var view = _root.AddComponent<CozyTownSaveDebugView>();
            var saveCalls = 0;
            var loadCalls = 0;
            view.SaveRequested += () => saveCalls++;
            view.LoadRequested += () => loadCalls++;

            view.RequestSave();
            view.RequestLoad();
            Assert.That(saveCalls, Is.Zero);
            Assert.That(loadCalls, Is.Zero);

            view.Show(hasSave: false, "No save");
            view.RequestSave();
            view.RequestLoad();
            Assert.That(saveCalls, Is.EqualTo(1));
            Assert.That(loadCalls, Is.Zero);
            Assert.That(view.Feedback, Is.EqualTo("No save"));

            view.Show(hasSave: true, "Ready");
            view.RequestLoad();
            Assert.That(loadCalls, Is.EqualTo(1));
            Assert.That(view.HasSave, Is.True);
        }
    }
}
