using System;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.NpcAgents;
using UnityEngine;

namespace CozyTown.Unity.Npc
{
    public sealed class ProxyNpcDecisionClient : INpcDecisionClient, INpcDecisionConfiguration
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient();
        private readonly Uri _endpoint;
        private readonly HttpClient _httpClient;
        private readonly ProxyNpcDecisionJsonCodec _codec = new ProxyNpcDecisionJsonCodec();

        public string SnapshotConfiguration { get; }

        public ProxyNpcDecisionClient(string endpoint, HttpClient httpClient = null, string snapshotConfiguration = null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("Decision proxy endpoint must be an absolute HTTP or HTTPS URI.", nameof(endpoint));
            _endpoint = uri;
            _httpClient = httpClient ?? SharedHttpClient;
            SnapshotConfiguration = DescribeConfiguration(snapshotConfiguration);
        }

        private static string DescribeConfiguration(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            SnapshotModelConfiguration configuration;
            try { configuration = JsonUtility.FromJson<SnapshotModelConfiguration>(json); }
            catch (ArgumentException) { throw new ArgumentException("Snapshot model configuration must be a JSON object."); }
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.model)
                || configuration.model.Length > 128 || string.IsNullOrWhiteSpace(configuration.promptVersion)
                || configuration.promptVersion.Length > 256 || configuration.protocolVersion != 4
                || configuration.maxTokens != 512 || configuration.thinking != "disabled"
                || configuration.responseFormat != "json_object" || configuration.stream)
                throw new ArgumentException("Snapshot model configuration must identify the model, prompt version, and supported proxy generation settings.");
            return JsonUtility.ToJson(configuration);
        }

        [Serializable]
        private sealed class SnapshotModelConfiguration
        {
            public string model;
            public string promptVersion;
            public int protocolVersion;
            public int maxTokens;
            public string thinking;
            public string responseFormat;
            public bool stream;
        }

        public async Task<NpcDecisionReply> DecideAsync(NpcDecisionRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
                { Content = new StringContent(_codec.SerializeRequest(request), Encoding.UTF8, "application/json") };
            using var response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            bool candidateFailure = (int)response.StatusCode == 422;
            if (!candidateFailure) response.EnsureSuccessStatusCode();
            if (response.Content == null) throw new FormatException("Decision proxy returned no response content.");
            int maximum = ProxyNpcDecisionJsonCodec.MaximumResponseBytes;
            if (response.Content.Headers.ContentLength > maximum)
                throw new FormatException("Decision proxy response exceeds the 16 KiB response limit.");
            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                int count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                if (body.Length + count > maximum)
                    throw new FormatException("Decision proxy response exceeds the 16 KiB response limit.");
                body.Write(buffer, 0, count);
            }
            cancellationToken.ThrowIfCancellationRequested();
            string json = Encoding.UTF8.GetString(body.ToArray());
            if (candidateFailure)
            {
                CandidateFailurePayload failure = null;
                try { failure = JsonUtility.FromJson<CandidateFailurePayload>(json); }
                catch (ArgumentException) { }
                if (failure?.error == "provider.candidate_invalid" && NpcCandidateException.IsKnownCode(failure.candidateErrorCode))
                    throw new NpcCandidateException(failure.candidateErrorCode);
                response.EnsureSuccessStatusCode();
            }
            return _codec.ParseResponse(json, request);
        }

        [Serializable]
        private sealed class CandidateFailurePayload
        {
            public string error, candidateErrorCode;
        }
    }
}
