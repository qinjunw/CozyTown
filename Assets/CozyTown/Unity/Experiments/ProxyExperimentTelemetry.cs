using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace CozyTown.Unity.Experiments
{
    public static class ProxyExperimentTelemetry
    {
        public const int MaximumBodyBytes = 8 * 1024 * 1024;
        public const int MaximumCandidateBytes = 16384;
        private static readonly HttpClient SharedHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });

        public static async Task<string> CaptureAsync(string endpoint, DecisionTrace trace, HttpClient http = null,
            CancellationToken cancellationToken = default)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp
                || !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Telemetry requires a loopback HTTP endpoint without credentials.", nameof(endpoint));
            if (trace?.calls == null) throw new ArgumentException("Telemetry requires a decision trace.", nameof(trace));
            var calls = trace.calls.ToArray();
            var requests = new Dictionary<string, Request>();
            foreach (var call in calls)
            {
                if (call == null || string.IsNullOrEmpty(call.requestJson))
                    throw new ArgumentException("Trace calls require request correlation fields.", nameof(trace));
                var request = ReadObject(call.requestJson);
                string id = RequiredString(request, "decisionId", 128), npc = RequiredString(request, "npcId", 128);
                long step = RequiredInteger(request, "step", 1);
                string key = Key(id, step);
                if (requests.ContainsKey(key)) throw new ArgumentException("Trace request identifiers and steps must be unique.", nameof(trace));
                requests.Add(key, new Request { Call = call, NpcId = npc });
            }
            cancellationToken.ThrowIfCancellationRequested();
            http ??= SharedHttp;
            byte[] status = await ReadBody(http, new Uri(uri, "/status"), MaximumBodyBytes, cancellationToken).ConfigureAwait(false);
            byte[] measurements = await ReadBody(http, new Uri(uri, "/measurements"), MaximumBodyBytes - status.Length, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() => BuildReport(status, measurements, requests, calls.Length, cancellationToken), cancellationToken).ConfigureAwait(false);
        }

        private sealed class Request
        {
            internal DecisionTraceCall Call;
            internal string NpcId;
            internal XmlElement Measurement;
        }

        private static async Task<byte[]> ReadBody(HttpClient http, Uri uri, int maximum, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content == null || response.Content.Headers.ContentLength > maximum)
                throw new FormatException("Proxy telemetry exceeds the response limit or has no content.");
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                int read = await input.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > maximum) throw new FormatException("Proxy telemetry exceeds the 8 MiB response limit.");
                output.Write(buffer, 0, read);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return output.ToArray();
        }

        private static string BuildReport(byte[] statusBytes, byte[] measurementBytes,
            Dictionary<string, Request> requests, int callCount, CancellationToken cancellationToken)
        {
            var status = ReadObject(statusBytes);
            var source = ReadObject(measurementBytes);
            var rows = Field(source, "measurements");
            if (rows?.GetAttribute("type") != "array") throw new FormatException("Proxy measurements must be an array.");
            var report = new XmlDocument();
            var root = Add(report, null, "root", "object");
            AddInteger(root, "schemaVersion", 1);
            AddInteger(root, "recordedCalls", callCount);
            var shared = Add(report, root, "status", "object");
            AddString(shared, "source", "proxy_shared");
            CopyString(status, shared, "requestedModel", 128, required: true);
            foreach (string name in new[] { "attemptedProviderCalls", "remainingCalls", "inflight" })
                CopyInteger(status, shared, name, required: true);
            var selected = Add(report, root, "measurements", "array");
            foreach (XmlElement row in rows.ChildNodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireObject(row);
                string id = RequiredString(row, "decisionId", 128);
                long step = RequiredInteger(row, "step", 1);
                if (!requests.TryGetValue(Key(id, step), out var request)) continue;
                if (request.Measurement != null) throw new FormatException("Proxy measurements contain duplicate records for one decision step.");
                if (RequiredString(row, "npcId", 128) != request.NpcId)
                    throw new FormatException("Proxy measurement resident does not match its recorded request.");
                var measurement = Add(report, selected, "item", "object");
                CopyInteger(row, measurement, "call", required: true, minimum: 1);
                AddString(measurement, "decisionId", id);
                AddInteger(measurement, "step", step);
                AddString(measurement, "npcId", request.NpcId);
                CopyString(row, measurement, "status", 128, required: true);
                CopyString(row, measurement, "requestedModel", 128);
                CopyString(row, measurement, "returnedModel", 128);
                CopyString(row, measurement, "candidateErrorCode", 128);
                foreach (string name in new[] { "promptTokens", "completionTokens", "totalTokens" }) CopyInteger(row, measurement, name);
                CopyInteger(row, measurement, "elapsedMilliseconds", required: true);
                CopyString(row, measurement, "rawCandidate", MaximumCandidateBytes, byteLimit: true);
                var truncated = Field(row, "rawCandidateTruncated");
                if (truncated?.GetAttribute("type") != "boolean")
                    throw new FormatException("Proxy measurements require an explicit raw candidate truncation flag.");
                Add(report, measurement, "rawCandidateTruncated", "boolean", truncated.InnerText);
                var candidate = Field(row, "candidate");
                if (candidate != null && candidate.GetAttribute("type") != "null")
                {
                    RequireObject(candidate);
                    var filtered = Add(report, measurement, "candidate", "object");
                    foreach (string name in new[] { "schemaVersion", "operation", "locationId", "activity", "durationGameMinutes",
                        "planId", "meetingId", "text", "speechIntent", "factId", "tone" })
                    {
                        var value = Field(candidate, name);
                        if (value == null) continue;
                        string type = value.GetAttribute("type");
                        if (type != "number" && type != "string" && type != "null")
                            throw new FormatException("A recorded candidate field has an unsupported JSON type.");
                        filtered.AppendChild(report.ImportNode(value, true));
                    }
                }
                request.Measurement = measurement;
            }
            int matched = requests.Values.Count(item => item.Measurement != null);
            AddInteger(root, "matchedMeasurements", matched);
            AddInteger(root, "unmatchedCalls", callCount - matched);
            string result = DecisionTraceIdentifiers.WriteJson(report);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var request in requests.Values)
            {
                var row = request.Measurement;
                request.Call.rawResponseJson = StringOrNull(row, "rawCandidate");
                request.Call.requestedModel = StringOrNull(row, "requestedModel");
                request.Call.returnedModel = StringOrNull(row, "returnedModel");
                request.Call.usageJson = row == null ? null : Usage(row);
            }
            return result;
        }

        private static string Usage(XmlElement row)
        {
            var document = new XmlDocument();
            var root = Add(document, null, "root", "object");
            foreach (string name in new[] { "promptTokens", "completionTokens", "totalTokens" })
                root.AppendChild(document.ImportNode(Field(row, name), true));
            return DecisionTraceIdentifiers.WriteJson(document);
        }

        private static string StringOrNull(XmlElement source, string name)
        {
            var field = source == null ? null : Field(source, name);
            return field?.GetAttribute("type") == "string" ? field.InnerText : null;
        }

        private static void CopyString(XmlElement source, XmlElement target, string name, int maximum,
            bool required = false, bool byteLimit = false)
        {
            var field = Field(source, name);
            if (field == null || field.GetAttribute("type") == "null")
            {
                if (required) throw new FormatException("Proxy telemetry is missing a required string field.");
                Add(target.OwnerDocument, target, name, "null");
                return;
            }
            if (field.GetAttribute("type") != "string"
                || (byteLimit ? Encoding.UTF8.GetByteCount(field.InnerText) : field.InnerText.Length) > maximum
                || (required && string.IsNullOrWhiteSpace(field.InnerText)))
                throw new FormatException("Proxy telemetry contains an invalid string field.");
            AddString(target, name, field.InnerText);
        }

        private static void CopyInteger(XmlElement source, XmlElement target, string name, bool required = false, long minimum = 0)
        {
            var field = Field(source, name);
            if (!required && (field == null || field.GetAttribute("type") == "null"))
            {
                Add(target.OwnerDocument, target, name, "null");
                return;
            }
            AddInteger(target, name, RequiredInteger(source, name, minimum));
        }

        private static string RequiredString(XmlElement source, string name, int maximum)
        {
            var field = Field(source, name);
            if (field?.GetAttribute("type") != "string" || string.IsNullOrWhiteSpace(field.InnerText) || field.InnerText.Length > maximum)
                throw new FormatException("Proxy telemetry requires a nonempty bounded string field.");
            return field.InnerText;
        }

        private static long RequiredInteger(XmlElement source, string name, long minimum)
        {
            var field = Field(source, name);
            if (field?.GetAttribute("type") != "number" || !long.TryParse(field.InnerText, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out long value) || value < minimum)
                throw new FormatException("Proxy telemetry requires a nonnegative integer field.");
            return value;
        }

        private static string Key(string decision, long step) => decision + ":" + step.ToString(CultureInfo.InvariantCulture);

        private static XmlElement ReadObject(string json) => ReadObject(Encoding.UTF8.GetBytes(json));
        private static XmlElement ReadObject(byte[] json)
        {
            try
            {
                using var reader = JsonReaderWriterFactory.CreateJsonReader(json,
                    new XmlDictionaryReaderQuotas { MaxDepth = 32, MaxStringContentLength = MaximumBodyBytes });
                var document = new XmlDocument();
                document.Load(reader);
                RequireObject(document.DocumentElement);
                return document.DocumentElement;
            }
            catch (XmlException exception) { throw new FormatException("Proxy telemetry is not valid JSON.", exception); }
        }

        private static void RequireObject(XmlElement node)
        {
            if (node?.GetAttribute("type") != "object" || node.ChildNodes.Cast<XmlElement>()
                .GroupBy(field => field.LocalName, StringComparer.Ordinal).Any(group => group.Count() > 1))
                throw new FormatException("Proxy telemetry requires JSON objects with unique fields.");
        }

        private static XmlElement Field(XmlElement source, string name)
        {
            var fields = source.ChildNodes.Cast<XmlElement>().Where(field => field.LocalName == name).ToArray();
            if (fields.Length > 1) throw new FormatException("Proxy telemetry contains duplicate fields.");
            return fields.FirstOrDefault();
        }

        private static XmlElement Add(XmlDocument document, XmlElement parent, string name, string type, string value = null)
        {
            var element = document.CreateElement(name);
            element.SetAttribute("type", type);
            if (value != null) element.InnerText = value;
            if (parent == null) document.AppendChild(element);
            else parent.AppendChild(element);
            return element;
        }
        private static void AddString(XmlElement target, string name, string value) => Add(target.OwnerDocument, target, name, "string", value);
        private static void AddInteger(XmlElement target, string name, long value)
            => Add(target.OwnerDocument, target, name, "number", value.ToString(CultureInfo.InvariantCulture));
    }
}
