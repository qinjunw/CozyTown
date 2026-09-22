#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Content;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CozyTown.Tests.PlayMode
{
    public sealed partial class NpcResourceScenarioPlayModeTests
    {
        [Test]
        public void ExpressionEvidence_StopsReadingAnOversizedReplyAtThePublicClientLimit()
        {
            int maximum = ProxyNpcDecisionJsonCodec.MaximumResponseBytes;
            var stream = new CountingReplyStream(maximum * 4);
            var call = new Call();
            using var http = new HttpClient(new ExpressionEvidenceHandler(call,
                new ControlledReplyHandler(new StreamingReplyContent(stream))));
            var world = new NpcAgentWorld(new[] { new NpcDailySchedule(Ren, "home", "outside", "entry",
                "work", "rest", "work", 360, 480, 720, 780, 1020, 1080) });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var capture = new CaptureExpressionRequestClient();
            using var scheduler = new NpcDecisionScheduler(world,
                DefaultMvpContent.CreateConfiguration().Npcs.Where(profile => profile.Id == Ren), capture);
            scheduler.Tick(0);

            Assert.ThrowsAsync<FormatException>(() => new ProxyNpcDecisionClient("http://127.0.0.1:1/decide", http)
                .DecideAsync(capture.Request, CancellationToken.None));

            Assert.That(stream.BytesRead, Is.EqualTo(maximum + 1));
            Assert.That(call.rawReplyTruncated, Is.True);
            Assert.That(call.rawReplyJson.Length, Is.EqualTo(maximum + 1));
            Assert.That(call.responseStatusCode, Is.EqualTo(200));
        }

        [UnityTest]
        public IEnumerator FreshExpressionScenes_KeepFourRegisteredWorldsAndRecordTheWirePayload()
        {
            var report = ExpressionReport("fixed_expression");
            yield return RunMatrix(report, null, 1, false, null, expression: true);

            Assert.That(report.trials.Select(trial => trial.scenario),
                Is.EqualTo(new[] { "available", "available", "seller_empty", "seller_empty" }));
            Assert.That(report.trials.Select(trial => trial.arm), Is.EqualTo(new[] { "F", "S", "S", "F" }));
            Assert.That(report.trials.Select(trial => trial.worldRunId).Distinct().Count(), Is.EqualTo(4));
            Assert.That(report.trials.All(trial => trial.initialized && trial.recovered
                && trial.hostViolations.Count == 0 && trial.calls.Count <= 12), Is.True);
            foreach (var trial in report.trials)
            foreach (var call in trial.calls)
            {
                Assert.That(call.sentContextJson, Is.EqualTo(call.contextJson));
                Assert.That(call.sentContextJson, Does.Contain("\"expression\":{"));
                Assert.That(call.sentContextJson, Does.Contain("\"mode\":\"" + trial.speechMode + "\""));
                Assert.That(call.sentContextJson, Does.Not.Contain("experimentObservation"));
                Assert.That(call.rawReplyJson, Is.Not.Null.And.Not.Empty);
                Assert.That(call.responseStatusCode, Is.EqualTo(200));
                if (call.operation == "say" && trial.arm == "S")
                {
                    Assert.That(call.speechFrame.factId, Does.EndWith(":region_name"));
                    Assert.That(call.text, Is.Null);
                }
            }
            foreach (var trial in report.trials.Where(trial => trial.scenario == "available"))
            {
                Assert.That(trial.activelyEnded, Is.True);
                Assert.That(trial.terminalActivitiesReleased, Is.True);
                Assert.That(trial.renMemoryRecords.Count(memory => memory.kind == "meeting.spoken"), Is.EqualTo(2));
                Assert.That(trial.soraMemoryRecords.Count(memory => memory.kind == "meeting.spoken"), Is.EqualTo(2));
                if (trial.arm == "S") Assert.That(trial.terminal.transcript.All(text => text.Contains("池塘周边")), Is.True);
            }
        }

        [UnityTest]
        [Category("ExternalProvider")]
        [Timeout(600000)]
        public IEnumerator LiveProxy_ComparedExpressionModesAcrossFourFreshWorlds()
        {
            if (Environment.GetEnvironmentVariable("COZYTOWN_RUN_EXPRESSION_EXPERIMENT") != "1")
                Assert.Ignore("Requires expression-experiment opt-in and two loopback proxies sharing a 48-call scene budget.");
            Assert.That(int.TryParse(Environment.GetEnvironmentVariable("COZYTOWN_EXPRESSION_BASE_PORT"), out int port)
                && port >= 1 && port <= 65534, Is.True, "The base port must leave room for two consecutive loopback ports.");
            string configuredPath = Environment.GetEnvironmentVariable("COZYTOWN_EXPRESSION_REPORT_PATH");
            string path = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
                ? "Logs/agent-expression-scene.json" : configuredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var file = new FileStream(path, FileMode.CreateNew)) { }
            var report = ExpressionReport("live_expression");
            Write(report, path);
            try
            {
                yield return RunMatrix(report, null, 1, false, path, expression: true, expressionBasePort: port);
                report.status = report.trials.All(trial => trial.initialized && trial.hostViolations.Count == 0 && trial.recovered)
                    ? "host_checks_passed" : "host_checks_failed";
                Assert.That(report.trials.Count, Is.EqualTo(4));
                Assert.That(report.trials.Sum(trial => trial.calls.Count(call => call.sentContextJson != null)), Is.LessThanOrEqualTo(48));
                Assert.That(report.status, Is.EqualTo("host_checks_passed"));
            }
            finally
            {
                if (report.status == "started") report.status = "interrupted";
                Write(report, path);
            }
        }

        [UnityTest]
        public IEnumerator StructuredSpeech_UsesSceneFactsForRenderedDialogueAndParticipantMemories()
        {
            yield return LoadFreshTown(Scenarios[0]);
            var client = new StructuredSceneClient();
            _controller.ConfigureDecisions(client,
                DefaultMvpContent.CreateConfiguration().Npcs.Where(n => n.Id == Ren || n.Id == Sora),
                meetingPlans: DefaultNpcResourcePlans.Create(), speechMode: NpcSpeechMode.StructuredFacts);
            double elapsed = 0;
            while (elapsed < 100 && _controller.GetMeeting(Sora)?.State != NpcMeetingState.Completed)
            {
                Advance(0.5);
                elapsed += 0.5;
                _controller.TickDecisions(elapsed);
                yield return null;
            }

            var meeting = _controller.GetMeeting(Sora);
            Assert.That(meeting?.State, Is.EqualTo(NpcMeetingState.Completed));
            Assert.That(meeting.DeliveryResultCode, Is.EqualTo("resource.delivered"));
            Assert.That(meeting.Transcript.Count, Is.EqualTo(2));
            Assert.That(meeting.Transcript.All(line => line.Text.Contains("池塘周边")), Is.True);
            Assert.That(_controller.GetComponent<NpcMeetingDialogueView>().VisibleText, Does.Contain("池塘周边"));
            foreach (string npcId in new[] { Ren, Sora })
                Assert.That(_controller.GetMeetingMemories(npcId).Where(memory => memory.Kind == "meeting.spoken")
                    .Select(memory => memory.Text), Is.EquivalentTo(meeting.Transcript.Select(line => line.Text)));
            var final = Observe(elapsed);
            Assert.That(new[] { final.renFish, final.soraFish, final.renCoins, final.soraCoins },
                Is.EqualTo(new[] { 1, 1, 25, 25 }));
            Assert.That(client.SpeechRequests.Count, Is.EqualTo(2));
            Assert.That(client.SpeechRequests.All(request => request.Observation != null
                && request.Observation.Facts.Any(fact => fact.FactId == request.NpcId + ":region_name"
                    && fact.Value == "池塘周边")), Is.True);
        }

        [UnityTest]
        public IEnumerator UnknownSpeechFact_PreservesTheCandidateWithoutChangingDialogueMemoriesOrAssets()
        {
            yield return LoadFreshTown(Scenarios[0]);
            AssetRow[] beforeAssets = null;
            MemoryRecord[] beforeRenMemories = null, beforeSoraMemories = null;
            var client = new StructuredSceneClient("missing.scene.fact", () =>
            {
                if (beforeAssets != null) return;
                beforeAssets = Assets();
                beforeRenMemories = CaptureMemories(Ren);
                beforeSoraMemories = CaptureMemories(Sora);
            });
            _controller.ConfigureDecisions(client,
                DefaultMvpContent.CreateConfiguration().Npcs.Where(n => n.Id == Ren || n.Id == Sora),
                meetingPlans: DefaultNpcResourcePlans.Create(), speechMode: NpcSpeechMode.StructuredFacts);
            double elapsed = 0;
            while (elapsed < 100 && _controller.GetDecisionOutcome(Sora)?.Code.StartsWith("speech.") != true)
            {
                Advance(0.5);
                elapsed += 0.5;
                _controller.TickDecisions(elapsed);
                yield return null;
            }

            var outcome = _controller.GetDecisionOutcome(Sora);
            Assert.That(outcome?.Code, Does.StartWith("speech."));
            Assert.That(outcome.Reply.SpeechFrame.FactId, Is.EqualTo("missing.scene.fact"));
            Assert.That(outcome.Reply.Text, Is.Null);
            Assert.That(_controller.GetMeeting(Sora).State, Is.EqualTo(NpcMeetingState.Talking));
            Assert.That(_controller.GetMeeting(Sora).Transcript, Is.Empty);
            Assert.That(Assets().Select(JsonUtility.ToJson), Is.EqualTo(beforeAssets.Select(JsonUtility.ToJson)));
            Assert.That(CaptureMemories(Ren).Select(JsonUtility.ToJson), Is.EqualTo(beforeRenMemories.Select(JsonUtility.ToJson)));
            Assert.That(CaptureMemories(Sora).Select(JsonUtility.ToJson), Is.EqualTo(beforeSoraMemories.Select(JsonUtility.ToJson)));
        }

        private sealed class StructuredSceneClient : INpcDecisionClient
        {
            internal readonly List<NpcDecisionRequest> SpeechRequests = new List<NpcDecisionRequest>();
            private readonly string _factId;
            private readonly Action _onSpeech;

            internal StructuredSceneClient(string factId = null, Action onSpeech = null)
            { _factId = factId; _onSpeech = onSpeech; }

            public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                var reply = await new RuleClient(false).DecideAsync(request, token);
                if (reply.Kind != NpcDecisionKind.Speak) return reply;
                SpeechRequests.Add(request);
                _onSpeech?.Invoke();
                return new NpcDecisionReply(NpcDecisionKind.Speak, meetingId: reply.MeetingId,
                    speechFrame: new NpcSpeechFrame("report_observation", _factId ?? request.NpcId + ":region_name", "neutral"));
            }
        }

        private static MatrixReport ExpressionReport(string mode)
            => new MatrixReport { mode = mode, providerCallCap = 48, startedAtUtc = DateTime.UtcNow.ToString("O"),
                registeredTrialOrder = new[] { "available:F", "available:S", "seller_empty:S", "seller_empty:F" } };

        private static string SpeechModeName(NpcSpeechMode mode)
            => mode == NpcSpeechMode.StructuredFacts ? "structured_facts" : "free_text";

        private MemoryRecord[] CaptureMemories(string npcId)
            => _controller.GetMeetingMemories(npcId).Select(memory => new MemoryRecord {
                meetingId = memory.MeetingId.ToString(), kind = memory.Kind, partnerId = memory.PartnerId,
                speakerId = memory.SpeakerId, text = memory.Text, totalMinutes = memory.TotalMinutes }).ToArray();

        private sealed class ExpressionProxyClient : INpcDecisionClient
        {
            private readonly string _endpoint;
            private readonly Trial _trial;
            private readonly bool _live;
            private readonly INpcDecisionClient _fixedClient;

            internal ExpressionProxyClient(string endpoint, Trial trial, NpcSpeechMode mode, bool live)
            {
                _endpoint = endpoint;
                _trial = trial;
                _live = live;
                _fixedClient = mode == NpcSpeechMode.StructuredFacts
                    ? (INpcDecisionClient)new StructuredSceneClient() : new RuleClient(false);
            }

            public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                var call = _trial.calls.Last(item => item.decisionId == request.DecisionId.ToString() && item.step == request.Step);
                HttpMessageHandler transport;
                if (_live) transport = new HttpClientHandler();
                else
                {
                    var reply = await _fixedClient.DecideAsync(request, token);
                    string json = reply.SpeechFrame == null
                        ? JsonUtility.ToJson(new MockCandidate { schemaVersion = request.Social?.Resources == null ? 1 : 4,
                            operation = reply.Operation, planId = reply.PlanId, meetingId = reply.MeetingId.ToString("N"), text = reply.Text })
                        : JsonUtility.ToJson(new StructuredCandidate { schemaVersion = request.Social?.Resources == null ? 1 : 4,
                            operation = reply.Operation, meetingId = reply.MeetingId.ToString("N"),
                            speechIntent = reply.SpeechFrame.Intent, factId = reply.SpeechFrame.FactId, tone = reply.SpeechFrame.Tone });
                    transport = new GroundingMockHandler(json, _ => { });
                }
                using var http = new HttpClient(new ExpressionEvidenceHandler(call, transport));
                return await new ProxyNpcDecisionClient(_endpoint, http).DecideAsync(request, token);
            }
        }

        private sealed class ExpressionEvidenceHandler : DelegatingHandler
        {
            private readonly Call _call;

            internal ExpressionEvidenceHandler(Call call, HttpMessageHandler inner)
            { _call = call; InnerHandler = inner; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                _call.sentContextJson = await request.Content.ReadAsStringAsync();
                var response = await base.SendAsync(request, token);
                _call.responseStatusCode = (int)response.StatusCode;
                if (response.Content == null) return response;
                using var body = new MemoryStream();
                try
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[4096];
                    int remaining = ProxyNpcDecisionJsonCodec.MaximumResponseBytes + 1;
                    while (remaining > 0)
                    {
                        int count = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining), token);
                        if (count == 0) break;
                        body.Write(buffer, 0, count);
                        remaining -= count;
                    }
                    byte[] bytes = body.ToArray();
                    _call.rawReplyJson = Encoding.UTF8.GetString(bytes);
                    _call.rawReplyTruncated = bytes.Length > ProxyNpcDecisionJsonCodec.MaximumResponseBytes;
                    if (_call.rawReplyTruncated)
                        throw new FormatException("Decision response exceeds 16 KiB; the evidence contains only its prefix.");
                    var original = response.Content;
                    response.Content = new ByteArrayContent(bytes);
                    foreach (var header in original.Headers)
                        response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    original.Dispose();
                    return response;
                }
                catch
                {
                    _call.rawReplyJson = Encoding.UTF8.GetString(body.ToArray());
                    response.Dispose();
                    throw;
                }
            }
        }

        private sealed class CaptureExpressionRequestClient : INpcDecisionClient
        {
            internal NpcDecisionRequest Request;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            { Request = request; return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait)); }
        }

        private sealed class ControlledReplyHandler : HttpMessageHandler
        {
            private readonly HttpContent _content;
            internal ControlledReplyHandler(HttpContent content) => _content = content;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = _content });
        }

        private sealed class StreamingReplyContent : HttpContent
        {
            private readonly Stream _stream;
            internal StreamingReplyContent(Stream stream) => _stream = stream;
            protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_stream);
            protected override Task SerializeToStreamAsync(Stream destination, TransportContext context)
                => _stream.CopyToAsync(destination);
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
        }

        private sealed class CountingReplyStream : Stream
        {
            private int _remaining;
            internal int BytesRead { get; private set; }
            internal CountingReplyStream(int length) => _remaining = length;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                int accepted = Math.Min(_remaining, count);
                for (int index = 0; index < accepted; index++) buffer[offset + index] = (byte)'x';
                _remaining -= accepted;
                BytesRead += accepted;
                return accepted;
            }
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
            { token.ThrowIfCancellationRequested(); return Task.FromResult(Read(buffer, offset, count)); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        [Serializable] private sealed class StructuredCandidate
        {
            public int schemaVersion;
            public string operation, meetingId, speechIntent, factId, tone;
        }

        [Serializable] private sealed class SpeechFrameRecord
        {
            public string speechIntent, factId, tone;
        }

        [Serializable] private sealed class MemoryRecord
        {
            public string meetingId, kind, partnerId, speakerId, text;
            public double totalMinutes;
        }
    }
}
#endif
