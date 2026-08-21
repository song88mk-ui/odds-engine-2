using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace OE.WebSocket.Service.Clients
{
    // The client list comes from Platform API discovery: GET
    // {Custom:PlatformAPI}/v1/clients with the internal API key, active clients only,
    // refreshed periodically so onboarding a client is a data change, not a deploy.
    // Every instance serves ALL active clients - run exactly one instance, or
    // users receive duplicate pushes.
    public class ClientRegistry : IClientRegistry
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IConfiguration _configuration;
        private readonly ILogger<ClientRegistry> _logger;
        private readonly HttpClient _discoveryHttpClient;
        private readonly object _refreshLock = new();

        private volatile List<ClientContext> _clients = new();
        private volatile Dictionary<string, ClientContext> _byCode = new();
        private volatile Dictionary<string, ClientContext> _byId = new();

        public ClientRegistry(IConfiguration configuration, ILogger<ClientRegistry> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _discoveryHttpClient = new HttpClient
            {
                BaseAddress = new Uri(_configuration["Custom:PlatformAPI"]),
                Timeout = TimeSpan.FromSeconds(5)
            };
            _discoveryHttpClient.DefaultRequestHeaders.Add("x-api-key",
                _configuration["Custom:InternalAPIKey"] ?? _configuration["Custom:APIKey"] ?? string.Empty);
        }

        public IReadOnlyList<ClientContext> Clients => _clients;

        public ClientContext ResolveByRoutingKey(string routingKey)
        {
            if (string.IsNullOrEmpty(routingKey))
                return null;

            var suffix = routingKey.Substring(routingKey.LastIndexOf('.') + 1);

            return _byCode.TryGetValue(suffix.ToLower(), out var client) ? client : null;
        }

        public ClientContext ResolveByClientId(string clientId)
        {
            if (string.IsNullOrEmpty(clientId))
                return null;

            return _byId.TryGetValue(clientId.ToUpper(), out var client) ? client : null;
        }

        public async Task<IReadOnlyList<ClientContext>> RefreshAsync()
        {
            var pairs = await LoadClientPairs();

            lock (_refreshLock)
            {
                var added = new List<ClientContext>();
                var next = new List<ClientContext>();

                foreach (var (clientCode, clientId) in pairs)
                {
                    var code = clientCode.ToLower();

                    if (_byCode.TryGetValue(code, out var existing))
                    {
                        next.Add(existing);
                        continue;
                    }

                    var context = new ClientContext
                    {
                        ClientId = clientId,
                        ClientCode = code,
                        HttpClient = CreateClientHttpClient(clientId)
                    };

                    added.Add(context);
                    next.Add(context);

                    _logger.LogInformation("client registry: added {clientCode} ({clientId})", code, clientId);
                }

                foreach (var removed in _clients.Where(c => next.All(n => n.ClientCode != c.ClientCode)))
                    _logger.LogWarning("client registry: removed {clientCode} ({clientId})", removed.ClientCode, removed.ClientId);

                _clients = next;
                _byCode = next.ToDictionary(c => c.ClientCode);
                _byId = next
                    .Where(c => !string.IsNullOrEmpty(c.ClientId))
                    .ToDictionary(c => c.ClientId.ToUpper());

                return added;
            }
        }

        private async Task<List<(string ClientCode, string ClientId)>> LoadClientPairs()
        {
            var discovered = await DiscoverClients();

            if (discovered == null)
            {
                // Discovery failed: keep serving the current snapshot rather than dropping clients.
                _logger.LogWarning("client discovery failed, keeping current client list ({count} clients)", _clients.Count);
                return _clients.Select(c => (c.ClientCode.ToUpper(), c.ClientId)).ToList();
            }

            return discovered
                .Where(c => c.IsActive && !string.IsNullOrEmpty(c.ClientCode))
                .Select(c => (c.ClientCode, ClientId: c.ClientId.ToUpper()))
                .ToList();
        }

        // Virtual so tests can stub the Platform API response.
        protected virtual async Task<List<ClientDiscoveryDTO>> DiscoverClients()
        {
            try
            {
                var response = await _discoveryHttpClient.GetAsync("v1/clients");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("client discovery: v1/clients returned {statusCode} ({reasonPhrase})", (int)response.StatusCode, response.ReasonPhrase);
                    return null;
                }

                var body = JsonSerializer.Deserialize<ClientDiscoveryResponseDTO>(
                    await response.Content.ReadAsStringAsync(),
                    JsonOptions);

                return body?.Result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "client discovery: error calling v1/clients");
                return null;
            }
        }

        private HttpClient CreateClientHttpClient(string clientId)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(_configuration["Custom:PlatformAPI"]),
                Timeout = TimeSpan.FromSeconds(5)
            };

            httpClient.DefaultRequestHeaders.Add("x-client-id", clientId ?? string.Empty);
            httpClient.DefaultRequestHeaders.Add("x-api-key", _configuration["Custom:APIKey"] ?? string.Empty);

            return httpClient;
        }
    }

    public class ClientDiscoveryResponseDTO
    {
        public List<ClientDiscoveryDTO> Result { get; set; }
    }

    public class ClientDiscoveryDTO
    {
        public string ClientId { get; set; }
        public string ClientCode { get; set; }
        public bool IsActive { get; set; }
    }
}
