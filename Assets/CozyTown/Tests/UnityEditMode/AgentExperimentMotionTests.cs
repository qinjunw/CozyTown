using System.Collections;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CozyTown.Unity.Editor;
using CozyTown.Unity.Experiments;
using CozyTown.Unity.Npc;
using CozyTown.Unity.Town;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class AgentExperimentMotionTests
    {
        [UnityTest]
        public IEnumerator Run_MovesVisibleResidentsInSmallStepsAndPauseDoesNotAccumulateWorldTime()
        {
            var scene = EditorSceneManager.OpenScene("Assets/CozyTown/Scenes/CozyTown_Dev.unity");
            AgentExperimentWindow.Install(scene, new AgentExperimentLaunchOptions
            {
                arm = AgentExperimentArm.F,
                runMode = AgentExperimentRunMode.Fixed,
                sourceRevision = "motion-regression-test"
            });
            yield return new EnterPlayMode();
            yield return null;

            var launcher = Object.FindFirstObjectByType<AgentExperimentLauncher>();
            Assert.That(launcher.Error, Is.Null);
            Assert.That(launcher.Session.Started, Is.True);
            // The accepted Mina/Eli meeting starts at 12:20; sample its actual route.
            for (int minute = 0; minute < 21; minute++) launcher.AdvanceOneMinute();
            var residents = Object.FindObjectsByType<NpcWorldResident2D>(FindObjectsSortMode.None)
                .Where(resident => resident.IsPresentInWorld && resident.Status == TownRouteStatus.Travelling).ToArray();
            Assert.That(residents.Length, Is.GreaterThan(0), "The sample needs a visible resident travelling to the meeting.");
            var visuals = residents.Select(resident => resident.GetComponentsInChildren<SpriteRenderer>()
                .Single(renderer => renderer.gameObject.name == "Visual")).ToArray();
            var offsets = visuals.Select((visual, i) => (Vector2)visual.transform.position - residents[i].Position).ToArray();
            var sprites = visuals.Select(visual => visual.sprite).ToArray();
            Assert.That(visuals.All(visual => visual.enabled), Is.True);
            bool animationChanged = false;
            int firstFrame = Time.frameCount;
            var previous = residents.Select(resident => resident.Position).ToArray();
            double last = launcher.RealSeconds;
            double started = last;
            double end = last + 2;
            double minutesBefore = launcher.Session.Controller.GameTotalMinutes;
            double previousMinutes = minutesBefore;
            double uninterruptedUntil = last, uninterruptedMinutes = minutesBefore;
            bool interrupted = false;
            float largestJump = 0;
            int movingFrames = 0;
            int observedFrames = 0;
            launcher.SetRunning(true);
            while (launcher.RealSeconds < end)
            {
                yield return null;
                Assert.That(launcher.Error, Is.Null);
                double now = launcher.RealSeconds;
                double minutes = launcher.Session.Controller.GameTotalMinutes;
                Assert.That(minutes - previousMinutes, Is.LessThanOrEqualTo(0.251),
                    "A frame may accept at most eight 1/64-second steps, even after a long stall.");
                interrupted |= now - last >= 0.125;
                if (!interrupted) { uninterruptedUntil = now; uninterruptedMinutes = minutes; }
                bool moved = false;
                for (int i = 0; i < residents.Length; i++)
                {
                    float distance = Vector2.Distance(previous[i], residents[i].Position);
                    Assert.That(distance, Is.LessThanOrEqualTo((minutes - previousMinutes) + 0.001),
                        "At 2 units/s and 0.5s/game-minute, position must not outrun accepted world time.");
                    Assert.That(Vector2.Distance((Vector2)visuals[i].transform.position - residents[i].Position, offsets[i]), Is.LessThan(0.0001f));
                    animationChanged |= visuals[i].sprite != sprites[i];
                    if (now - last < 0.08)
                    {
                        largestJump = Mathf.Max(largestJump, distance);
                        moved |= distance > 0.001f;
                    }
                    previous[i] = residents[i].Position;
                }
                if (now - last < 0.08) observedFrames++;
                if (moved) movingFrames++;
                last = now;
                previousMinutes = minutes;
            }
            launcher.SetRunning(false);
            TestContext.WriteLine($"Observed {observedFrames} short samples over {Time.frameCount - firstFrame} player-loop frames, {movingFrames} moving samples; largest movement {largestJump:F4} units.");
            Assert.That(observedFrames, Is.GreaterThan(15), "The runner must provide enough rendered frames to assess motion.");
            Assert.That(movingFrames, Is.GreaterThan(8), "A travelling resident must move more frequently than twice a second.");
            Assert.That(largestJump, Is.LessThanOrEqualTo(0.27f), "Even eight catch-up steps must not jump a whole world unit.");
            Assert.That(animationChanged, Is.True, "The visible walking sprite must animate.");
            if (uninterruptedUntil - started < 0.5)
                Assert.Inconclusive("The runner stalled before a half-second motion sample; repeat this fixture in an idle editor.");
            TestContext.WriteLine($"Uninterrupted rate sample: {uninterruptedUntil - started:F4}s; long frame observed: {interrupted}.");
            Assert.That(uninterruptedMinutes - minutesBefore, Is.EqualTo((uninterruptedUntil - started) * 2).Within(0.1),
                "Continuous running must retain the configured world-time rate.");

            var pausedPositions = residents.Select(resident => resident.Position).ToArray();
            int pausedTick = launcher.Session.Tick;
            double pausedUntil = launcher.RealSeconds + 0.3;
            while (launcher.RealSeconds < pausedUntil) yield return null;
            Assert.That(launcher.Session.Tick, Is.EqualTo(pausedTick));
            CollectionAssert.AreEqual(pausedPositions, residents.Select(resident => resident.Position).ToArray());
            launcher.SetRunning(true);
            double resumedAt = launcher.RealSeconds;
            while (launcher.RealSeconds < resumedAt + 0.15) yield return null;
            launcher.SetRunning(false);
            Assert.That(launcher.Session.Controller.GameTotalMinutes - previousMinutes,
                Is.EqualTo((launcher.RealSeconds - resumedAt) * 2).Within(0.1));
            double beforeManualStep = launcher.Session.Controller.GameTotalMinutes;
            launcher.AdvanceOneMinute();
            Assert.That(launcher.Session.Controller.GameTotalMinutes, Is.EqualTo(beforeManualStep + 1).Within(0.000001));
        }

        [UnityTest]
        public IEnumerator Replay_PacesEachRecordedDurationAndConsumesZeroDurationOperationsWithoutExtraWaits()
        {
            var scene = EditorSceneManager.OpenScene("Assets/CozyTown/Scenes/CozyTown_Dev.unity");
            AgentExperimentWindow.Install(scene, new AgentExperimentLaunchOptions
            {
                arm = AgentExperimentArm.S, runMode = AgentExperimentRunMode.Fixed, sourceRevision = "motion-replay-test"
            });
            yield return new EnterPlayMode();
            yield return null;
            var source = Object.FindFirstObjectByType<AgentExperimentLauncher>();
            Assert.That(source.Error, Is.Null);
            source.AdvanceOneMinute();
            source.Session.Step(1.0 / 64, source.RealSeconds);
            for (int i = 0; i < 20; i++) source.PumpResponses();
            source.Session.Save("fractional");
            source.Session.Load("fractional");
            for (int i = 0; i < 4; i++) source.PumpResponses();
            source.Session.Complete();
            int recordedTicks = source.Session.Tick;
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-motion", "packages", Guid.NewGuid().ToString("N")));
            source.Session.Export(directory);
            yield return new ExitPlayMode();

            scene = EditorSceneManager.OpenScene("Assets/CozyTown/Scenes/CozyTown_Dev.unity");
            AgentExperimentWindow.Install(scene, new AgentExperimentLaunchOptions
            {
                runMode = AgentExperimentRunMode.Replay, replayDirectory = directory
            });
            yield return new EnterPlayMode();
            yield return null;
            var replay = Object.FindFirstObjectByType<AgentExperimentLauncher>();
            Assert.That(replay.Error, Is.Null);
            replay.SetRunning(true);
            double waitStarted = replay.RealSeconds;
            double waitUntil = waitStarted + 0.1;
            while (replay.RealSeconds < waitUntil) yield return null;
            Assert.That(replay.RealSeconds - waitStarted, Is.LessThan(0.5), "The runner stalled past the legacy playback interval; this sample cannot assess early playback.");
            Assert.That(replay.Session.Tick, Is.Zero, "A legacy 0.5s input must not play at the new 1/64s cadence.");
            replay.AdvanceOneMinute();
            Assert.That(replay.Session.Tick, Is.EqualTo(1), "Manual replay consumes exactly one original input.");
            replay.SetRunning(true);
            waitUntil = replay.RealSeconds + 0.25;
            int samples = 0;
            while (replay.RealSeconds < waitUntil && replay.IsRunning)
            {
                int tick = replay.Session.Tick;
                yield return null;
                samples++;
                Assert.That(replay.Session.Tick - tick, Is.LessThanOrEqualTo(8), "Zero-duration inputs share the per-frame catch-up limit.");
            }
            Assert.That(replay.Error, Is.Null);
            Assert.That(replay.Session.Completed, Is.True, $"Fine and zero-duration inputs must not each wait 0.5s (observed {samples} samples; at least four frames are needed).");
            Assert.That(replay.Session.Tick, Is.EqualTo(recordedTicks));
            Assert.That(replay.IsRunning, Is.False);
        }

        [UnityTest]
        public IEnumerator Replay_InvalidDurationsAreRejectedBeforeTheyCanWait()
        {
            var scene = EditorSceneManager.OpenScene("Assets/CozyTown/Scenes/CozyTown_Dev.unity");
            AgentExperimentWindow.Install(scene, new AgentExperimentLaunchOptions
            {
                arm = AgentExperimentArm.F, runMode = AgentExperimentRunMode.Fixed, sourceRevision = "invalid-duration-test"
            });
            yield return new EnterPlayMode();
            yield return null;
            var source = Object.FindFirstObjectByType<AgentExperimentLauncher>();
            Assert.That(source.Error, Is.Null);
            source.AdvanceOneMinute();
            for (int i = 0; i < 10; i++) source.PumpResponses();
            source.Session.Complete();
            string directory = Path.GetFullPath(Path.Combine("Logs", "agent-motion", "packages", Guid.NewGuid().ToString("N")));
            source.Session.Export(directory);
            string manifestPath = Path.Combine(directory, "manifest.json");
            string original = File.ReadAllText(manifestPath);
            yield return new ExitPlayMode();

            foreach (string duration in new[] { "-1", "1e309", "-1e309" })
            {
                string changed = new Regex("\"elapsedGameSeconds\"\\s*:\\s*0\\.5")
                    .Replace(original, "\"elapsedGameSeconds\": " + duration, 1);
                Assert.That(changed, Is.Not.EqualTo(original));
                File.WriteAllText(manifestPath, changed);
                var parsed = AgentExperimentPackage.Read(directory);
                if (!parsed.IsSuccess)
                {
                    Assert.That(duration, Is.Not.EqualTo("-1"), "The finite negative duration exercises the launcher's playback validation.");
                    Assert.That(parsed.ErrorCode, Is.EqualTo("experiment.read_failed"));
                    TestContext.WriteLine($"Duration {duration} rejected while reading the package.");
                    continue;
                }
                scene = EditorSceneManager.OpenScene("Assets/CozyTown/Scenes/CozyTown_Dev.unity");
                AgentExperimentWindow.Install(scene, new AgentExperimentLaunchOptions
                {
                    runMode = AgentExperimentRunMode.Replay, replayDirectory = directory
                });
                yield return new EnterPlayMode();
                yield return null;
                var replay = Object.FindFirstObjectByType<AgentExperimentLauncher>();
                Assert.That(replay.Error, Is.Null);
                replay.SetRunning(true);
                double deadline = replay.RealSeconds + 0.2;
                while (replay.RealSeconds < deadline && replay.IsRunning) yield return null;
                Assert.That(replay.IsRunning, Is.False, duration);
                Assert.That(replay.Error, Does.Contain("duration"), duration);
                Assert.That(replay.Session.Tick, Is.Zero);
                yield return new ExitPlayMode();
            }
        }

        [UnityTearDown]
        public IEnumerator LeavePlayMode()
        {
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }
}
