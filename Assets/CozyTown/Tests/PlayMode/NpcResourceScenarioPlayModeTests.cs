#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
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
    public sealed partial class NpcResourceScenarioPlayModeTests
    {
        private const string Ren = DefaultMvpIds.Npcs.Fisher, Sora = DefaultMvpIds.Npcs.Cook;
        private const string Fish = DefaultMvpIds.Items.Carp;
        private Scene _scene;
        private DaytimeClockDriver _driver;
        private CozyTownTownLifeController _controller;
        private CozyTownServices _services;
        private NpcWorldResident2D _ren, _sora;
        private TownMap2D _map;

        private static readonly Scenario[] Scenarios = {
            new Scenario("available", 2, 0, 50), new Scenario("seller_empty", 0, 0, 50),
            new Scenario("buyer_poor", 2, 0, 10), new Scenario("need_satisfied", 2, 1, 50)
        };

        [UnityTest]
        public IEnumerator CandidateCorrection_PreservesTheRejectedAttemptAndUsesTheSameOpportunity()
        {
            var report = new MatrixReport();
            yield return RunMatrix(report, null, 1, false, null, correctFirstInvitation: true);
            Assert.That(report.trials.Count, Is.EqualTo(4));
            Assert.That(report.trials.All(t => t.initialized && t.hostViolations.Count == 0 && t.recovered), Is.True);
            Assert.That(report.trials.Single(t => t.scenario == "available").behavior, Is.EqualTo("delivered_and_completed"));
            Assert.That(report.trials.Single(t => t.scenario == "seller_empty").behavior, Is.EqualTo("declined_missing_stock"));
            foreach (var trial in report.trials.Where(t => t.scenario == "available" || t.scenario == "seller_empty"))
            {
                var rejected = trial.calls.Single(c => c.error == nameof(NpcCandidateException));
                Assert.That(rejected.candidateErrorCode, Is.EqualTo("candidate.plan_id_required"));
                Assert.That(rejected.hostCode, Is.EqualTo("candidate.plan_id_required"));
                var corrected = trial.calls.Single(c => c.decisionId == rejected.decisionId && c.step == 2);
                Assert.That(corrected.operation, Is.EqualTo("invite"));
                Assert.That(corrected.hostCode, Is.EqualTo("meeting.invited"));
                Assert.That(corrected.sentContextJson, Does.Contain("\"candidateErrorCode\":\"candidate.plan_id_required\""));
                Assert.That(rejected.decisionOutcomeCode, Is.EqualTo("meeting.invited"));
                Assert.That(trial.calls.Count(c => c.decisionId == rejected.decisionId), Is.EqualTo(2));
            }
        }

        [UnityTest]
        public IEnumerator FreshScenes_SeparateReasonableChoicesFromRejectedTrades()
        {
            var report = new MatrixReport();
            yield return RunMatrix(report, null, 2, false, null);
            Assert.That(report.trials.Count, Is.EqualTo(8));
            Assert.That(report.trials.All(t => t.initialized && t.hostViolations.Count == 0 && t.recovered), Is.True);
            Assert.That(report.trials.Select(t => t.worldRunId).Distinct().Count(), Is.EqualTo(8));
            Assert.That(report.trials.All(t => t.behavior == "delivered_and_completed" || t.behavior == "declined_missing_stock"
                || t.behavior == "waited_insufficient_funds" || t.behavior == "need_suppressed"), Is.True);
            var reckless = new MatrixReport();
            yield return RunMatrix(reckless, null, 1, true, null);
            Assert.That(reckless.trials.Count, Is.EqualTo(4));
            Assert.That(reckless.trials.Where(t => t.scenario == "seller_empty" || t.scenario == "buyer_poor")
                .All(t => t.behavior == "host_rejected_resource_commitment" && t.hostViolations.Count == 0 && t.recovered
                    && t.calls.Any(c => c.hostCode == "agent.operation_unavailable")), Is.True);
            var continuation = new MatrixReport();
            yield return RunMatrix(continuation, null, 3, false, null, 8);
            Assert.That(continuation.trials.Select(t => t.ordinal), Is.EqualTo(new[] { 8, 9, 10, 11, 12 }));
            Assert.That(continuation.trials.All(t => t.initialized && t.hostViolations.Count == 0 && t.recovered), Is.True);
        }

        [UnityTest]
        public IEnumerator FreshGroundingScenes_CoverEveryArmAndCaptureActualObservationAtTheHttpBoundary()
        {
            var report = new MatrixReport();
            yield return RunMatrix(report, null, 4, false, null, grounding: true);
            Assert.That(report.trials.Count, Is.EqualTo(16));
            Assert.That(report.trials.Select(t => t.worldRunId).Distinct().Count(), Is.EqualTo(16));
            Assert.That(report.trials.All(t => t.initialized && t.hostViolations.Count == 0 && t.recovered), Is.True);
            foreach (var scenario in Scenarios)
                Assert.That(report.trials.Where(t => t.scenario == scenario.Id).Select(t => t.arm),
                    Is.EquivalentTo(new[] { "A", "B", "C", "D" }), scenario.Id);
            foreach (var trial in report.trials)
            foreach (var call in trial.calls)
            {
                Assert.That(call.experimentObservation, Is.Not.Null);
                Assert.That(call.sentObservation, Is.Not.Null, "The mock HTTP handler must receive the injected observation.");
                Assert.That(JsonUtility.ToJson(call.sentObservation), Is.EqualTo(JsonUtility.ToJson(call.experimentObservation)));
                Assert.That(call.experimentObservation.worldRunId, Is.EqualTo(Guid.Parse(trial.worldRunId).ToString("N")));
                Assert.That(call.sentContextJson, Does.Not.Contain("\"arm\""));
                if (call.phase == "Ordinary")
                {
                    Assert.That(call.experimentObservation.partner, Is.Null);
                    Assert.That(call.sentContextJson, Does.Not.Contain("\"partner\":"));
                }
                if (call.phase == "Opportunity")
                {
                    Assert.That(call.experimentObservation.meetingBothArrived, Is.False);
                    Assert.That(call.experimentObservation.self.targetLocationId, Is.EqualTo(trial.initial.soraTarget));
                    Assert.That(call.experimentObservation.self.position, Is.EqualTo(trial.initial.soraPosition));
                    Assert.That(call.experimentObservation.partner.routeStatus, Is.EqualTo("Travelling"));
                    Assert.That(call.experimentObservation.partner.position, Is.Not.EqualTo(new Vector2(-3f, -1.4f)));
                }
                if (call.phase == "Delivery" || call.phase == "Conversation")
                {
                    Assert.That(call.experimentObservation.meetingBothArrived, Is.True);
                    Assert.That(call.experimentObservation.self.routeStatus, Is.EqualTo("Arrived"));
                    Assert.That(call.experimentObservation.partner.routeStatus, Is.EqualTo("Arrived"));
                    Assert.That(call.experimentObservation.meetingPlaceId, Is.EqualTo("pond-walk"));
                }
            }
        }

        [UnityTest]
        [Category("ExternalProvider")]
        [Timeout(1500000)]
        public IEnumerator LiveProxy_RepeatsFourFreshInitialScenarios()
        {
            string endpoint = Environment.GetEnvironmentVariable("COZYTOWN_SCENARIO_ENDPOINT");
            if (Environment.GetEnvironmentVariable("COZYTOWN_RUN_LIVE_SCENARIOS") != "1" || string.IsNullOrWhiteSpace(endpoint))
                Assert.Ignore("Requires explicit scenario-test opt-in and one proxy capped at 96 calls for the entire matrix.");
            Assert.That(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.IsLoopback, Is.True);
            string startText = Environment.GetEnvironmentVariable("COZYTOWN_SCENARIO_START_ORDINAL");
            int first = 1;
            if (!string.IsNullOrWhiteSpace(startText))
                Assert.That(int.TryParse(startText, out first) && first >= 1 && first <= 12, Is.True, "Start ordinal must be between 1 and 12.");
            string reportPath = Environment.GetEnvironmentVariable("COZYTOWN_SCENARIO_REPORT_PATH");
            string path = Path.GetFullPath(!string.IsNullOrWhiteSpace(reportPath) ? reportPath
                : first == 1 ? "Logs/agent-resource-scenarios-live.json" : "Logs/agent-resource-scenarios-live-from-" + first + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = new FileStream(path, FileMode.CreateNew)) { }
            var report = new MatrixReport { mode = "live", status = "started", firstOrdinal = first, startedAtUtc = DateTime.UtcNow.ToString("O") };
            Write(report, path);
            try
            {
                yield return RunMatrix(report, endpoint, 3, false, path, first);
                report.status = report.trials.All(t => t.initialized && t.hostViolations.Count == 0 && t.recovered)
                    ? "host_checks_passed" : "host_checks_failed";
                Assert.That(report.trials.Count, Is.EqualTo(13 - first));
                Assert.That(report.status, Is.EqualTo("host_checks_passed"));
            }
            finally { if (report.status == "started") report.status = "interrupted"; Write(report, path); }
        }

        [UnityTest]
        [Category("ExternalProvider")]
        [Timeout(1500000)]
        public IEnumerator LiveProxy_ComparedGroundingArmsAcrossFreshScenarios()
        {
            if (Environment.GetEnvironmentVariable("COZYTOWN_RUN_GROUNDING_EXPERIMENT") != "1")
                Assert.Ignore("Requires explicit grounding-experiment opt-in and four loopback endpoints with a shared 192-call scene budget.");
            Assert.That(int.TryParse(Environment.GetEnvironmentVariable("COZYTOWN_GROUNDING_BASE_PORT"), out int port)
                && port >= 1 && port <= 65532, Is.True, "The base port must leave room for four consecutive loopback ports.");
            string path = Path.GetFullPath("Logs/agent-grounding-scene.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = new FileStream(path, FileMode.CreateNew)) { }
            var report = new MatrixReport { mode = "live_grounding", providerCallCap = 192,
                startedAtUtc = DateTime.UtcNow.ToString("O") };
            Write(report, path);
            try
            {
                yield return RunMatrix(report, null, 4, false, path, grounding: true, groundingBasePort: port);
                report.status = report.trials.All(t => t.initialized && t.hostViolations.Count == 0 && t.recovered)
                    ? "host_checks_passed" : "host_checks_failed";
                Assert.That(report.trials.Count, Is.EqualTo(16));
                Assert.That(report.status, Is.EqualTo("host_checks_passed"));
            }
            finally { if (report.status == "started") report.status = "interrupted"; Write(report, path); }
        }

        private IEnumerator RunMatrix(MatrixReport report, string endpoint, int repetitions, bool reckless, string path, int firstOrdinal = 1,
            bool grounding = false, int groundingBasePort = 0, bool correctFirstInvitation = false,
            bool expression = false, int expressionBasePort = 0)
        {
            if (expression && (repetitions != 1 || firstOrdinal != 1))
                throw new ArgumentException("Expression trials require the four registered worlds without continuation.");
            bool live = endpoint != null || groundingBasePort != 0 || expressionBasePort != 0;
            var epochs = new HashSet<string>();
            for (int repeat = 0; repeat < repetitions; repeat++)
            for (int offset = 0; offset < Scenarios.Length; offset++)
            {
                int ordinal = repeat * Scenarios.Length + offset + 1;
                if (ordinal < firstOrdinal) continue;
                // Rotate the order on each repetition; the provider budget is shared across all trials.
                var scenario = expression ? Scenarios[offset / 2] : Scenarios[(repeat + offset) % Scenarios.Length];
                string expressionArm = offset == 0 || offset == 3 ? "F" : "S";
                var speechMode = expression && expressionArm == "S" ? NpcSpeechMode.StructuredFacts : NpcSpeechMode.FreeText;
                var trial = new Trial { scenario = scenario.Id, repetition = repeat + 1, ordinal = ordinal,
                    startedAtUtc = DateTime.UtcNow.ToString("O"), arm = expression ? expressionArm
                        : grounding ? ((char)('A' + offset)).ToString() : null,
                    speechMode = expression ? SpeechModeName(speechMode) : null };
                report.trials.Add(trial);
                Write(report, path);
                yield return LoadFreshTown(scenario);
                INpcDecisionClient inner;
                if (expression)
                {
                    string expressionEndpoint = "http://127.0.0.1:"
                        + (live ? expressionBasePort + (expressionArm == "S" ? 1 : 0) : 1) + "/decide";
                    inner = new ExpressionProxyClient(expressionEndpoint, trial, speechMode, live);
                }
                else if (grounding)
                {
                    string armEndpoint = "http://127.0.0.1:" + (live ? groundingBasePort + offset : 1) + "/decide";
                    inner = new GroundingClient(armEndpoint, request =>
                    {
                        var observation = CaptureGroundingObservation(request);
                        trial.calls.Last(c => c.decisionId == request.DecisionId.ToString() && c.step == request.Step)
                            .experimentObservation = observation;
                        return observation;
                    }, request => live ? (HttpMessageHandler)new HttpClientHandler() : CreateGroundingMock(request, trial, reckless));
                }
                else inner = live ? (INpcDecisionClient)new ProxyNpcDecisionClient(endpoint) : new RuleClient(reckless);
                if (correctFirstInvitation) inner = new CorrectionProbeClient(trial);
                var client = new RecordingClient(inner, trial);
                _controller.ConfigureDecisions(client, DefaultMvpContent.CreateConfiguration().Npcs.Where(n => n.Id == Ren || n.Id == Sora),
                    meetingPlans: DefaultNpcResourcePlans.Create(), speechMode: speechMode);
                trial.worldRunId = _controller.GetAgentState(Ren).WorldRunId.ToString();
                trial.worldSeed = _services.WorldSeed.Value;
                trial.initialAssets = Assets();
                trial.initial = Observe(0);
                trial.initialized = epochs.Add(trial.worldRunId) && _controller.GetAgentState(Sora).WorldRunId.ToString() == trial.worldRunId
                    && Math.Abs(_controller.GameTotalMinutes - 735) < 0.001 && _controller.DecisionRequestsStarted == 0
                    && _controller.ActiveDecisionRequests == 0 && _controller.WaitingDecisionResidents == 0
                    && new[] { Ren, Sora }.All(id => _controller.GetMeeting(id) == null
                        && _controller.GetMeetingMemories(id).Count == 0 && _controller.GetAgentState(id).ActiveActivity == null
                        && _controller.GetDecisionOutcome(id) == null)
                    && trial.initial.renFish == scenario.RenFish && trial.initial.soraFish == scenario.SoraFish
                    && trial.initial.renCoins == 0 && trial.initial.soraCoins == scenario.SoraCoins;
                Write(report, path);
                Assert.That(trial.initialized, Is.True, "Fresh initialization failed before any model request: " + scenario.Id);
                double start = UnityEngine.Time.realtimeSinceStartupAsDouble, previous = start, simulated = 0;
                string last = null;
                while ((live ? UnityEngine.Time.realtimeSinceStartupAsDouble - start : simulated) < 100)
                {
                    double now = UnityEngine.Time.realtimeSinceStartupAsDouble;
                    double delta = live ? now - previous : 0.5;
                    previous = now; simulated += delta;
                    Advance(delta);
                    _controller.TickDecisions(live ? now : simulated);
                    CaptureOutcomes(trial);
                    var observation = Observe(live ? now - start : simulated);
                    CheckAssets(trial, scenario, observation);
                    string change = observation.state + ":" + observation.receipt + ":" + observation.transcript.Length;
                    if (last != change) { trial.observations.Add(observation); last = change; Write(report, path); }
                    bool terminal = observation.state == "Completed" || observation.state == "Declined"
                        || observation.state == "Cancelled" || observation.state == "Expired";
                    if (client.Active == 0 && (terminal || (observation.state == "None" && _controller.GameTotalMinutes >= 796))) break;
                    if (expression && client.Active == 0 && trial.calls.Count >= 12) break;
                    yield return null;
                }
                trial.terminal = Observe(live ? UnityEngine.Time.realtimeSinceStartupAsDouble - start : simulated);
                trial.behavior = Classify(trial);
                trial.schedulerCalls = _controller.DecisionRequestsStarted;
                if (expression)
                {
                    trial.stopReason = trial.terminal.state == "Completed" || trial.terminal.state == "Declined"
                        || trial.terminal.state == "Cancelled" || trial.terminal.state == "Expired"
                        ? "meeting_" + trial.terminal.state.ToLowerInvariant()
                        : trial.calls.Count >= 12 ? "trial_call_cap"
                        : trial.terminal.state == "None" && _controller.GameTotalMinutes >= 796
                            ? "opportunity_closed" : "trial_deadline";
                    trial.activelyEnded = trial.calls.Any(call => call.operation == "end_conversation"
                        && call.hostCode == "meeting.end_conversation");
                    trial.terminalActivitiesReleased = new[] { Ren, Sora }
                        .All(id => _controller.GetAgentState(id).ActiveActivity == null);
                }
                // Stop model dispatch before advancing to the common recovery checkpoint.
                Advance(Math.Max(0, 1000 - _controller.GameTotalMinutes) * WorldTimeProgress.EffectiveSecondsPerGameMinute);
                trial.recovery = Observe(live ? UnityEngine.Time.realtimeSinceStartupAsDouble - start : simulated);
                CheckAssets(trial, scenario, trial.recovery);
                trial.recovered = new[] { _ren, _sora }.All(n => n.TargetLocationId.StartsWith("work.")
                    && n.Status == TownRouteStatus.Arrived && _controller.GetAgentState(n.NpcId).ActiveActivity == null);
                trial.finalAssets = Assets();
                trial.renMemories = _controller.GetMeetingMemories(Ren).Count;
                trial.soraMemories = _controller.GetMeetingMemories(Sora).Count;
                if (expression)
                {
                    trial.deadlineReleased = trial.terminal.state == "Expired" || trial.recovery.state == "Expired";
                    trial.renMemoryRecords = CaptureMemories(Ren);
                    trial.soraMemoryRecords = CaptureMemories(Sora);
                }
                Write(report, path);
                yield return UnloadTown();
                // A cancelled HTTP request can outlive its client. Allow the proxy's seven-second provider timeout to finish.
                if (live && trial.calls.Any(c => c.error != null || c.hostCode == "agent.request_timeout"))
                {
                    double drainUntil = UnityEngine.Time.realtimeSinceStartupAsDouble + 8;
                    while (UnityEngine.Time.realtimeSinceStartupAsDouble < drainUntil) yield return null;
                }
                Assert.That(client.Active, Is.Zero, "A prior trial still has a client request in flight.");
                Write(report, path);
            }
        }

        private IEnumerator LoadFreshTown(Scenario scenario)
        {
            const string scenePath = "Assets/CozyTown/Scenes/CozyTown_Dev.unity";
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            _scene = SceneManager.GetSceneByPath(scenePath);
            var components = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).ToArray();
            _driver = components.OfType<DaytimeClockDriver>().Single();
            _driver.SetApplicationFocus(false);
            _controller = components.OfType<CozyTownTownLifeController>().Single();
            _controller.enabled = false;
            _ren = components.OfType<NpcWorldResident2D>().Single(n => n.NpcId == Ren);
            _sora = components.OfType<NpcWorldResident2D>().Single(n => n.NpcId == Sora);
            _map = components.OfType<TownMap2D>().Single();
            var config = DefaultMvpContent.CreateConfiguration();
            for (int i = 0; i < config.InitialNpcEconomy.Length; i++)
            {
                var old = config.InitialNpcEconomy[i];
                if (old.CharacterId != Ren && old.CharacterId != Sora) continue;
                var items = old.Backpack.Items.Where(item => item.ItemId != Fish).ToList();
                int quantity = old.CharacterId == Ren ? scenario.RenFish : scenario.SoraFish;
                if (quantity > 0) items.Add(new ItemStack(Fish, quantity));
                config.InitialNpcEconomy[i] = new CharacterEconomySnapshot(old.CharacterId, new InventorySnapshot(items.ToArray()),
                    new WalletSnapshot(old.CharacterId == Ren ? 0 : scenario.SoraCoins));
            }
            _services = CozyTownCompositionRoot.Create(config);
            _driver.Bind(_services.DaytimeClock);
            _controller.Bind(_services.WorldTimeFlow, _services.ResourceTrading);
            Advance(375 * WorldTimeProgress.EffectiveSecondsPerGameMinute);
        }

        private ExperimentObservation CaptureGroundingObservation(NpcDecisionRequest request)
        {
            var self = request.NpcId == Ren ? _ren : _sora;
            var observation = new ExperimentObservation { worldRunId = request.Self.WorldRunId.ToString("N"),
                gameTotalMinutes = request.GameTotalMinutes, self = Resident(self) };
            if (request.Social?.Resources == null) return observation;
            var partner = request.Social.PartnerId == Ren ? _ren : _sora;
            var plan = DefaultNpcResourcePlans.Create().Single(p => p.Id == request.Social.PlanId);
            observation.partner = Resident(partner);
            observation.meetingPlaceId = plan.PlaceId;
            observation.meetingBothArrived = AtMeetingLocation(_sora, plan.InitiatorLocationId)
                && AtMeetingLocation(_ren, plan.PartnerLocationId);
            return observation;
        }

        private bool AtMeetingLocation(NpcWorldResident2D resident, string locationId)
            => resident.Status == TownRouteStatus.Arrived && resident.TargetLocationId == locationId
                && _map.TryGetLocation(locationId, out var position) && Vector2.Distance(resident.Position, position) < 0.01f;

        private static ResidentObservation Resident(NpcWorldResident2D resident)
            => new ResidentObservation { npcId = resident.NpcId, position = resident.Position,
                routeStatus = resident.Status.ToString(), targetLocationId = resident.TargetLocationId };

        private static HttpMessageHandler CreateGroundingMock(NpcDecisionRequest request, Trial trial, bool reckless)
        {
            var reply = new RuleClient(reckless).DecideAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            string response = JsonUtility.ToJson(new MockCandidate { schemaVersion = request.Social?.Resources == null ? 1 : 4,
                operation = reply.Operation, planId = reply.PlanId, meetingId = reply.MeetingId.ToString("N"), text = reply.Text });
            var call = trial.calls.Last(c => c.decisionId == request.DecisionId.ToString() && c.step == request.Step);
            return new GroundingMockHandler(response, json =>
            {
                call.sentContextJson = json;
                call.sentObservation = JsonUtility.FromJson<ObservedRequest>(json).experimentObservation;
            });
        }

        private void Advance(double seconds)
        {
            _driver.SetApplicationFocus(true); _driver.AdvanceFrame(0);
            // Preserve the same route stepping as the existing scene test.
            while (seconds > 0) { double step = Math.Min(seconds, 0.5); _driver.AdvanceFrame(step); seconds -= step; }
            _driver.SetApplicationFocus(false);
        }

        private Observation Observe(double elapsed)
        {
            var meeting = _controller.GetMeeting(Sora);
            _services.EconomyState.TryGetCharacter(Ren, out var ren);
            _services.EconomyState.TryGetCharacter(Sora, out var sora);
            return new Observation { elapsedSeconds = elapsed, gameTotalMinutes = _controller.GameTotalMinutes,
                state = meeting?.State.ToString() ?? "None", receipt = meeting?.DeliveryResultCode,
                meetingId = meeting?.Id.ToString(), renFish = Count(ren, Fish), soraFish = Count(sora, Fish),
                renCoins = ren.Wallet.Balance, soraCoins = sora.Wallet.Balance,
                renPosition = _ren.Position, soraPosition = _sora.Position, renRoute = _ren.Status.ToString(), soraRoute = _sora.Status.ToString(),
                renTarget = _ren.TargetLocationId, soraTarget = _sora.TargetLocationId,
                visibleText = _controller.GetComponent<NpcMeetingDialogueView>()?.VisibleText,
                transcript = meeting?.Transcript.Select(l => l.SpeakerId + ": " + l.Text).ToArray() ?? Array.Empty<string>() };
        }

        private void CheckAssets(Trial trial, Scenario scenario, Observation o)
        {
            bool delivered = o.receipt == "resource.delivered";
            int transfer = delivered ? 1 : 0;
            if (o.renFish != scenario.RenFish - transfer || o.soraFish != scenario.SoraFish + transfer
                || o.renCoins != transfer * 25 || o.soraCoins != scenario.SoraCoins - transfer * 25
                || o.renFish < 0 || o.soraCoins < 0) Violation(trial, "asset_delta_or_receipt");
            if (delivered && (scenario.RenFish == 0 || scenario.SoraCoins < 25 || scenario.SoraFish > 0)) Violation(trial, "infeasible_delivery");
            if (trial.initialAssets.Where(a => a.id != Ren && a.id != Sora).Select(JsonUtility.ToJson)
                .SequenceEqual(Assets().Where(a => a.id != Ren && a.id != Sora).Select(JsonUtility.ToJson)) == false)
                Violation(trial, "other_owner_assets_changed");
            foreach (string id in new[] { Ren, Sora })
            {
                var initial = trial.initialAssets.Single(a => a.id == id);
                var current = Assets().Single(a => a.id == id);
                if (!initial.items.Where(i => !i.StartsWith(Fish + ":")).SequenceEqual(current.items.Where(i => !i.StartsWith(Fish + ":"))))
                    Violation(trial, "other_items_changed");
            }
            if (delivered && !trial.deliveryObserved)
            {
                trial.deliveryObserved = true;
                if (o.renRoute != "Arrived" || o.soraRoute != "Arrived" || o.renTarget != "rest.fisher_ren" || o.soraTarget != "road.west_lane")
                    Violation(trial, "delivery_before_physical_arrival");
            }
        }

        private static void Violation(Trial trial, string code) { if (!trial.hostViolations.Contains(code)) trial.hostViolations.Add(code); }
        private static int Count(CharacterEconomySnapshot owner, string itemId) => owner.Backpack.Items.Where(i => i.ItemId == itemId).Sum(i => i.Quantity);

        private AssetRow[] Assets()
        {
            var snapshot = _services.EconomyState.CaptureSnapshot();
            return snapshot.Characters.Select(c => new AssetRow { id = c.CharacterId, coins = c.Wallet.Balance,
                items = c.Backpack.Items.Select(i => i.ItemId + ":" + i.Quantity).OrderBy(i => i).ToArray() })
                .Concat(snapshot.Shops.Select(s => new AssetRow { id = s.ShopId, coins = s.Wallet.Balance,
                    items = s.Stock.Items.Select(i => i.ItemId + ":" + i.Quantity).OrderBy(i => i).ToArray() })).OrderBy(a => a.id).ToArray();
        }

        private void CaptureOutcomes(Trial trial)
        {
            foreach (string id in new[] { Ren, Sora })
            {
                var outcome = _controller.GetDecisionOutcome(id);
                if (outcome == null) continue;
                foreach (var call in trial.calls.Where(c => c.decisionId == outcome.DecisionId.ToString()))
                {
                    call.decisionOutcomeCode = outcome.Code;
                    call.hostCode = string.IsNullOrEmpty(call.candidateErrorCode) ? outcome.Code : call.candidateErrorCode;
                    call.executionObservationJson = call.step == outcome.Context.Step
                        ? SerializeExecutionObservation(outcome.ExecutionObservation) : "null";
                }
            }
        }

        private static string Classify(Trial trial)
        {
            if (trial.terminal.receipt == "resource.delivered") return trial.terminal.state == "Completed" ? "delivered_and_completed" : "delivered_without_completion";
            if (trial.calls.Any(c => (c.operation == "invite" || c.operation == "accept_invite")
                && (c.hostCode == "agent.operation_unavailable" || c.hostCode == "wallet.insufficient_funds"
                    || c.hostCode == "inventory.insufficient_quantity"))) return "host_rejected_resource_commitment";
            if (trial.calls.Any(c => c.operation == "deliver" && (c.hostCode == "wallet.insufficient_funds"
                || c.hostCode == "inventory.insufficient_quantity"))) return "host_rejected_infeasible_trade";
            if (trial.scenario == "need_satisfied" && trial.calls.All(c => c.phase == "Ordinary")) return "need_suppressed";
            if (trial.scenario == "seller_empty" && trial.calls.Any(c => c.npcId == Ren && c.operation == "decline_invite" && c.hostCode == "meeting.decline_invite")) return "declined_missing_stock";
            if (trial.scenario == "buyer_poor" && trial.calls.Any(c => c.npcId == Sora && c.phase == "Opportunity" && c.operation == "wait" && c.hostCode == "agent.decision_wait")) return "waited_insufficient_funds";
            if (trial.calls.Any(c => c.operation == "cancel_exchange")) return "cancelled_after_invitation";
            if (trial.calls.Any(c => c.error != null || c.hostCode == "agent.request_timeout")) return "provider_or_client_failure";
            return "no_expected_resolution";
        }

        private static void Write(MatrixReport report, string path)
        { if (path != null) File.WriteAllText(path, JsonUtility.ToJson(report, true)); }

        private sealed class RecordingClient : INpcDecisionClient
        {
            private readonly INpcDecisionClient _inner;
            private readonly Trial _trial;
            public int Active { get; private set; }
            internal RecordingClient(INpcDecisionClient inner, Trial trial) { _inner = inner; _trial = trial; }
            public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                var call = new Call { npcId = request.NpcId, decisionId = request.DecisionId.ToString(), step = request.Step, phase = request.Social?.Kind.ToString() ?? "Ordinary",
                    contextJson = new ProxyNpcDecisionJsonCodec().SerializeRequest(request) };
                _trial.calls.Add(call);
                if (_trial.calls.Count > 12) { call.error = "trial_call_cap"; throw new InvalidOperationException("Trial request cap reached."); }
                Active++;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var reply = await _inner.DecideAsync(request, token);
                    call.operation = reply.Operation; call.text = reply.Text;
                    if (reply.SpeechFrame != null) call.speechFrame = new SpeechFrameRecord {
                        speechIntent = reply.SpeechFrame.Intent, factId = reply.SpeechFrame.FactId, tone = reply.SpeechFrame.Tone };
                    return reply;
                }
                catch (Exception ex)
                {
                    call.error = ex.GetType().Name;
                    if (ex is NpcCandidateException candidate) call.candidateErrorCode = call.hostCode = candidate.Code;
                    throw;
                }
                finally { call.elapsedMilliseconds = watch.Elapsed.TotalMilliseconds; Active--; }
            }
        }

        private sealed class CorrectionProbeClient : INpcDecisionClient
        {
            private readonly Trial _trial;
            internal CorrectionProbeClient(Trial trial) => _trial = trial;
            public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                bool reject = request.Step == 1 && request.Social?.Kind == NpcSocialContextKind.Opportunity
                    && request.AllowedOperations.Contains("invite");
                var reply = await new RuleClient(false).DecideAsync(request, token);
                string json = reject ? "{\"error\":\"provider.candidate_invalid\",\"candidateErrorCode\":\"candidate.plan_id_required\"}"
                    : JsonUtility.ToJson(new MockCandidate { schemaVersion = request.Social == null ? 1 : 4,
                        operation = reply.Operation, planId = reply.PlanId, meetingId = reply.MeetingId.ToString("N"), text = reply.Text });
                using var http = new HttpClient(new CorrectionProbeHandler(reject, json, sent =>
                    _trial.calls.Last(c => c.decisionId == request.DecisionId.ToString() && c.step == request.Step).sentContextJson = sent));
                return await new ProxyNpcDecisionClient("http://127.0.0.1:1/decide", http).DecideAsync(request, token);
            }
        }

        private sealed class CorrectionProbeHandler : HttpMessageHandler
        {
            private readonly bool _reject;
            private readonly string _json;
            private readonly Action<string> _capture;
            internal CorrectionProbeHandler(bool reject, string json, Action<string> capture)
            { _reject = reject; _json = json; _capture = capture; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                _capture(await request.Content.ReadAsStringAsync());
                return new HttpResponseMessage(_reject ? (HttpStatusCode)422 : HttpStatusCode.OK)
                    { Content = new StringContent(_json) };
            }
        }

        private sealed class GroundingClient : INpcDecisionClient
        {
            private readonly string _endpoint;
            private readonly Func<NpcDecisionRequest, ExperimentObservation> _capture;
            private readonly Func<NpcDecisionRequest, HttpMessageHandler> _transport;
            internal GroundingClient(string endpoint, Func<NpcDecisionRequest, ExperimentObservation> capture,
                Func<NpcDecisionRequest, HttpMessageHandler> transport)
            { _endpoint = endpoint; _capture = capture; _transport = transport; }

            public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                // Capture Unity state before the HTTP client's asynchronous continuation can leave the main thread.
                var observation = _capture(request);
                string json = JsonUtility.ToJson(new ObservationPayload { worldRunId = observation.worldRunId,
                    gameTotalMinutes = observation.gameTotalMinutes, self = observation.self,
                    meetingPlaceId = observation.meetingPlaceId, meetingBothArrived = observation.meetingBothArrived });
                if (observation.partner != null)
                    json = json.Substring(0, json.Length - 1) + ",\"partner\":" + JsonUtility.ToJson(observation.partner) + "}";
                using var http = new HttpClient(new GroundingObservationHandler(json, _transport(request)));
                return await new ProxyNpcDecisionClient(_endpoint, http).DecideAsync(request, token);
            }
        }

        private sealed class GroundingObservationHandler : DelegatingHandler
        {
            private readonly string _observation;
            internal GroundingObservationHandler(string observation, HttpMessageHandler inner)
            { _observation = observation; InnerHandler = inner; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                string json = (await request.Content.ReadAsStringAsync()).TrimEnd();
                if (!json.EndsWith("}", StringComparison.Ordinal)) throw new FormatException("Expected a decision request JSON object.");
                var original = request.Content;
                request.Content = new StringContent(json.Substring(0, json.Length - 1)
                    + ",\"experimentObservation\":" + _observation + "}", Encoding.UTF8, "application/json");
                original.Dispose();
                return await base.SendAsync(request, token);
            }
        }

        private sealed class GroundingMockHandler : HttpMessageHandler
        {
            private readonly string _response;
            private readonly Action<string> _capture;
            internal GroundingMockHandler(string response, Action<string> capture) { _response = response; _capture = capture; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                _capture(await request.Content.ReadAsStringAsync());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_response, Encoding.UTF8, "application/json") };
            }
        }

        private sealed class RuleClient : INpcDecisionClient
        {
            private readonly bool _reckless;
            internal RuleClient(bool reckless) { _reckless = reckless; }
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                var s = request.Social;
                return Task.FromResult(s == null ? new NpcDecisionReply(NpcDecisionKind.Wait)
                    : s.Kind == NpcSocialContextKind.Opportunity ? new NpcDecisionReply(!_reckless && s.Resources.Balance < 25 ? NpcDecisionKind.Wait : NpcDecisionKind.Invite, planId: s.PlanId)
                    : s.Kind == NpcSocialContextKind.Invitation ? new NpcDecisionReply(!_reckless && s.Resources.OwnedQuantity == 0 ? NpcDecisionKind.DeclineInvitation : NpcDecisionKind.AcceptInvitation, meetingId: s.MeetingId)
                    : s.Kind == NpcSocialContextKind.Delivery ? new NpcDecisionReply(NpcDecisionKind.Deliver, meetingId: s.MeetingId)
                    : new NpcDecisionReply(s.Transcript.Count >= 2 ? NpcDecisionKind.EndConversation : NpcDecisionKind.Speak, meetingId: s.MeetingId, text: "Thank you for the agreed trade."));
            }
        }

        private sealed class Scenario
        {
            internal readonly string Id;
            internal readonly int RenFish, SoraFish, SoraCoins;
            internal Scenario(string id, int renFish, int soraFish, int soraCoins) { Id = id; RenFish = renFish; SoraFish = soraFish; SoraCoins = soraCoins; }
        }
        [Serializable] private sealed class MatrixReport
        {
            public string mode = "fixed", status = "started", startedAtUtc;
            public int providerCallCap = 96, perTrialCallCap = 12, trialDeadlineSeconds = 100, initialGameMinute = 735, recoveryGameMinute = 1000;
            public int firstOrdinal = 1;
            public string[] registeredTrialOrder;
            public List<Trial> trials = new List<Trial>();
        }
        [Serializable] private sealed class Trial
        {
            public string scenario, startedAtUtc, worldRunId, behavior, arm, speechMode, stopReason;
            public int repetition, ordinal, renMemories, soraMemories, worldSeed;
            public long schedulerCalls;
            public bool initialized, recovered, deliveryObserved;
            public bool activelyEnded, deadlineReleased, terminalActivitiesReleased;
            public MemoryRecord[] renMemoryRecords, soraMemoryRecords;
            public AssetRow[] initialAssets, finalAssets;
            public Observation initial, terminal, recovery;
            public List<Observation> observations = new List<Observation>();
            public List<Call> calls = new List<Call>();
            public List<string> hostViolations = new List<string>();
        }
        [Serializable] private sealed class Call
        {
            public string npcId, decisionId, phase, contextJson, operation, text, error, hostCode, sentContextJson;
            public string candidateErrorCode, decisionOutcomeCode, rawReplyJson;
            public string executionObservationJson = "null";
            public SpeechFrameRecord speechFrame;
            public int responseStatusCode;
            public bool rawReplyTruncated;
            public int step;
            public double elapsedMilliseconds;
            public ExperimentObservation experimentObservation, sentObservation;
        }
        [Serializable] private sealed class ExperimentObservation
        {
            public string worldRunId, meetingPlaceId;
            public double gameTotalMinutes;
            public ResidentObservation self, partner;
            public bool meetingBothArrived;
        }
        [Serializable] private sealed class ResidentObservation
        {
            public string npcId, routeStatus, targetLocationId;
            public Vector2 position;
        }
        [Serializable] private sealed class ObservationPayload
        {
            public string worldRunId, meetingPlaceId;
            public double gameTotalMinutes;
            public ResidentObservation self;
            public bool meetingBothArrived;
        }
        [Serializable] private sealed class ObservedRequest { public ExperimentObservation experimentObservation; }
        [Serializable] private sealed class MockCandidate
        {
            public int schemaVersion;
            public string operation, planId, meetingId, text;
        }
        [Serializable] private sealed class AssetRow { public string id; public int coins; public string[] items; }
        [Serializable] private sealed class Observation
        {
            public double elapsedSeconds, gameTotalMinutes;
            public string state, receipt, meetingId, renRoute, soraRoute, renTarget, soraTarget, visibleText;
            public int renFish, soraFish, renCoins, soraCoins;
            public Vector2 renPosition, soraPosition;
            public string[] transcript;
        }

        [UnityTearDown] public IEnumerator UnloadTown()
        { if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene); }
    }
}
#endif
