#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
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
    public sealed class NpcResourcePlayModeTests
    {
        private Scene _scene;
        private DaytimeClockDriver _driver;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D _ren, _sora;

        [UnityTest]
        public IEnumerator MissingFish_DrivesActualMeetingDeliveryAndScheduleRecovery()
        {
            yield return LoadTown();
            Configure(new ResourceClient());
            var before = _sora.Position;
            _controller.TickDecisions(0); _controller.TickDecisions(0.1); _controller.TickDecisions(0.2); _controller.TickDecisions(0.3);
            Assert.That(_controller.GetMeeting(_sora.NpcId)?.State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(_sora.Position, Is.EqualTo(before));
            Assert.That(_sora.TargetLocationId, Does.StartWith("work."));
            AssertAssets(2, 0, 0, 50);
            AdvanceMinutes(45);
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Travelling));
            _controller.TickDecisions(1);
            AssertAssets(2, 0, 0, 50);
            AdvanceMinutes(10);
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_ren.Position, Is.EqualTo(new Vector2(-3f, -1.4f)));
            Assert.That(_sora.Position, Is.EqualTo(new Vector2(-3f, 0.4f)));
            for (int i = 0; i < 12; i++) _controller.TickDecisions(2 + i * 0.1);
            AssertCompleted();
            AdvanceMinutes(90);
            AssertBackAtWork();
        }

        [UnityTest]
        [Category("ExternalProvider")]
        public IEnumerator LiveProxy_TransfersOwnedResourcesThroughTheActualScene()
        {
            string endpoint = Environment.GetEnvironmentVariable("COZYTOWN_LIVE_RESOURCE_ENDPOINT");
            if (Environment.GetEnvironmentVariable("COZYTOWN_RUN_LIVE_RESOURCE") != "1" || string.IsNullOrWhiteSpace(endpoint))
                Assert.Ignore("Requires explicit resource-test opt-in and a capped local proxy.");
            Assert.That(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.IsLoopback, Is.True);
            string path = Path.GetFullPath("Logs/agent-resources-live-scene.json");
            Assert.That(File.Exists(path), Is.False, "A live report already exists; preserve evidence and account for prior calls before another run.");
            var report = new ResourceReport { startedAtUtc = DateTime.UtcNow.ToString("O"), status = "started" };
            var observations = new List<ResourceObservation>();
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            try
            {
                yield return LoadTown();
                Configure(new ProxyNpcDecisionClient(endpoint));
                double start = UnityEngine.Time.realtimeSinceStartupAsDouble;
                double previous = start;
                string last = null;
                while (UnityEngine.Time.realtimeSinceStartupAsDouble - start < 100)
                {
                    double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                    _driver.SetApplicationFocus(true); _driver.AdvanceFrame(0); _driver.AdvanceFrame(now - previous); _driver.SetApplicationFocus(false);
                    previous = now;
                    _controller.TickDecisions(now);
                    var meeting = _controller.GetMeeting(_sora.NpcId);
                    string change = (meeting?.State.ToString() ?? "None") + ":" + meeting?.DeliveryResultCode + ":" + meeting?.Transcript.Count;
                    if (change != last) { observations.Add(Observe(now - start)); last = change; }
                    if (meeting?.State == NpcMeetingState.Completed || meeting?.State == NpcMeetingState.Declined
                        || meeting?.State == NpcMeetingState.Cancelled || meeting?.State == NpcMeetingState.Expired) break;
                    yield return null;
                }
                report.clientCalls = _controller.DecisionRequestsStarted;
                report.status = _controller.GetMeeting(_sora.NpcId)?.State.ToString() ?? "NoMeeting";
                AssertCompleted();
                Assert.That(report.clientCalls, Is.LessThanOrEqualTo(10));
                AdvanceMinutes(Math.Max(0, 880 - (int)Math.Floor(_controller.GameTotalMinutes)));
                AssertBackAtWork();
                observations.Add(Observe(UnityEngine.Time.realtimeSinceStartupAsDouble - start));
                report.status = "passed";
            }
            finally
            {
                report.observations = observations.ToArray();
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
            }
        }

        private void AssertCompleted()
        {
            var meeting = _controller.GetMeeting(_sora.NpcId);
            Assert.That(meeting?.State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(meeting.DeliveryResultCode, Is.EqualTo("resource.delivered"));
            Assert.That(meeting.Transcript.Count, Is.InRange(2, 4));
            AssertAssets(1, 1, 25, 25);
            Assert.That(_controller.GetAgentState(_ren.NpcId).ActiveActivity, Is.Null);
            Assert.That(_controller.GetAgentState(_sora.NpcId).ActiveActivity, Is.Null);
        }

        private void AssertAssets(int renFish, int soraFish, int renCoins, int soraCoins)
        {
            var ren = _controller.GetMeetingResources(_ren.NpcId);
            var sora = _controller.GetMeetingResources(_sora.NpcId);
            Assert.That(ren.OwnedQuantity, Is.EqualTo(renFish));
            Assert.That(sora.OwnedQuantity, Is.EqualTo(soraFish));
            Assert.That(ren.Balance, Is.EqualTo(renCoins));
            Assert.That(sora.Balance, Is.EqualTo(soraCoins));
        }

        private void AssertBackAtWork()
        {
            Assert.That(_ren.TargetLocationId, Does.StartWith("work."));
            Assert.That(_sora.TargetLocationId, Does.StartWith("work."));
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        private void Configure(INpcDecisionClient client) => _controller.ConfigureDecisions(client,
            DefaultMvpContent.CreateConfiguration().Npcs.Where(item => item.Id == _ren.NpcId || item.Id == _sora.NpcId),
            meetingPlans: DefaultNpcResourcePlans.Create());

        private IEnumerator LoadTown()
        {
            const string path = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(path);
            var components = _scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            _driver = components.OfType<DaytimeClockDriver>().Single();
            _driver.SetApplicationFocus(false);
            _controller = components.OfType<CozyTownTownLifeController>().Single();
            _controller.enabled = false;
            _ren = components.OfType<NpcWorldResident2D>().Single(item => item.NpcId == DefaultMvpIds.Npcs.Fisher);
            _sora = components.OfType<NpcWorldResident2D>().Single(item => item.NpcId == DefaultMvpIds.Npcs.Cook);
            AdvanceMinutes(375);
        }

        private void AdvanceMinutes(int minutes)
        {
            _driver.SetApplicationFocus(true); _driver.AdvanceFrame(0);
            for (int i = 0; i < minutes; i++) _driver.AdvanceFrame(WorldTimeProgress.EffectiveSecondsPerGameMinute);
            _driver.SetApplicationFocus(false);
        }

        private ResourceObservation Observe(double elapsed)
        {
            var meeting = _controller.GetMeeting(_sora.NpcId);
            var ren = _controller.GetMeetingResources(_ren.NpcId);
            var sora = _controller.GetMeetingResources(_sora.NpcId);
            return new ResourceObservation { elapsedSeconds = elapsed, gameTotalMinutes = _controller.GameTotalMinutes,
                state = meeting?.State.ToString() ?? "None", deliveryResultCode = meeting?.DeliveryResultCode,
                renFish = ren?.OwnedQuantity ?? -1, soraFish = sora?.OwnedQuantity ?? -1,
                renCoins = ren?.Balance ?? -1, soraCoins = sora?.Balance ?? -1,
                renPosition = _ren.Position, soraPosition = _sora.Position,
                renRoute = _ren.Status.ToString(), soraRoute = _sora.Status.ToString(),
                transcript = meeting?.Transcript.Select(item => item.SpeakerId + ": " + item.Text).ToArray() ?? Array.Empty<string>() };
        }

        [Serializable] private sealed class ResourceReport
        {
            public string startedAtUtc, status;
            public long clientCalls;
            public ResourceObservation[] observations;
        }

        [Serializable] private sealed class ResourceObservation
        {
            public double elapsedSeconds, gameTotalMinutes;
            public string state, deliveryResultCode, renRoute, soraRoute;
            public int renFish, soraFish, renCoins, soraCoins;
            public Vector2 renPosition, soraPosition;
            public string[] transcript;
        }

        private sealed class ResourceClient : INpcDecisionClient
        {
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                var social = request.Social;
                return Task.FromResult(social == null ? new NpcDecisionReply(NpcDecisionKind.Wait)
                    : social.Kind == NpcSocialContextKind.Opportunity ? new NpcDecisionReply(NpcDecisionKind.Invite, planId: social.PlanId)
                    : social.Kind == NpcSocialContextKind.Invitation ? new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: social.MeetingId)
                    : social.Kind == NpcSocialContextKind.Delivery ? new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: social.MeetingId)
                    : new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: social.MeetingId, text: "The agreed fish and payment have been delivered."));
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
