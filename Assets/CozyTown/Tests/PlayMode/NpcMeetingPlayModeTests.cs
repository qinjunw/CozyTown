#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed class NpcMeetingPlayModeTests
    {
        private Scene _scene;
        private DaytimeClockDriver _driver;
        private CozyTownTownLifeController _controller;
        private NpcWorldResident2D _ren, _sora;

        [UnityTest]
        [Category("ExternalProvider")]
        public IEnumerator LiveProxy_RealProviderDrivesTheSceneMeetingWithinTheConfiguredBudget()
        {
            string endpoint = Environment.GetEnvironmentVariable("COZYTOWN_LIVE_MEETING_ENDPOINT");
            if (Environment.GetEnvironmentVariable("COZYTOWN_RUN_LIVE_MEETING") != "1" || string.IsNullOrWhiteSpace(endpoint))
                Assert.Ignore("Requires explicit live-meeting opt-in and a capped local proxy.");
            Assert.That(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.IsLoopback, Is.True);
            string output = Path.GetFullPath("Logs/agent-meetings-live-scene.json");
            Assert.That(File.Exists(output), Is.False, "A live report already exists; do not silently spend another test budget.");
            var report = new MeetingReport { mode = "live", startedAtUtc = DateTime.UtcNow.ToString("O"), status = "started" };
            var observations = new List<MeetingObservation>();
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            try
            {
                yield return LoadTown();
                var profiles = DefaultMvpContent.CreateConfiguration().Npcs.Where(item => item.Id == _ren.NpcId || item.Id == _sora.NpcId);
                _controller.ConfigureDecisions(new ProxyNpcDecisionClient(endpoint), profiles, meetingPlans: DefaultNpcMeetingPlans.Create());
                double start = UnityEngine.Time.realtimeSinceStartupAsDouble;
                double previous = start;
                string lastObservation = null;
                bool captured = false;
                while (UnityEngine.Time.realtimeSinceStartupAsDouble - start < 100)
                {
                    double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                    _driver.SetApplicationFocus(true);
                    _driver.AdvanceFrame(0);
                    _driver.AdvanceFrame(now - previous);
                    _driver.SetApplicationFocus(false);
                    previous = now;
                    _controller.TickDecisions(now);
                    var meeting = _controller.GetMeeting(_ren.NpcId);
                    string observation = (meeting?.State.ToString() ?? "None") + ":" + (meeting?.Transcript.Count ?? 0);
                    if (observation != lastObservation)
                    {
                        observations.Add(ObserveMeeting(now - start));
                        lastObservation = observation;
                    }
                    if (!captured && meeting != null && meeting.Transcript.Count > 0)
                    {
                        yield return Capture("agent-meetings-live-conversation.png");
                        captured = true;
                    }
                    if (meeting?.State == NpcMeetingState.Completed) break;
                    if (meeting?.State == NpcMeetingState.Cancelled || meeting?.State == NpcMeetingState.Expired
                        || meeting?.State == NpcMeetingState.Declined) break;
                    yield return null;
                }
                report.clientCalls = _controller.DecisionRequestsStarted;
                var result = _controller.GetMeeting(_ren.NpcId);
                report.status = result?.State.ToString() ?? "NoMeeting";
                Assert.That(result?.State, Is.EqualTo(NpcMeetingState.Completed), "See the local scene report and proxy trace for the actual outcome.");
                Assert.That(result.Transcript.Count, Is.InRange(2, 4));
                Assert.That(report.clientCalls, Is.LessThanOrEqualTo(11));
                Assert.That(_controller.GetAgentState(_ren.NpcId).ActiveActivity, Is.Null);
                Assert.That(_controller.GetAgentState(_sora.NpcId).ActiveActivity, Is.Null);
                AdvanceMinutes(Math.Max(0, 880 - (int)Math.Floor(_controller.GameTotalMinutes)));
                Assert.That(_ren.TargetLocationId, Does.StartWith("work."));
                Assert.That(_sora.TargetLocationId, Does.StartWith("work."));
                Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Arrived));
                Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Arrived));
                observations.Add(ObserveMeeting(UnityEngine.Time.realtimeSinceStartupAsDouble - start));
                report.status = "passed";
            }
            finally
            {
                report.observations = observations.ToArray();
                File.WriteAllText(output, JsonUtility.ToJson(report, true));
            }
        }

        private MeetingObservation ObserveMeeting(double elapsed)
        {
            var meeting = _controller.GetMeeting(_ren.NpcId);
            return new MeetingObservation { elapsedSeconds = elapsed, gameTotalMinutes = _controller.GameTotalMinutes,
                state = meeting?.State.ToString() ?? "None", renPosition = _ren.Position, soraPosition = _sora.Position,
                renRoute = _ren.Status.ToString(), soraRoute = _sora.Status.ToString(),
                renTarget = _ren.TargetLocationId, soraTarget = _sora.TargetLocationId,
                transcript = meeting?.Transcript.Select(item => item.SpeakerId + ": " + item.Text).ToArray() ?? Array.Empty<string>() };
        }

        [Serializable]
        private sealed class MeetingReport
        {
            public string mode, startedAtUtc, status;
            public long clientCalls;
            public MeetingObservation[] observations;
        }

        [Serializable]
        private sealed class MeetingObservation
        {
            public double elapsedSeconds, gameTotalMinutes;
            public string state, renTarget, soraTarget, renRoute, soraRoute;
            public Vector2 renPosition, soraPosition;
            public string[] transcript;
        }

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

        [UnityTest]
        public IEnumerator BuiltInOpportunity_MeetsThroughActualRoadsAndReturnsBothResidentsToWork()
        {
            yield return LoadTown();
            var client = new MeetingClient();
            var profiles = DefaultMvpContent.CreateConfiguration().Npcs.Where(item => item.Id == _ren.NpcId || item.Id == _sora.NpcId);
            _controller.ConfigureDecisions(client, profiles, meetingPlans: DefaultNpcMeetingPlans.Create());
            Vector2 before = _sora.Position;
            _controller.TickDecisions(0);
            _controller.TickDecisions(0.1);
            _controller.TickDecisions(0.2);
            Assert.That(_controller.GetMeeting(_ren.NpcId)?.State, Is.EqualTo(NpcMeetingState.Scheduled));
            Assert.That(_sora.Position, Is.EqualTo(before), "Accepting a future invitation must not teleport the partner.");
            Assert.That(_sora.TargetLocationId, Does.StartWith("work."));
            AdvanceMinutes(45);
            Assert.That(_sora.TargetLocationId, Is.EqualTo("road.west_lane"));
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Travelling));
            _controller.TickDecisions(1);
            Assert.That(client.Requests.Count, Is.EqualTo(2), "Arrival cannot be inferred from accepting or starting a route.");
            AdvanceMinutes(1);
            Assert.That(Vector2.Distance(_sora.Position, before), Is.InRange(0.01f, 1.01f));
            AdvanceMinutes(69);
            Assert.That(_ren.Position, Is.EqualTo(new Vector2(-3f, -1.4f)));
            Assert.That(_sora.Position, Is.EqualTo(new Vector2(-3f, 0.4f)));
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Arrived));
            _controller.TickDecisions(2);
            _controller.TickDecisions(2.1);
            Assert.That(_controller.GetComponent<NpcMeetingDialogueView>().VisibleText, Does.Contain("The pond is calm today."));
            yield return Capture("agent-meetings-fixed-conversation.png");
            for (int turn = 2; turn < 6; turn++) _controller.TickDecisions(2 + turn * 0.1);
            Assert.That(_controller.GetMeeting(_ren.NpcId).State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(_controller.GetMeeting(_ren.NpcId).Transcript.Count, Is.EqualTo(4));
            Assert.That(client.Requests.Count, Is.EqualTo(6));
            Assert.That(_controller.GetAgentState(_ren.NpcId).ActiveActivity, Is.Null);
            Assert.That(_controller.GetAgentState(_sora.NpcId).ActiveActivity, Is.Null);
            Assert.That(_ren.TargetLocationId, Does.StartWith("work."));
            Assert.That(_sora.TargetLocationId, Does.StartWith("work."));
            AdvanceMinutes(30);
            Assert.That(_ren.Status, Is.EqualTo(TownRouteStatus.Arrived));
            Assert.That(_sora.Status, Is.EqualTo(TownRouteStatus.Arrived));
        }

        [UnityTest]
        public IEnumerator ReconfiguringDecisions_ReleasesThePreviousMeetingActivities()
        {
            yield return LoadTown();
            var profiles = DefaultMvpContent.CreateConfiguration().Npcs.Where(item => item.Id == _ren.NpcId || item.Id == _sora.NpcId);
            _controller.ConfigureDecisions(new MeetingClient(), profiles, meetingPlans: DefaultNpcMeetingPlans.Create());
            _controller.TickDecisions(0); _controller.TickDecisions(0.1); _controller.TickDecisions(0.2);
            Assert.That(_controller.GetMeeting(_ren.NpcId).State, Is.EqualTo(NpcMeetingState.Scheduled));
            _controller.ConfigureDecisions(new MeetingClient(), profiles);
            Assert.That(_controller.GetMeeting(_ren.NpcId), Is.Null);
            Assert.That(_controller.GetAgentState(_ren.NpcId).ActiveActivity, Is.Null);
            Assert.That(_controller.GetAgentState(_sora.NpcId).ActiveActivity, Is.Null);
        }

        private IEnumerator Capture(string filename)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
            var roots = _scene.GetRootGameObjects();
            var player = roots.Single(root => root.name == "Player");
            var body = player.GetComponent<Rigidbody2D>();
            body.linearVelocity = Vector2.zero;
            body.position = new Vector2(-3, -0.5f);
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
            var camera = roots.Single(root => root.name == "Main Camera").GetComponent<Camera>();
            float previousSize = camera.orthographicSize;
            var previousTarget = camera.targetTexture;
            var pixelCamera = camera.GetComponent<UnityEngine.Rendering.Universal.PixelPerfectCamera>();
            bool pixelEnabled = pixelCamera != null && pixelCamera.enabled;
            var canvas = _controller.GetComponent<NpcMeetingDialogueView>().GetComponentInChildren<Canvas>();
            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            var target = new RenderTexture(1440, 900, 24);
            var texture = new Texture2D(1440, 900, TextureFormat.RGBA32, false);
            bool rendered = false;
            void Observe(ScriptableRenderContext context, Camera current) { if (current == camera) rendered = true; }
            RenderPipelineManager.endCameraRendering += Observe;
            camera.orthographicSize = 4.5f;
            if (pixelCamera != null) pixelCamera.enabled = false;
            camera.targetTexture = target;
            // Batch runs have no screen backbuffer; route the existing overlay into the evidence camera.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1;
            try
            {
                for (int frame = 0; frame < 30 && !rendered; frame++) yield return null;
                Assert.That(rendered, Is.True);
                var active = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(Path.GetFullPath("Logs/" + filename), texture.EncodeToPNG());
                }
                finally { RenderTexture.active = active; }
            }
            finally
            {
                RenderPipelineManager.endCameraRendering -= Observe;
                camera.orthographicSize = previousSize;
                camera.targetTexture = previousTarget;
                if (pixelCamera != null) pixelCamera.enabled = pixelEnabled;
                canvas.renderMode = previousMode;
                canvas.worldCamera = previousCamera;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private void AdvanceMinutes(int minutes)
        {
            _driver.SetApplicationFocus(true);
            _driver.AdvanceFrame(0);
            for (int minute = 0; minute < minutes; minute++) _driver.AdvanceFrame(WorldTimeProgress.EffectiveSecondsPerGameMinute);
            _driver.SetApplicationFocus(false);
        }

        private sealed class MeetingClient : INpcDecisionClient
        {
            public readonly List<NpcDecisionRequest> Requests = new List<NpcDecisionRequest>();
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Requests.Add(request);
                var social = request.Social;
                return Task.FromResult(social == null ? new NpcDecisionReply(NpcDecisionKind.Wait)
                    : social.Kind == NpcSocialContextKind.Opportunity ? new NpcDecisionReply(NpcDecisionKind.Invite, planId: social.PlanId)
                    : social.Kind == NpcSocialContextKind.Invitation ? new NpcDecisionReply(NpcDecisionKind.AcceptInvitation, meetingId: social.MeetingId)
                    : new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: social.MeetingId, text: "The pond is calm today."));
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
