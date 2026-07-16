using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pandora.Network
{
    public class PandoraHttpClient(HttpClient client)
    {
        public readonly HttpClient _client = client;
        public HttpRequestHeaders Headers { get => _client.DefaultRequestHeaders; }

        public async Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken = default)
        {
            var response = await _client.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return doc.RootElement.Clone();
        }

        public async Task<JsonElement> PostJsonAsync(string url, object body, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(body);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return doc.RootElement.Clone();
        }
    }

    public class PandoraHttpClientFactory
    {
        private readonly SocketsHttpHandler _handler;
        public string? ProxyUrl { get; }

        public PandoraHttpClientFactory(string? proxyUrl = null)
        {
            ProxyUrl = proxyUrl;
            _handler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            };
            if (!string.IsNullOrEmpty(proxyUrl))
            {
                _handler.Proxy = new WebProxy(proxyUrl) { BypassProxyOnLocal = true };
            }
        }

        public PandoraHttpClient CreatePandoraClient()
        {
            return new PandoraHttpClient(CreateClient());
        }
        public HttpClient CreateClient()
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }
}