using System;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;
using CozyTown.Runtime.NpcAgents;
using CozyTown.Runtime.NpcLife;
using CozyTown.Runtime.Time;
using CozyTown.Unity.Npc;
using NUnit.Framework;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProxyNpcDecisionClientTests
    {
        [TestCase("file:///tmp/agent")]
        [TestCase("relative")]
        public void InvalidEndpoint_IsRejected(string endpoint)
            => Assert.Throws<ArgumentException>(() => new ProxyNpcDecisionClient(endpoint));

        [Test]
        public void HttpFailure_IsReportedWithoutParsingTheBody()
        {
            using var http = new HttpClient(new Handler((message, token) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("Unavailable") })));
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await new ProxyNpcDecisionClient("https://agent.invalid/decide", http).DecideAsync(Request(), CancellationToken.None));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OversizedResponse_IsBoundedWithOrWithoutContentLength(bool knownLength)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new string('x', ProxyNpcDecisionJsonCodec.MaximumResponseBytes + 1));
            using var http = new HttpClient(new Handler((message, token) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = knownLength ? (HttpContent)new ByteArrayContent(bytes) : new UnknownLengthContent(bytes) })));
            Assert.ThrowsAsync<FormatException>(async () =>
                await new ProxyNpcDecisionClient("https://agent.invalid/decide", http).DecideAsync(Request(), CancellationToken.None));
        }

        [Test]
        public async Task Cancellation_ReachesThePendingTransport()
        {
            var entered = new TaskCompletionSource<bool>();
            using var http = new HttpClient(new Handler(async (message, token) =>
            {
                entered.SetResult(true);
                await Task.Delay(Timeout.Infinite, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }));
            using var cancellation = new CancellationTokenSource();
            var task = new ProxyNpcDecisionClient("https://agent.invalid/decide", http).DecideAsync(Request(), cancellation.Token);
            await entered.Task;
            cancellation.Cancel();
            try { await task; Assert.Fail("The cancelled transport must not return a candidate."); }
            catch (OperationCanceledException) { }
        }

        private sealed class UnknownLengthContent : HttpContent
        {
            private readonly byte[] _bytes;
            public UnknownLengthContent(byte[] bytes) => _bytes = bytes;
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
                => stream.WriteAsync(_bytes, 0, _bytes.Length);
            protected override bool TryComputeLength(out long length) { length = 0; return false; }
        }

        [Test]
        public async Task DecisionProxy_PostsBoundJsonAndReturnsACandidate()
        {
            string body = null;
            using var http = new HttpClient(new Handler(async (message, token) =>
            {
                Assert.That(message.Method, Is.EqualTo(HttpMethod.Post));
                Assert.That(message.RequestUri.AbsoluteUri, Is.EqualTo("https://agent.invalid/decide"));
                Assert.That(message.Content.Headers.ContentType.MediaType, Is.EqualTo("application/json"));
                body = await message.Content.ReadAsStringAsync();
                return new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent("{\"schemaVersion\":1,\"operation\":\"wait\"}") };
            }));

            var reply = await new ProxyNpcDecisionClient("https://agent.invalid/decide", http)
                .DecideAsync(Request(), CancellationToken.None);

            Assert.That(body, Does.Contain("\"npcId\":\"mina\""));
            Assert.That(reply.Kind, Is.EqualTo(NpcDecisionKind.Wait));
        }

        private static NpcDecisionRequest Request()
        {
            var schedule = new NpcDailySchedule("mina", "home", "outside", "entry", "work", "rest", "work",
                360, 480, 720, 780, 1020, 1080);
            var world = new NpcAgentWorld(new[] { schedule });
            world.Observe(new WorldTimeProgress(new GameClockSnapshot(1, 720), 0, false, 1));
            var capture = new CaptureClient();
            using var scheduler = new NpcDecisionScheduler(world,
                new[] { new NpcDefinition("mina", "Mina", "Shopkeeper", "Hello") }, capture);
            scheduler.Tick(0);
            return capture.Request;
        }

        private sealed class CaptureClient : INpcDecisionClient
        {
            public NpcDecisionRequest Request;
            public Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken token)
            {
                Request = request;
                return Task.FromResult(new NpcDecisionReply(NpcDecisionKind.Wait));
            }
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
            public Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
                => _send(request, token);
        }
    }
}
