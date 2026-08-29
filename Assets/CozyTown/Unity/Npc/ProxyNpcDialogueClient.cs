using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CozyTown.Runtime.Npc;

namespace CozyTown.Unity.Npc
{
    public sealed class ProxyNpcDialogueClient : IAiNpcDialogueClient
    {
        private static readonly HttpClient SharedHttpClient = new HttpClient();

        private readonly string _endpoint;
        private readonly ProxyNpcDialogueJsonCodec _codec;

        public ProxyNpcDialogueClient(string endpoint)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new ArgumentException(
                    "Dialogue proxy endpoint must be an absolute HTTP or HTTPS URI.",
                    nameof(endpoint));
            }

            _endpoint = uri.AbsoluteUri;
            _codec = new ProxyNpcDialogueJsonCodec();
        }

        public async Task<AiNpcDialogueCandidate> GenerateAsync(
            NpcDialogueRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(
                    _codec.SerializeRequest(request),
                    Encoding.UTF8,
                    "application/json")
            };

            HttpResponseMessage response;
            try
            {
                response = await SharedHttpClient.SendAsync(message, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                throw new AiNpcDialogueClientException(
                    AiNpcDialogueClientFailure.Transport,
                    "Dialogue proxy connection failed.",
                    exception);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new AiNpcDialogueClientException(
                        AiNpcDialogueClientFailure.Provider,
                        $"Dialogue proxy returned HTTP {(int)response.StatusCode}.");
                }

                string json;
                try
                {
                    json = await response.Content.ReadAsStringAsync();
                }
                catch (HttpRequestException exception)
                {
                    throw new AiNpcDialogueClientException(
                        AiNpcDialogueClientFailure.Transport,
                        "Dialogue proxy response could not be read.",
                        exception);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return _codec.ParseResponse(json);
            }
        }
    }
}
