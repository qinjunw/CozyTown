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
    public sealed class ProxyNpcDecisionClient : INpcDecisionClient
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient();
        private readonly Uri _endpoint;
        private readonly HttpClient _httpClient;
        private readonly ProxyNpcDecisionJsonCodec _codec = new ProxyNpcDecisionJsonCodec();

        public ProxyNpcDecisionClient(string endpoint, HttpClient httpClient = null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException("Decision proxy endpoint must be an absolute HTTP or HTTPS URI.", nameof(endpoint));
            _endpoint = uri;
            _httpClient = httpClient ?? SharedHttpClient;
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
