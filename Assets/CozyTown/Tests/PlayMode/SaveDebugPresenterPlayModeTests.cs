using System.Collections;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Core;
using CozyTown.Unity.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class SaveDebugPresenterPlayModeTests
    {
        private GameObject _root;

        [UnityTest]
        public IEnumerator SaveEnablesLoad_AndCommandsExposeExplicitFeedback()
        {
            var coordinator = new StubSaveCoordinator
            {
                SaveResult = OperationResult.Success(),
                LoadResult = OperationResult.Failure("save.payload_invalid")
            };
            CozyTownSaveDebugPresenter presenter = CreatePresenter(coordinator, out var view);
            yield return null;

            Assert.That(presenter.enabled, Is.True);
            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.HasSave, Is.False);
            view.RequestLoad();
            Assert.That(coordinator.LoadCalls, Is.Zero);

            view.RequestSave();
            Assert.That(coordinator.SaveCalls, Is.EqualTo(1));
            Assert.That(view.HasSave, Is.True);
            Assert.That(view.Feedback, Is.EqualTo("Game saved."));

            view.RequestLoad();
            Assert.That(coordinator.LoadCalls, Is.EqualTo(1));
            Assert.That(view.Feedback, Is.EqualTo("Load failed: save.payload_invalid"));
        }

        [UnityTest]
        public IEnumerator DisableThenEnable_HidesViewAndKeepsSingleSubscription()
        {
            var coordinator = new StubSaveCoordinator
            {
                SaveResult = OperationResult.Failure("save.write_failed")
            };
            CozyTownSaveDebugPresenter presenter = CreatePresenter(coordinator, out var view);
            yield return null;

            presenter.enabled = false;
            Assert.That(view.IsVisible, Is.False);
            view.RequestSave();
            Assert.That(coordinator.SaveCalls, Is.Zero);

            presenter.enabled = true;
            Assert.That(view.IsVisible, Is.True);
            view.RequestSave();
            Assert.That(coordinator.SaveCalls, Is.EqualTo(1));
            Assert.That(view.Feedback, Is.EqualTo("Save failed: save.write_failed"));
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private CozyTownSaveDebugPresenter CreatePresenter(
            IGameSaveCoordinator coordinator,
            out CozyTownSaveDebugView view)
        {
            _root = new GameObject("Save UI");
            _root.SetActive(false);
            view = _root.AddComponent<CozyTownSaveDebugView>();
            var presenter = _root.AddComponent<CozyTownSaveDebugPresenter>();
            presenter.Configure(view);
            presenter.Bind(coordinator);
            _root.SetActive(true);
            return presenter;
        }

        private sealed class StubSaveCoordinator : IGameSaveCoordinator
        {
            public bool HasSave { get; private set; }

            public OperationResult SaveResult { get; set; }

            public OperationResult LoadResult { get; set; }

            public int SaveCalls { get; private set; }

            public int LoadCalls { get; private set; }

            public OperationResult Save()
            {
                SaveCalls++;
                if (SaveResult.IsSuccess)
                {
                    HasSave = true;
                }

                return SaveResult;
            }

            public OperationResult Load()
            {
                LoadCalls++;
                return LoadResult;
            }
        }
    }
}
