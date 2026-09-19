using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using CozyTown.Unity.Experiments;
using NUnit.Framework;

namespace CozyTown.Tests.UnityEditMode
{
    public sealed class ProxyExperimentTelemetryTests
    {
        private const string Decision = "11111111111111111111111111111111";
        private const string Status = "{\"requestedModel\":\"deepseek-v4-flash\",\"attemptedProviderCalls\":8,\"remainingCalls\":4,"
            + "\"inflight\":0,\"endpoint\":\"private-endpoint\",\"apiKey\":\"test-secret\"}";
        private const string Selected = "{\"call\":7,\"npcId\":\"sora\",\"decisionId\":\"" + Decision
            + "\",\"step\":1,\"requestedModel\":\"deepseek-v4-flash\",\"returnedModel\":\"deepseek-flash\","
            + "\"status\":\"passed\",\"promptTokens\":null,\"completionTokens\":0,\"totalTokens\":null,"
            + "\"rawCandidate\":\"{\\\"schemaVersion\\\":1,\\\"operation\\\":\\\"wait\\\"}\","
            + "\"rawCandidateTruncated\":false,\"elapsedMilliseconds\":25,"
            + "\"candidate\":{\"schemaVersion\":1,\"operation\":\"wait\",\"extra\":\"test-secret\"},\"apiKey\":\"test-secret\"}";

        [Test]
        public async Task Capture_FiltersTheExperimentAndKeepsUnknownTokensDistinctFromZero()
        {
            var trace = Trace();
            var handler = new Handler("{\"measurements\":[" + Selected
                + ",{\"decisionId\":\"22222222222222222222222222222222\",\"step\":1,\"npcId\":\"ren\",\"rawCandidate\":\"other-experiment\"}]}");
            using var http = new HttpClient(handler);
            string report = await ProxyExperimentTelemetry.CaptureAsync("http://127.0.0.1:8765/decide", trace, http);
            Assert.That(report, Does.Contain("proxy_shared"));
            Assert.That(report, Does.Not.Contain("test-secret").And.Not.Contain("private-endpoint").And.Not.Contain("other-experiment"));
            var document = Json(report);
            Assert.That(document.SelectSingleNode("/root/status/attemptedProviderCalls").InnerText, Is.EqualTo("8"));
            Assert.That(document.SelectSingleNode("/root/recordedCalls").InnerText, Is.EqualTo("2"));
            Assert.That(document.SelectNodes("/root/measurements/item").Count, Is.EqualTo(1));
            Assert.That(document.SelectSingleNode("/root/measurements/item/promptTokens").Attributes["type"].Value, Is.EqualTo("null"));
            Assert.That(document.SelectSingleNode("/root/measurements/item/completionTokens").InnerText, Is.EqualTo("0"));
            Assert.That(trace.calls[0].rawResponseJson, Is.EqualTo("{\"schemaVersion\":1,\"operation\":\"wait\"}"));
            Assert.That(trace.calls[0].requestedModel, Is.EqualTo("deepseek-v4-flash"));
            Assert.That(trace.calls[0].returnedModel, Is.EqualTo("deepseek-flash"));
            Assert.That(Json(trace.calls[0].usageJson).SelectSingleNode("/root/promptTokens").Attributes["type"].Value, Is.EqualTo("null"));
            Assert.That(trace.calls[1].rawResponseJson, Is.Null);
            Assert.That(trace.calls[1].usageJson, Is.Null);
            Assert.That(handler.Methods, Is.EqualTo(new[] { HttpMethod.Get, HttpMethod.Get }));
            Assert.That(handler.Paths, Is.EqualTo(new[] { "/status", "/measurements" }));
        }

        [TestCase("https://localhost:8765/decide")]
        [TestCase("http://example.com/decide")]
        [TestCase("http://user:password@localhost:8765/decide")]
        [TestCase("/decide")]
        public void Capture_RejectsUnsupportedEndpointsBeforeSendingARequest(string endpoint)
        {
            var handler = new Handler("{\"measurements\":[]}");
            using var http = new HttpClient(handler);
            Assert.ThrowsAsync<ArgumentException>(() => ProxyExperimentTelemetry.CaptureAsync(endpoint, Trace(), http));
            Assert.That(handler.Paths, Is.Empty);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Capture_RejectsAmbiguousOrMismatchedRecordsWithoutChangingAnnotations(bool duplicate)
        {
            var trace = Trace();
            trace.calls[0].rawResponseJson = "previous-capture";
            string rows = duplicate ? Selected + "," + Selected : Selected.Replace("\"npcId\":\"sora\"", "\"npcId\":\"ren\"");
            using var http = new HttpClient(new Handler("{\"measurements\":[" + rows + "]}"));
            Assert.ThrowsAsync<FormatException>(() => ProxyExperimentTelemetry.CaptureAsync("http://127.0.0.1:8765/decide", trace, http));
            Assert.That(trace.calls[0].rawResponseJson, Is.EqualTo("previous-capture"));
            Assert.That(trace.calls[1].rawResponseJson, Is.EqualTo("stale"));
            Assert.That(trace.calls[1].usageJson, Is.EqualTo("stale"));
        }

        [Test]
        public void Capture_RejectsRawCandidatesLargerThanTheExportLimit()
        {
            string row = Selected.Replace("{\\\"schemaVersion\\\":1,\\\"operation\\\":\\\"wait\\\"}",
                new string('x', ProxyExperimentTelemetry.MaximumCandidateBytes + 1));
            using var http = new HttpClient(new Handler("{\"measurements\":[" + row + "]}"));
            Assert.ThrowsAsync<FormatException>(() => ProxyExperimentTelemetry.CaptureAsync("http://127.0.0.1:8765/decide", Trace(), http));
        }

        [Test]
        public void Capture_EnforcesTheCombinedResponseSizeLimit()
        {
            const string empty = "{\"measurements\":[]}";
            string measurements = empty + new string(' ', ProxyExperimentTelemetry.MaximumBodyBytes
                - Encoding.UTF8.GetByteCount(Status) - empty.Length + 1);
            Assert.That(Encoding.UTF8.GetByteCount(measurements), Is.LessThan(ProxyExperimentTelemetry.MaximumBodyBytes));
            using var http = new HttpClient(new Handler(measurements));
            Assert.ThrowsAsync<FormatException>(() => ProxyExperimentTelemetry.CaptureAsync("http://127.0.0.1:8765/decide", Trace(), http));
        }

        [Test]
        public void Capture_CancellationBeforeRequestPreservesAnnotations()
        {
            var trace = Trace();
            var handler = new Handler("{\"measurements\":[]}");
            using var http = new HttpClient(handler);
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            Assert.CatchAsync<OperationCanceledException>(() => ProxyExperimentTelemetry.CaptureAsync(
                "http://127.0.0.1:8765/decide", trace, http, canceled.Token));
            Assert.That(handler.Paths, Is.Empty);
            Assert.That(trace.calls[1].rawResponseJson, Is.EqualTo("stale"));
            Assert.That(trace.calls[1].usageJson, Is.EqualTo("stale"));
        }

        private static DecisionTrace Trace() => new DecisionTrace { calls = new List<DecisionTraceCall> {
            new DecisionTraceCall { requestJson = "{\"decisionId\":\"" + Decision + "\",\"step\":1,\"npcId\":\"sora\"}" },
            new DecisionTraceCall { requestJson = "{\"decisionId\":\"" + Decision + "\",\"step\":2,\"npcId\":\"sora\"}",
                rawResponseJson = "stale", usageJson = "stale" } } };

        private static XmlDocument Json(string json)
        {
            using var reader = JsonReaderWriterFactory.CreateJsonReader(Encoding.UTF8.GetBytes(json), XmlDictionaryReaderQuotas.Max);
            var document = new XmlDocument();
            document.Load(reader);
            return document;
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly string _measurements;
            internal readonly List<HttpMethod> Methods = new List<HttpMethod>();
            internal readonly List<string> Paths = new List<string>();
            internal Handler(string measurements) => _measurements = measurements;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Methods.Add(request.Method);
                Paths.Add(request.RequestUri.AbsolutePath);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(request.RequestUri.AbsolutePath == "/status" ? Status : _measurements) });
            }
        }
    }
}
