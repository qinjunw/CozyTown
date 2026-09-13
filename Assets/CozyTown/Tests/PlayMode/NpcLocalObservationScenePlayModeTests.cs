#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Time;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcLocalObservationScenePlayModeTests
    {
        private Scene _scene;

        [UnityTest]
        public IEnumerator DevelopmentScene_AutomaticallyObservesPondAtBothMeetingStandingLocations()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var components = _scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            var driver = components.OfType<DaytimeClockDriver>().Single();
            driver.SetApplicationFocus(false);
            var controller = components.OfType<CozyTownTownLifeController>().Single();
            controller.enabled = false;
            var ren = components.OfType<NpcWorldResident2D>().Single(item => item.NpcId == DefaultMvpIds.Npcs.Fisher);
            var sora = components.OfType<NpcWorldResident2D>().Single(item => item.NpcId == DefaultMvpIds.Npcs.Cook);
            Assert.That(controller.GetObservation(ren.NpcId).RegionId, Is.Not.Null);
            Assert.That(controller.GetObservation(sora.NpcId).RegionId, Is.Not.Null);

            Visit(ren.NpcId, "rest.fisher_ren");
            Visit(sora.NpcId, "road.west_lane");
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            for (int minute = 0; minute < 120; minute++)
                driver.AdvanceFrame(WorldTimeProgress.EffectiveSecondsPerGameMinute);
            driver.SetApplicationFocus(false);

            Assert.That(ren.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(sora.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(ren.Position, Is.EqualTo(new Vector2(-3, -1.4f)));
            Assert.That(sora.Position, Is.EqualTo(new Vector2(-3, 0.4f)));
            foreach (var resident in new[] { ren, sora })
            {
                var observation = controller.GetObservation(resident.NpcId);
                Assert.That(observation.RegionId, Is.EqualTo("pond-surroundings"));
                Assert.That(observation.SpaceId, Is.EqualTo("outdoors"));
                Assert.That(observation.X, Is.EqualTo(resident.Position.x).Within(0.001));
                Assert.That(observation.Y, Is.EqualTo(resident.Position.y).Within(0.001));
                CollectionAssert.Contains(observation.NearbyEntityIds, "pond");
            }

            void Visit(string npcId, string targetLocationId)
            {
                var state = controller.GetAgentState(npcId);
                Assert.That(controller.SubmitActivity(new NpcActivityRequest(npcId, state.WorldRunId,
                    state.Revision, targetLocationId, NpcActivity.Resting, controller.GameTotalMinutes + 180)).IsSuccess,
                    Is.True);
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene);
        }
    }
}
#endif
