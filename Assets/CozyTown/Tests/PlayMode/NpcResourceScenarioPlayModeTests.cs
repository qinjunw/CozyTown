#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public sealed class NpcResourceScenarioPlayModeTests
    {
        private const string Ren = DefaultMvpIds.Npcs.Fisher, Sora = DefaultMvpIds.Npcs.Cook;
        private const string Fish = DefaultMvpIds.Items.Carp;
        private Scene _scene;
        private DaytimeClockDriver _driver;
        private CozyTownTownLifeController _controller;
        private CozyTownServices _services;
        private NpcWorldResident2D _ren, _sora;

        private static readonly Scenario[] Scenarios = {
            new Scenario("available", 2, 0, 50), new Scenario("seller_empty", 0, 0, 50),
            new Scenario("buyer_poor", 2, 0, 10), new Scenario("need_satisfied", 2, 1, 50)
        };

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
                .All(t => t.behavior == "host_rejected_infeasible_trade" && t.hostViolations.Count == 0), Is.True);
            var continuation = new MatrixReport();
            yield return RunMatrix(continuation, null, 3, false, null, 8);
            Assert.That(continuation.trials.Select(t => t.ordinal), Is.EqualTo(new[] { 8, 9, 10, 11, 12 }));
            Assert.That(continuation.trials.All(t => t.initialized && t.hostViolations.Count == 0 && t.recovered), Is.True);
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
            string path = Path.GetFullPath(first == 1 ? "Logs/agent-resource-scenarios-live.json"
                : "Logs/agent-resource-scenarios-live-from-" + first + ".json");
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

        private IEnumerator RunMatrix(MatrixReport report, string endpoint, int repetitions, bool reckless, string path, int firstOrdinal = 1)
        {
            bool live = endpoint != null;
            var epochs = new HashSet<string>();
            for (int repeat = 0; repeat < repetitions; repeat++)
            for (int offset = 0; offset < Scenarios.Length; offset++)
            {
                int ordinal = repeat * Scenarios.Length + offset + 1;
                if (ordinal < firstOrdinal) continue;
                // Rotate the order on each repetition; the provider budget is shared across all trials.
                var scenario = Scenarios[(repeat + offset) % Scenarios.Length];
                var trial = new Trial { scenario = scenario.Id, repetition = repeat + 1, ordinal = ordinal,
                    startedAtUtc = DateTime.UtcNow.ToString("O") };
                report.trials.Add(trial);
                Write(report, path);
                yield return LoadFreshTown(scenario);
                var client = new RecordingClient(live ? (INpcDecisionClient)new ProxyNpcDecisionClient(endpoint) : new RuleClient(reckless), trial);
                _controller.ConfigureDecisions(client, DefaultMvpContent.CreateConfiguration().Npcs.Where(n => n.Id == Ren || n.Id == Sora),
                    meetingPlans: DefaultNpcResourcePlans.Create());
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
                    yield return null;
                }
                trial.terminal = Observe(live ? UnityEngine.Time.realtimeSinceStartupAsDouble - start : simulated);
                trial.behavior = Classify(trial);
                trial.schedulerCalls = _controller.DecisionRequestsStarted;
                // Stop model dispatch before advancing to the common recovery checkpoint.
                Advance(Math.Max(0, 1000 - _controller.GameTotalMinutes) * WorldTimeProgress.EffectiveSecondsPerGameMinute);
                trial.recovery = Observe(live ? UnityEngine.Time.realtimeSinceStartupAsDouble - start : simulated);
                CheckAssets(trial, scenario, trial.recovery);
                trial.recovered = new[] { _ren, _sora }.All(n => n.TargetLocationId.StartsWith("work.")
                    && n.Status == TownRouteStatus.Arrived && _controller.GetAgentState(n.NpcId).ActiveActivity == null);
                trial.finalAssets = Assets();
                trial.renMemories = _controller.GetMeetingMemories(Ren).Count;
                trial.soraMemories = _controller.GetMeetingMemories(Sora).Count;
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
                foreach (var call in trial.calls.Where(c => c.decisionId == outcome.DecisionId.ToString())) call.hostCode = outcome.Code;
            }
        }

        private static string Classify(Trial trial)
        {
            if (trial.terminal.receipt == "resource.delivered") return trial.terminal.state == "Completed" ? "delivered_and_completed" : "delivered_without_completion";
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
                    return reply;
                }
                catch (Exception ex) { call.error = ex.GetType().Name; throw; }
                finally { call.elapsedMilliseconds = watch.Elapsed.TotalMilliseconds; Active--; }
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
            public List<Trial> trials = new List<Trial>();
        }
        [Serializable] private sealed class Trial
        {
            public string scenario, startedAtUtc, worldRunId, behavior;
            public int repetition, ordinal, renMemories, soraMemories, worldSeed;
            public long schedulerCalls;
            public bool initialized, recovered, deliveryObserved;
            public AssetRow[] initialAssets, finalAssets;
            public Observation initial, terminal, recovery;
            public List<Observation> observations = new List<Observation>();
            public List<Call> calls = new List<Call>();
            public List<string> hostViolations = new List<string>();
        }
        [Serializable] private sealed class Call
        {
            public string npcId, decisionId, phase, contextJson, operation, text, error, hostCode;
            public int step;
            public double elapsedMilliseconds;
        }
        [Serializable] private sealed class AssetRow { public string id; public int coins; public string[] items; }
        [Serializable] private sealed class Observation
        {
            public double elapsedSeconds, gameTotalMinutes;
            public string state, receipt, meetingId, renRoute, soraRoute, renTarget, soraTarget;
            public int renFish, soraFish, renCoins, soraCoins;
            public Vector2 renPosition, soraPosition;
            public string[] transcript;
        }

        [UnityTearDown] public IEnumerator UnloadTown()
        { if (_scene.IsValid() && _scene.isLoaded) yield return SceneManager.UnloadSceneAsync(_scene); }
    }
}
#endif
