#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Application;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.Economy;
using CozyTown.Runtime.Inventory;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEngine;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class NpcExpressionExperimentTests
    {
        private const string Ren = DefaultMvpIds.Npcs.Fisher, Sora = DefaultMvpIds.Npcs.Cook;
        private const string Fish = DefaultMvpIds.Items.Carp;
        private static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [Test]
        public void FixedCases_ExposeTheRegisteredEvidenceWithoutPromotingStatementsToCurrentAssets()
        {
            var cases = ReadCases();
            Assert.That(cases.Length, Is.EqualTo(8));
            foreach (var item in cases)
            {
                var request = CreateRequest(item);
                Assert.That(request.AllowedOperations, Does.Contain("say"), item.id);
                Assert.That(request.Observation.ListenerId, Is.EqualTo(request.Social.PartnerId));
                Assert.That(request.Observation.Facts.All(f => f.ObserverId == request.NpcId), Is.True);
                var codec = new ProxyNpcDecisionJsonCodec();
                string free = codec.SerializeRequest(request);
                string structured = ForArm(free, "S");
                Assert.That(ForArm(structured, "F"), Is.EqualTo(free));
                if (item.id == "position_destination")
                {
                    Assert.That(request.Observation.RegionId, Is.EqualTo("west"));
                    Assert.That(request.Self.Target.TargetLocationId, Does.EndWith(".rest"));
                    Assert.That(request.Observation.NearbyEntityIds, Does.Not.Contain("pond.water"));
                }
                if (item.id == "past_statement_current_stock")
                {
                    Assert.That(request.Observation.Facts.Single(f => f.Predicate == "owned_quantity").Value, Is.EqualTo("0"));
                    Assert.That(request.Observation.Facts.Any(f => f.Knowledge == "statement" && f.Value == "I have two carp."), Is.True);
                }
                if (item.id == "successful_receipt")
                    Assert.That(request.Observation.Facts.Single(f => f.Predicate == "delivery_result").Knowledge, Is.EqualTo("receipt"));
                if (item.id == "truncated_local_absence") Assert.That(request.Observation.NearbyComplete, Is.False);
                if (item.id == "partner_private_assets")
                    Assert.That(request.Observation.Facts.Any(f => f.EntityId == request.Social.PartnerId && f.Predicate == "owned_quantity"), Is.False);
                TestContext.WriteLine("EXPRESSION_FIXED_CONTEXT=" + free);
            }
        }

        [Test, Timeout(600000)]
        public async Task LiveProxy_CompareEightFactCasesWithOneCallPerSample()
        {
            if (Environment.GetEnvironmentVariable("COZYTOWN_RUN_EXPRESSION_FIXED") != "1")
                Assert.Ignore("Set COZYTOWN_RUN_EXPRESSION_FIXED=1 to run the registered provider comparison.");
            int port = int.Parse(Environment.GetEnvironmentVariable("COZYTOWN_EXPRESSION_BASE_PORT"));
            string path = Environment.GetEnvironmentVariable("COZYTOWN_EXPRESSION_FIXED_REPORT_PATH");
            if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(Root, "Logs/agent-expression-fixed.json");
            var report = new Report { stage = "fixed", state = "running" };
            using (File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { }
            Save(path, report);
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            var codec = new ProxyNpcDecisionJsonCodec();
            var cases = ReadCases();
            var requests = cases.Select(item => {
                var request = CreateRequest(item, out var setup);
                report.setups.Add(setup);
                return request;
            }).ToArray();
            Save(path, report);
            try
            {
                for (int repeat = 0; repeat < 3; repeat++)
                    for (int i = 0; i < cases.Length; i++)
                        foreach (string arm in (repeat + i) % 2 == 0 ? new[] { "F", "S" } : new[] { "S", "F" })
                        {
                            var request = requests[i];
                            var sample = new Sample { ordinal = report.samples.Count + 1, caseId = cases[i].id,
                                repeat = repeat + 1, arm = arm, expected = cases[i].expected,
                                contextJson = ForArm(codec.SerializeRequest(request), arm), status = "request_started" };
                            report.samples.Add(sample);
                            Save(path, report);
                            var started = System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                using var response = await http.PostAsync("http://127.0.0.1:" + (port + (arm == "F" ? 0 : 1)) + "/decide",
                                    new StringContent(sample.contextJson, Encoding.UTF8, "application/json"));
                                sample.httpStatus = (int)response.StatusCode;
                                sample.rawReply = await response.Content.ReadAsStringAsync();
                                if (!response.IsSuccessStatusCode) sample.status = "proxy_rejected";
                                else Evaluate(sample, request, codec);
                            }
                            catch (Exception exception)
                            {
                                sample.status = "transport_or_host_failure";
                                sample.error = exception.GetType().Name;
                            }
                            sample.elapsedMilliseconds = started.ElapsedMilliseconds;
                            Save(path, report);
                        }
                report.state = "completed";
            }
            finally { Save(path, report); }
            Assert.That(report.samples.Count, Is.EqualTo(48));
        }

        private static void Evaluate(Sample sample, NpcDecisionRequest request, ProxyNpcDecisionJsonCodec codec)
        {
            try
            {
                // Both arms use the original identity; only the speech contract varies in this fixed probe.
                var reply = codec.ParseResponse(sample.rawReply, sample.arm == "F" ? request : null);
                var header = JsonUtility.FromJson<CandidateHeader>(sample.rawReply);
                int expectedSchema = request.Social.Resources == null ? 2 : 4;
                if (header.schemaVersion != expectedSchema || !request.AllowedOperations.Contains(reply.Operation)
                    || reply.MeetingId != request.Social.MeetingId)
                { sample.status = "host_rejected"; sample.error = "candidate.request_binding_invalid"; return; }
                sample.operation = reply.Operation;
                if (reply.Kind != NpcDecisionKind.Speak) { sample.status = "no_speech"; return; }
                if (sample.arm == "F") sample.displayedText = reply.Text;
                else
                {
                    sample.speechIntent = reply.SpeechFrame?.Intent;
                    sample.factId = reply.SpeechFrame?.FactId;
                    sample.tone = reply.SpeechFrame?.Tone;
                    if (!NpcFactSpeech.TryRender(request.Observation, reply.SpeechFrame, out sample.displayedText, out sample.error))
                    { sample.status = "host_rejected"; return; }
                }
                sample.status = "displayed";
            }
            catch (NpcCandidateException exception) { sample.status = "host_rejected"; sample.error = exception.Code; }
            catch (FormatException) { sample.status = "host_rejected"; sample.error = "candidate.invalid_json"; }
        }

        private static string ForArm(string json, string arm)
            => json.Replace(arm == "S" ? "\"mode\":\"free_text\"" : "\"mode\":\"structured_facts\"",
                arm == "S" ? "\"mode\":\"structured_facts\"" : "\"mode\":\"free_text\"");

        private static Case[] ReadCases()
            => JsonUtility.FromJson<Corpus>(File.ReadAllText(Path.Combine(Root, "Tools/agent_proxy/expression_cases.json"))).cases;

        private static void Save(string path, Report report) => File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));

        private static NpcDecisionRequest CreateRequest(Case item)
            => CreateRequest(item, out _);

        private static NpcDecisionRequest CreateRequest(Case item, out FixtureSetup setup)
        {
            string actor = item.actor == "sora" ? Sora : Ren, partner = actor == Sora ? Ren : Sora;
            var world = new NpcAgentWorld(new[] { Ren, Sora }.Select(id => new NpcDailySchedule(id,
                id + ".home", id + ".outside", id + ".entry", id + ".work", id + ".rest", id + ".afternoon",
                360, 480, 720, 810, 1020, 1080)));
            world.Observe(Time(720));
            bool stockChanged = item.id == "past_statement_current_stock";
            var store = new InMemoryEconomyStateStore(new[] { Character(Ren, 2, 0), Character(Sora, 0, 50) },
                Array.Empty<ShopEconomySnapshot>());
            var terms = new CharacterTradeTerms(Ren, Sora, Fish, 1, 25);
            var trading = new CharacterResourceTrading(store, new[] { new ItemDefinition(Fish, "Carp", ItemCategory.Fish, 99) }, 8);
            var board = new NpcMeetingBoard(world, new[] { new NpcMeetingPlan("expression-probe", Sora, Ren, "pond",
                Sora + ".rest", Ren + ".rest", 720, 750, 790, maxTurns: 8, resourceTerms: terms) },
                (npc, location) => NpcMeetingPresence.Arrived, trading);
            setup = new FixtureSetup { caseId = item.id, worldRunId = world.GetState(actor).WorldRunId.ToString("N"),
                initialAssets = CaptureAssets(trading, terms), initialMemories = board.GetMemories(actor).Count + board.GetMemories(partner).Count,
                initialMeetingAbsent = board.GetCurrent(actor) == null && board.GetCurrent(partner) == null,
                fixtureMutation = stockChanged ? "At 750 set Sora to 2 carp; at 752 set Sora to 0 carp." : "none",
                presenceInput = "arrived", capturedAtGameMinute = 752 };
            Assert.That(setup.initialAssets, Is.EqualTo(new[] { 2, 0, 0, 50 }));
            Assert.That(setup.initialMemories, Is.Zero);
            Assert.That(setup.initialMeetingAbsent, Is.True);
            var meeting = board.Invite(world.GetState(Sora), "expression-probe").Value;
            Assert.That(board.Respond(world.GetState(Ren), meeting.Id, true).IsSuccess, Is.True);
            world.Observe(Time(750));
            board.Observe();
            Assert.That(board.Deliver(world.GetState(Sora), meeting.Id).IsSuccess, Is.True);
            if (stockChanged) store.CommitCharacter(Character(Sora, 2, 25));
            if (actor == Sora)
                Assert.That(board.Speak(world.GetState(Sora), meeting.Id, stockChanged ? "I have two carp." : "Let us talk.").IsSuccess, Is.True);
            if (item.id == "statement_not_experience")
            {
                Assert.That(board.Speak(world.GetState(partner), meeting.Id, "I caught fish this morning.").IsSuccess, Is.True);
                Assert.That(board.Speak(world.GetState(actor), meeting.Id, "Please clarify what you mean.").IsSuccess, Is.True);
            }
            world.Observe(Time(752));
            if (stockChanged) store.CommitCharacter(Character(actor, 0, 25));
            Assert.That(board.Speak(world.GetState(partner), meeting.Id, item.question).IsSuccess, Is.True);
            var entities = new List<NpcObservationEntity> {
                new NpcObservationEntity("pond.water", "Pond", 1, 0, "landmark", interactionId: "fishing", resourceItemId: Fish),
                new NpcObservationEntity("decor.plant", "Decorative plant", 0, 1, "decoration") };
            if (item.id == "truncated_local_absence")
                entities.AddRange(Enumerable.Range(0, 10).Select(i => new NpcObservationEntity("lamp." + i, "Lamp " + i, 1, 1, "decoration")));
            var scene = new NpcObservationScene(new[] { new NpcObservationRegion("pond", "Pond walk", -5, -5, 5, 5),
                new NpcObservationRegion("west", "West lane", -15, -5, -5, 5) }, entities, radius: 6);
            double x = item.id == "position_destination" ? -8 : 0;
            var bodies = new[] { new NpcObservationBody(actor, x, 0), new NpcObservationBody(partner, x + 1, 0) };
            var capture = new CaptureClient();
            var profile = DefaultMvpContent.CreateConfiguration().Npcs.Single(n => n.Id == actor);
            using var scheduler = new NpcDecisionScheduler(world, new[] { profile }, capture, meetings: board,
                observe: request => scene.Read(actor, request.Self.WorldRunId, world.TotalMinutes, bodies,
                    request.Social, trading.Inspect(terms, actor), board.GetMemories(actor)));
            scheduler.Tick(0);
            Assert.That(capture.Request, Is.Not.Null, item.id);
            setup.capturedAssets = CaptureAssets(trading, terms);
            setup.actorX = x;
            setup.partnerX = x + 1;
            return capture.Request;
        }

        private static int[] CaptureAssets(CharacterResourceTrading trading, CharacterTradeTerms terms)
        {
            var ren = trading.Inspect(terms, Ren);
            var sora = trading.Inspect(terms, Sora);
            return new[] { ren.OwnedQuantity, sora.OwnedQuantity, ren.Balance, sora.Balance };
        }

        private static CharacterEconomySnapshot Character(string id, int quantity, int coins)
            => new CharacterEconomySnapshot(id, new InventorySnapshot(quantity == 0 ? Array.Empty<ItemStack>() : new[] { new ItemStack(Fish, quantity) }), new WalletSnapshot(coins));
        private static WorldTimeProgress Time(int minute) => new WorldTimeProgress(new GameClockSnapshot(1, minute), 0, false, 1);
        private sealed class CaptureClient : INpcDecisionClient
        {
            internal NpcDecisionRequest Request;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            { Request = request; return new TaskCompletionSource<NpcDecisionReply>().Task; }
        }
        [Serializable] private sealed class Corpus { public int schemaVersion; public Case[] cases; }
        [Serializable] private sealed class Case { public string id, actor, question, expected, expectedFact; }
        [Serializable] private sealed class CandidateHeader { public int schemaVersion; }
        [Serializable] private sealed class Report
        {
            public string stage, state;
            public List<FixtureSetup> setups = new List<FixtureSetup>();
            public List<Sample> samples = new List<Sample>();
        }
        [Serializable] private sealed class FixtureSetup
        {
            public string caseId, worldRunId, fixtureMutation, presenceInput;
            public int[] initialAssets, capturedAssets;
            public int initialMemories, capturedAtGameMinute;
            public bool initialMeetingAbsent;
            public double actorX, partnerX;
        }
        [Serializable] private sealed class Sample
        {
            public int ordinal, repeat, httpStatus;
            public long elapsedMilliseconds;
            public string caseId, arm, expected, contextJson, rawReply, operation, status, error, displayedText, speechIntent, factId, tone;
        }
    }
}
#endif
