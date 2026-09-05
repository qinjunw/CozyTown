#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CozyTown.Runtime.Content;
using CozyTown.Unity.Bed;
using CozyTown.Unity.Interaction;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Save;
using CozyTown.Unity.Time;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class FourResidentRoutinePlayModeTests
    {
        private const string ScenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
        private Scene _scene;

        private static readonly Routine[] Routines =
        {
            new Routine(DefaultMvpIds.Npcs.Shopkeeper, "home.shopkeeper_mina",
                "work.shopkeeper_mina", "rest.shopkeeper_mina", "work.shopkeeper_mina",
                360, 480, 720, 780, 1020, 1080),
            new Routine(DefaultMvpIds.Npcs.Farmer, "home.farmer_eli",
                "work.farmer_eli", "rest.farmer_eli", "work.farmer_eli",
                330, 450, 690, 750, 990, 1050),
            new Routine(DefaultMvpIds.Npcs.Fisher, "home.fisher_ren",
                "work.fisher_ren.morning", "rest.fisher_ren", "work.fisher_ren.afternoon",
                345, 465, 735, 795, 1050, 1110),
            new Routine(DefaultMvpIds.Npcs.Cook, "home.cook_sora",
                "work.cook_sora", "rest.cook_sora", "work.cook_sora",
                390, 510, 780, 840, 1080, 1140)
        };

        [UnityTest]
        public IEnumerator FourResidents_FollowPersonalRoutinesAndCommuteWithinTheirWindows()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            var roots = _scene.GetRootGameObjects();
            var driver = roots.Single(root => root.name == "CozyTown").GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(false);
            var world = roots.Single(root => root.name == "World");
            var map = world.GetComponent<TownMap2D>();
            var residents = world.GetComponentsInChildren<NpcWorldResident2D>(true)
                .ToDictionary(resident => resident.NpcId);
            CollectionAssert.AreEquivalent(Routines.Select(routine => routine.NpcId), residents.Keys);
            foreach (var routine in Routines)
            {
                Assert.That(map.TryGetHome(routine.NpcId, out var home), Is.True, routine.NpcId);
                Assert.That(home.HomeId, Is.EqualTo(routine.HomeId), routine.NpcId);
                Assert.That(home.EntryLocationId, Is.EqualTo(routine.EntryId), routine.NpcId);
            }

            var commutes = new List<Commute>();
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            for (int minute = 361; minute <= 1440 + 510; minute++)
            {
                driver.AdvanceFrame(0.5);
                foreach (var routine in Routines)
                {
                    var resident = residents[routine.NpcId];
                    Assert.That(resident.Status, Is.Not.EqualTo(TownRouteStatus.Blocked),
                        $"{routine.NpcId} became blocked at absolute minute {minute}.");
                    if (minute == routine.MorningDeadline)
                        AssertArrived(map, resident, routine.MorningId, false);
                    if (minute == routine.RestStart)
                        AssertDeparting(map, resident, routine.MorningId, routine.RestId);
                    if (minute == routine.RestStart + 15)
                        AssertArrived(map, resident, routine.RestId, false);
                    if (minute == routine.AfternoonStart)
                        AssertDeparting(map, resident, routine.RestId, routine.AfternoonId);
                    if (minute == routine.AfternoonStart + 20)
                        AssertArrived(map, resident, routine.AfternoonId, false);
                    if (minute == routine.ReturnStart)
                    {
                        AssertDeparting(map, resident, routine.AfternoonId, routine.EntryId);
                        commutes.Add(new Commute(map, resident, routine.AfternoonId,
                            routine.EntryId, minute, 60, "return"));
                    }
                    if (minute == routine.HomeDeadline || minute == 1440 + routine.Departure - 1)
                        AssertArrived(map, resident, routine.EntryId, true);
                    if (minute == 1440 + routine.Departure)
                    {
                        AssertDeparting(map, resident, routine.EntryId, routine.MorningId);
                        commutes.Add(new Commute(map, resident, routine.EntryId,
                            routine.MorningId, minute, 120, "outbound"));
                    }
                    if (minute == 1440 + routine.MorningDeadline)
                        AssertArrived(map, resident, routine.MorningId, false);
                }
                foreach (var commute in commutes) commute.Observe(minute);
            }
            driver.SetApplicationFocus(false);
            Assert.That(commutes, Has.Count.EqualTo(8));
            Assert.That(commutes.All(commute => commute.Completed), Is.True,
                "All four return journeys and next-morning departures must physically arrive.");
        }

        [UnityTest]
        public IEnumerator WorkingResidents_FaceTheirObjectsAfterArrivalAndLoadWithoutChangingRouteHeadings()
        {
            yield return LoadPausedScene();
            var world = RequireRoot("World");
            var map = world.GetComponent<TownMap2D>();
            var residents = world.GetComponentsInChildren<NpcWorldResident2D>(true)
                .ToDictionary(resident => resident.NpcId);
            var save = RequireRoot("Debug HUD").GetComponent<CozyTownSaveDebugView>();
            string[] morningDirections = { "left", "left", "right", "right" };
            string[] afternoonDirections = { "left", "left", "left", "right" };

            AdvanceMinutes(150);
            AssertWorkFacing(map, residents, false, morningDirections);
            save.RequestSave();
            Assert.That(save.Feedback, Is.EqualTo("Game saved."));
            AdvanceMinutes(180);
            save.RequestLoad();
            Assert.That(save.Feedback, Is.EqualTo("Game loaded."));
            AssertWorkFacing(map, residents, false, morningDirections);

            AdvanceMinutes(360);
            AssertWorkFacing(map, residents, true, afternoonDirections);
            save.RequestSave();
            Assert.That(save.Feedback, Is.EqualTo("Game saved."));
            AdvanceMinutes(210);
            save.RequestLoad();
            Assert.That(save.Feedback, Is.EqualTo("Game loaded."));
            AssertWorkFacing(map, residents, true, afternoonDirections);

            AdvanceMinutes(915);
            var ren = residents[DefaultMvpIds.Npcs.Fisher];
            Vector2 departurePosition = ren.Position;
            var driver = RequireRoot("CozyTown").GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            driver.AdvanceFrame(0.25);
            driver.SetApplicationFocus(false);
            Assert.That(ren.Status, Is.EqualTo(TownRouteStatus.Travelling));
            Assert.That(Vector2.Distance(ren.Position, departurePosition + Vector2.down * 0.5f), Is.LessThan(0.001f));
            Assert.That(ren.FacingDirection, Is.EqualTo(Vector2.down));
            Assert.That(ren.transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.name,
                Does.StartWith("npc_fisher_ren_walk_down_"));

            var obstruction = new GameObject("Work-facing test obstruction");
            obstruction.transform.SetParent(world.transform, false);
            obstruction.transform.position = ren.Position;
            obstruction.AddComponent<BoxCollider2D>().size = new Vector2(0.1f, 0.1f);
            Physics2D.SyncTransforms();
            Vector2 blockedPosition = ren.Position;
            AdvanceMinutes(1);
            Assert.That(ren.Status, Is.EqualTo(TownRouteStatus.Blocked));
            Assert.That(ren.Position, Is.EqualTo(blockedPosition));
            Assert.That(ren.FacingDirection, Is.EqualTo(Vector2.down));
            Assert.That(ren.transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.name,
                Is.EqualTo("npc_fisher_ren_idle_down"));
        }

        private static void AssertWorkFacing(TownMap2D map,
            Dictionary<string, NpcWorldResident2D> residents, bool afternoon, string[] directions)
        {
            for (int index = 0; index < Routines.Length; index++)
            {
                var routine = Routines[index];
                var resident = residents[routine.NpcId];
                AssertArrived(map, resident, afternoon ? routine.AfternoonId : routine.MorningId, false);
                Vector2 expectedFacing = directions[index] == "left" ? Vector2.left : Vector2.right;
                Assert.That(resident.FacingDirection, Is.EqualTo(expectedFacing), routine.NpcId);
                Assert.That(resident.transform.Find("Visual").GetComponent<SpriteRenderer>().sprite.name,
                    Is.EqualTo(routine.NpcId.Replace('.', '_') + "_idle_" + directions[index]));
            }
        }

        [UnityTest]
        public IEnumerator SleepAcrossEveningDepartures_MatchesMinuteStepsWhileSoraIsStillReturning()
        {
            yield return CompareSleepWithMinuteSteps(961, 2, "Slept to Day 1 18:01.",
                DefaultMvpIds.Npcs.Cook, "home.cook_sora.entry");
        }

        [UnityTest]
        public IEnumerator SleepAcrossMidnight_MatchesMinuteStepsWhileEliIsLeavingHome()
        {
            yield return CompareSleepWithMinuteSteps(1411, 6, "Slept to Day 2 05:31.",
                DefaultMvpIds.Npcs.Farmer, "work.farmer_eli");
        }

        private IEnumerator CompareSleepWithMinuteSteps(int startMinute, int sleepHours,
            string expectedFeedback, string travellingNpcId, string travellingTarget)
        {
            yield return LoadPausedScene();
            AdvanceMinutes(startMinute - 360);
            var before = CaptureResidents();
            AdvanceMinutes(sleepHours * 60);
            var afterMinuteSteps = CaptureResidents();
            foreach (var routine in Routines)
            {
                var resident = afterMinuteSteps[routine.NpcId];
                bool travelling = routine.NpcId == travellingNpcId;
                Assert.That(resident.IsHome, Is.EqualTo(!travelling), routine.NpcId);
                Assert.That(resident.Status, Is.EqualTo(travelling
                    ? TownRouteStatus.Travelling : TownRouteStatus.Arrived), routine.NpcId);
                Assert.That(resident.Target, Is.EqualTo(travelling
                    ? travellingTarget : routine.EntryId), routine.NpcId);
            }

            yield return UnloadScene();
            yield return LoadPausedScene();
            AdvanceMinutes(startMinute - 360);
            AssertResidentsMatch(before);
            var bed = RequireRoot("World").GetComponentsInChildren<TownInteractionPoint2D>(true)
                .Single(point => point.Kind == TownInteractionKind.Bed);
            var view = RequireRoot("Debug HUD").GetComponent<CozyTownBedDebugView>();
            bed.Interact(new InteractionContext(RequireRoot("Player")));
            Assert.That(view.IsVisible, Is.True);
            for (int clicks = 0; clicks < 8 - sleepHours; clicks++) view.RequestDecreaseSleepHours();
            Assert.That(view.SelectedSleepHours, Is.EqualTo(sleepHours));
            view.RequestSleep();
            Assert.That(view.Feedback, Is.EqualTo(expectedFeedback));
            AssertResidentsMatch(afterMinuteSteps);
            view.RequestClose();
            Assert.That(view.IsVisible, Is.False);
        }

        private IEnumerator LoadPausedScene()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(ScenePath);
            RequireRoot("CozyTown").GetComponent<DaytimeClockDriver>().SetApplicationFocus(false);
        }

        private void AdvanceMinutes(int minutes)
        {
            var driver = RequireRoot("CozyTown").GetComponent<DaytimeClockDriver>();
            driver.SetApplicationFocus(true);
            driver.AdvanceFrame(0);
            for (int minute = 0; minute < minutes; minute++) driver.AdvanceFrame(0.5);
            driver.SetApplicationFocus(false);
        }

        private Dictionary<string, ResidentSnapshot> CaptureResidents()
        {
            var residents = RequireRoot("World").GetComponentsInChildren<NpcWorldResident2D>(true);
            CollectionAssert.AreEquivalent(Routines.Select(routine => routine.NpcId),
                residents.Select(resident => resident.NpcId));
            foreach (var resident in residents) AssertPresence(resident, !resident.IsHome);
            return residents.ToDictionary(resident => resident.NpcId,
                resident => new ResidentSnapshot(resident));
        }

        private void AssertResidentsMatch(Dictionary<string, ResidentSnapshot> expected)
        {
            var actual = CaptureResidents();
            foreach (var routine in Routines)
            {
                var left = expected[routine.NpcId];
                var right = actual[routine.NpcId];
                Assert.That(Vector2.Distance(left.Position, right.Position), Is.LessThan(0.001f), routine.NpcId);
                Assert.That(right.Target, Is.EqualTo(left.Target), routine.NpcId);
                Assert.That(right.Status, Is.EqualTo(left.Status), routine.NpcId);
                Assert.That(right.IsHome, Is.EqualTo(left.IsHome), routine.NpcId);
                Assert.That(right.Facing, Is.EqualTo(left.Facing), routine.NpcId);
            }
        }

        private GameObject RequireRoot(string name) => _scene.GetRootGameObjects()
            .Single(root => root.name == name);

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            if (_scene.IsValid() && _scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_scene);
        }

        private static void AssertDeparting(TownMap2D map, NpcWorldResident2D resident,
            string originId, string targetId)
        {
            AssertPosition(map, resident, originId);
            Assert.That(resident.TargetLocationId, Is.EqualTo(targetId), resident.NpcId);
            Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Travelling), resident.NpcId);
            Assert.That(resident.IsHome, Is.False, resident.NpcId);
            AssertPresence(resident, true);
        }

        private static void AssertArrived(TownMap2D map, NpcWorldResident2D resident,
            string locationId, bool home)
        {
            AssertPosition(map, resident, locationId);
            Assert.That(resident.TargetLocationId, Is.EqualTo(locationId), resident.NpcId);
            Assert.That(resident.Status, Is.EqualTo(TownRouteStatus.Arrived), resident.NpcId);
            Assert.That(resident.IsHome, Is.EqualTo(home), resident.NpcId);
            AssertPresence(resident, !home);
        }

        private static void AssertPosition(TownMap2D map, NpcWorldResident2D resident, string locationId)
        {
            Assert.That(map.TryGetLocation(locationId, out var expected), Is.True, locationId);
            Assert.That(Vector2.Distance(resident.Position, expected), Is.LessThan(0.001f),
                $"{resident.NpcId} should physically occupy {locationId}.");
        }

        private static void AssertPresence(NpcWorldResident2D resident, bool visible)
        {
            Assert.That(resident.transform.Find("Visual").GetComponent<SpriteRenderer>().enabled,
                Is.EqualTo(visible), resident.NpcId);
            var point = resident.GetComponent<TownInteractionPoint2D>();
            Assert.That(point.enabled, Is.EqualTo(visible), resident.NpcId);
            Assert.That(point.PromptAnchor.gameObject.activeSelf, Is.EqualTo(visible), resident.NpcId);
            var colliders = resident.GetComponents<Collider2D>();
            Assert.That(colliders, Is.Not.Empty, resident.NpcId);
            Assert.That(colliders.All(collider => collider.enabled == visible), Is.True, resident.NpcId);
        }

        private sealed class Commute
        {
            private readonly NpcWorldResident2D _resident;
            private readonly string _destination;
            private readonly int _startedAt;
            private readonly int _windowMinutes;
            private readonly string _direction;
            private readonly float _roadLength;

            public Commute(TownMap2D map, NpcWorldResident2D resident, string origin,
                string destination, int startedAt, int windowMinutes, string direction)
            {
                _resident = resident;
                _destination = destination;
                _startedAt = startedAt;
                _windowMinutes = windowMinutes;
                _direction = direction;
                Assert.That(map.TryFindRoute(origin, destination, out var route), Is.True, resident.NpcId);
                for (int index = 1; index < route.Count; index++)
                    _roadLength += Vector2.Distance(route[index - 1], route[index]);
                Assert.That(_roadLength, Is.GreaterThan(0), resident.NpcId);
            }

            public bool Completed { get; private set; }

            public void Observe(int minute)
            {
                if (Completed || _resident.TargetLocationId != _destination
                    || _resident.Status != TownRouteStatus.Arrived) return;
                int elapsedMinutes = minute - _startedAt;
                double elapsedSeconds = elapsedMinutes * 0.5;
                Assert.That(elapsedMinutes, Is.InRange(1, _windowMinutes), _resident.NpcId);
                Assert.That(elapsedSeconds, Is.GreaterThanOrEqualTo(_roadLength / 2.0 - 0.001),
                    $"{_resident.NpcId} must not teleport or exceed 2 world units per effective second.");
                Assert.That(elapsedSeconds, Is.LessThan(_roadLength / 2.0 + 0.501),
                    $"{_resident.NpcId} must spend the accepted walking budget without idle gaps.");
                Completed = true;
                Debug.Log(
                    $"{_resident.NpcId} {_direction}: {_roadLength:F3} road units; "
                    + $"arrival observed after {elapsedSeconds:F1} effective seconds / "
                    + $"{elapsedMinutes} game minutes, window {_windowMinutes} game minutes.");
            }
        }

        private sealed class ResidentSnapshot
        {
            public ResidentSnapshot(NpcWorldResident2D resident)
            {
                Position = resident.Position;
                Target = resident.TargetLocationId;
                Status = resident.Status;
                IsHome = resident.IsHome;
                Facing = resident.FacingDirection;
            }

            public Vector2 Position { get; }
            public string Target { get; }
            public TownRouteStatus Status { get; }
            public bool IsHome { get; }
            public Vector2 Facing { get; }
        }

        private sealed class Routine
        {
            public Routine(string npcId, string homeId, string morningId, string restId,
                string afternoonId, int departure, int morningDeadline, int restStart,
                int afternoonStart, int returnStart, int homeDeadline)
            {
                NpcId = npcId;
                HomeId = homeId;
                MorningId = morningId;
                RestId = restId;
                AfternoonId = afternoonId;
                Departure = departure;
                MorningDeadline = morningDeadline;
                RestStart = restStart;
                AfternoonStart = afternoonStart;
                ReturnStart = returnStart;
                HomeDeadline = homeDeadline;
            }

            public string NpcId { get; }
            public string HomeId { get; }
            public string EntryId => HomeId + ".entry";
            public string MorningId { get; }
            public string RestId { get; }
            public string AfternoonId { get; }
            public int Departure { get; }
            public int MorningDeadline { get; }
            public int RestStart { get; }
            public int AfternoonStart { get; }
            public int ReturnStart { get; }
            public int HomeDeadline { get; }
        }
    }
}
#endif
