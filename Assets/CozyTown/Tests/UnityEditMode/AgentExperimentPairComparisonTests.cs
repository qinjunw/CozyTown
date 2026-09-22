using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Core;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Save;
using CozyTown.Unity.Experiments;
using NUnit.Framework;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class AgentExperimentPairComparisonTests
    {
        [TestCase("available")]
        [TestCase("seller_empty")]
        [TestCase("buyer_poor")]
        [TestCase("need_satisfied")]
        public void InitialComparison_AcceptsIndependentFSWorldsForEachResourceCase(string scenario)
        {
            var free = Snapshot(NpcSpeechMode.FreeText, scenario);
            var structured = Snapshot(NpcSpeechMode.StructuredFacts, scenario);
            string before = JsonFileSaveStorage.SerializeSnapshot(structured);

            Assert.That(AgentExperimentPairComparison.CompareInitial(free, structured, 0, 0), Is.Null);

            Assert.That(JsonFileSaveStorage.SerializeSnapshot(structured), Is.EqualTo(before));
        }

        [TestCase("wallet", "/characters/")]
        [TestCase("clock", "/clock/minuteOfDay")]
        [TestCase("body", "/completeWorld/residents/")]
        [TestCase("persona", "/persona")]
        [TestCase("budget", "/maxRequestsPerMinute")]
        [TestCase("client", "/clientConfiguration")]
        [TestCase("content", "/contentConfiguration")]
        [TestCase("bodyConfiguration", "/bodyConfiguration")]
        [TestCase("seed", "/worldSeed")]
        public void InitialComparison_IdentifiesNonSpeechDifferences(string changed, string pathPart)
        {
            string difference = AgentExperimentPairComparison.CompareInitial(Snapshot(NpcSpeechMode.FreeText),
                Snapshot(NpcSpeechMode.StructuredFacts, changed: changed), 0, 0);

            Assert.That(difference, Does.Contain(pathPart));
        }

        [Test]
        public void InitialComparison_RejectsSameModeNonzeroInitialClockAndExecutionHistory()
        {
            var free = Snapshot(NpcSpeechMode.FreeText);
            var structured = Snapshot(NpcSpeechMode.StructuredFacts);
            Assert.That(AgentExperimentPairComparison.CompareInitial(free, free, 0, 0), Does.Contain("speechMode"));
            Assert.That(AgentExperimentPairComparison.CompareInitial(free, structured, 0, 0.001), Does.Contain("realSchedulerTime"));
            Assert.That(AgentExperimentPairComparison.CompareInitial(Snapshot(NpcSpeechMode.FreeText, changed: "history"),
                Snapshot(NpcSpeechMode.StructuredFacts, changed: "history"), 0, 0), Does.Contain("history"));
        }

        [Test]
        public void ContextComparison_AllowsOnlySpeechModeAndKnownIdentitiesWithoutComparingWallClock()
        {
            var free = Trace("free_text");
            var structured = Trace("structured_facts");
            foreach (var call in structured.calls) { call.dispatchRealSeconds += 0.013; call.dispatchTick += 2; }
            string before = structured.calls[0].requestJson;

            Assert.That(AgentExperimentPairComparison.FirstContextDivergence(free, structured), Is.Null);

            Assert.That(structured.calls[0].requestJson, Is.EqualTo(before));
        }

        [Test]
        public void ContextComparison_MatchesUnassignedOpportunitiesBeforeLaterMeetings()
        {
            var free = Trace("free_text", startsWithOpportunity: true);
            var structured = Trace("structured_facts", startsWithOpportunity: true);

            Assert.That(AgentExperimentPairComparison.FirstContextDivergence(free, structured), Is.Null);
        }

        [Test]
        public void ContextComparison_ReturnsTheFirstDifferentFactAndStopsAtThatPrefix()
        {
            var free = Trace("free_text");
            var structured = Trace("structured_facts");
            structured.calls[1].requestJson = structured.calls[1].requestJson.Replace("\"value\":\"1\"", "\"value\":\"0\"");
            structured.calls[1].dispatchRealSeconds = 1.25;
            structured.calls[2].requestJson = "invalid later context";

            var difference = AgentExperimentPairComparison.FirstContextDivergence(free, structured);

            Assert.That(difference.path, Is.EqualTo("/observation/facts/0/value"));
            Assert.That(difference.freeTextCallOrdinal, Is.EqualTo(1));
            Assert.That(difference.structuredFactsCallOrdinal, Is.EqualTo(1));
            Assert.That(difference.freeTextTick, Is.EqualTo(1));
            Assert.That(difference.structuredFactsGameMinutes, Is.EqualTo(722));
            Assert.That(difference.structuredFactsRealSeconds, Is.EqualTo(1.25));
        }

        [TestCase("action-version", "/schemaVersion")]
        [TestCase("expression-version", "/expression/schemaVersion")]
        [TestCase("game-time", "/gameTotalMinutes")]
        [TestCase("identity-in-fact", "/observation/facts/0/value")]
        [TestCase("world-reference", "/observation/worldRunId")]
        [TestCase("order", "/callOrdinal")]
        [TestCase("missing-call", "/calls/2")]
        public void ContextComparison_DoesNotNormalizeProtocolFactsReferencesOrCallOrder(string changed, string expectedPath)
        {
            var free = Trace("free_text");
            var structured = Trace("structured_facts");
            var call = structured.calls[0];
            if (changed == "action-version") call.requestJson = call.requestJson.Replace("\"schemaVersion\":4", "\"schemaVersion\":3");
            if (changed == "expression-version") call.requestJson = call.requestJson.Replace("\"schemaVersion\":1", "\"schemaVersion\":2");
            if (changed == "game-time") call.requestJson = call.requestJson.Replace("\"gameTotalMinutes\":721", "\"gameTotalMinutes\":722");
            if (changed == "identity-in-fact")
                call.requestJson = call.requestJson.Replace("\"value\":\"1\"", "\"value\":\"" + Guid.NewGuid().ToString("N") + "\"");
            if (changed == "world-reference")
            {
                const string prefix = "\"observation\":{\"worldRunId\":\"";
                int start = call.requestJson.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
                call.requestJson = call.requestJson.Remove(start, 32).Insert(start, Guid.NewGuid().ToString("N"));
            }
            if (changed == "order") call.callOrdinal = 1;
            if (changed == "missing-call") structured.calls.RemoveAt(2);

            var difference = AgentExperimentPairComparison.FirstContextDivergence(free, structured);

            Assert.That(difference.path, Is.EqualTo(expectedPath));
        }

        private static GameSaveSnapshot Snapshot(NpcSpeechMode mode, string scenario = "available", string changed = null)
        {
            var configuration = AgentExperimentContent.CreateConfiguration(scenario);
            var services = CozyTownCompositionRoot.Create(configuration);
            if (changed == "wallet") services.Wallet.Credit(1);
            if (changed == "clock") services.WorldTime.AdvanceMinutes(1);
            var profiles = configuration.Npcs.Select((profile, index) => changed == "persona" && index == 0
                ? new NpcDefinition(profile.Id, profile.DisplayName, "Another persona", profile.FallbackDialogue) : profile).ToArray();
            var ids = profiles.Select(profile => profile.Id).ToArray();
            var world = new NpcAgentWorld(ids.Select(id => new NpcDailySchedule(id, id + ".home", id + ".outside",
                id + ".entry", id + ".work", id + ".rest", id + ".work", 360, 480, 720, 780, 1020, 1080)));
            world.Observe(services.WorldTimeFlow.Current);
            var meetings = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("pair-social", ids[0], ids[1],
                "main-road", ids[0] + ".rest", ids[1] + ".rest", 720, 740, 745, 60) });
            using var scheduler = new NpcDecisionScheduler(world, profiles, new DeclaredClient(changed == "client" ? "other-client" : "test-client"),
                new NpcDecisionSettings(maxRequestsPerMinute: changed == "budget" ? 8 : 16), meetings, speechMode: mode);
            var decisions = scheduler.CaptureSnapshot(0);
            if (changed == "history") decisions = new NpcDecisionSchedulerSnapshot(decisions.GameTotalMinutes,
                profiles.Select(profile => new NpcDecisionResidentSnapshot(profile, 0, "agent.waited", null)), decisions.Settings,
                mode, decisions.NextResidentId, decisions.ClientConfiguration);
            var bodies = ids.Select((id, index) => {
                float x = index + (changed == "body" ? 1 : 0);
                return new NpcBodySnapshot(id, new TownRouteSnapshot(new Position2DSnapshot(x, 0), new Position2DSnapshot(0, 1),
                    id + ".rest", new[] { new Position2DSnapshot(x, 0) }, 1, 1, false), NpcActivity.Resting, false);
            }).ToArray();
            var complete = new CompleteWorldSnapshot(changed == "content" ? "other-content" : "pair-content-v1",
                changed == "bodyConfiguration" ? "other-bodies" : "pair-bodies-v1", world.CaptureSnapshot(), true, meetings.CaptureSnapshot(),
                true, decisions, bodies, new PlayerBodySnapshot(new Position2DSnapshot(-3, -3), new Position2DSnapshot(0, -1)));
            var economy = services.EconomyState.CaptureSnapshot();
            return new GameSaveSnapshot(4, services.WorldSeed.Value + (changed == "seed" ? 1 : 0), services.Time.Current,
                economy.Characters, economy.Shops, services.Farm.CaptureSnapshot(), services.Livestock.CaptureSnapshot(), 0, complete);
        }

        private static DecisionTrace Trace(string mode, bool startsWithOpportunity = false)
        {
            string world = Guid.NewGuid().ToString("N"), meeting = Guid.NewGuid().ToString("N");
            var trace = new DecisionTrace { clientConfiguration = "test-client", calls = new List<DecisionTraceCall>() };
            for (int index = 0; index < 3; index++)
            {
                bool opportunity = startsWithOpportunity && index == 0;
                string request = "{\"schemaVersion\":4,\"npcId\":\"npc.fisher_ren\",\"worldRunId\":\"" + world
                    + "\",\"decisionId\":\"" + Guid.NewGuid().ToString("N") + "\",\"gameTotalMinutes\":" + (721 + index).ToString(CultureInfo.InvariantCulture)
                    + ",\"expression\":{\"schemaVersion\":1,\"mode\":\"" + mode + "\"},\"social\":{\"meetingId\":\"" + (opportunity ? string.Empty : meeting)
                    + "\"},\"observation\":{\"worldRunId\":\"" + world + "\",\"facts\":" + (opportunity ? "[]" : "[{\"factId\":\"meeting:" + meeting
                    + ":delivered_quantity\",\"entityId\":\"meeting:" + meeting + "\",\"value\":\"1\"}]") + "}}";
                trace.calls.Add(new DecisionTraceCall { callOrdinal = index, dispatchTick = index, dispatchRealSeconds = index, requestJson = request });
            }
            return trace;
        }

        private sealed class DeclaredClient : INpcDecisionClient, INpcDecisionConfiguration
        {
            public DeclaredClient(string configuration) => SnapshotConfiguration = configuration;
            public string SnapshotConfiguration { get; }
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
                => throw new InvalidOperationException("Initial comparison fixtures must not dispatch decisions.");
        }
    }
}
