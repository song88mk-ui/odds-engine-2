using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OE.Shared.Data.Trading;
using OE.Shared.Data.Trading.Entities;
using OE.Shared.Enums;
using OE.Shared.Models.Responses;
using System.Text.Json;

namespace OE.Adapter.Service.ClientVisibility
{
    // The clients are the ones Backoffice's Config page lists: GET v1/clients of the Platform API, the same call and
    // key as Backoffice.API's PlatformRepository.GetClients, so a client the page can restrict is a client the lists
    // contain. The restrictions are the Config page's saved rows with IsActive 0. Both are small, and are read in
    // full on every refresh.
    public class ClientVisibilitySource : IClientVisibilitySource
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly HttpMessageHandler _messageHandler;

        // created on the first read, so an adapter that has no Platform API address configured starts without one
        private HttpClient _httpClient;

        public ClientVisibilitySource(IServiceScopeFactory scopeFactory, IConfiguration configuration, HttpMessageHandler messageHandler = null)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _messageHandler = messageHandler;
        }

        public async Task<IReadOnlyList<string>> ReadClientCodesAsync(CancellationToken cancellationToken)
        {
            var response = await GetHttpClient().GetAsync("v1/clients", cancellationToken);
            response.EnsureSuccessStatusCode();

            var body = JsonSerializer.Deserialize<BaseResponseDTO<List<PlatformClient>>>(
                await response.Content.ReadAsStringAsync(cancellationToken), JsonOptions);

            if (body?.StatusCode != StatusCode.Success || body.Result == null)
                throw new InvalidOperationException($"The Platform API clients call answered {body?.StatusCode.ToString() ?? "nothing"}");

            return body.Result.Select(client => client.ClientCode).Where(code => !string.IsNullOrWhiteSpace(code)).ToList();
        }

        public async Task<IReadOnlyList<LeagueRestriction>> ReadRestrictionsAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<TradingContext>();

            var rows = await context.ClientLeagueConfig.AsNoTracking()
                .Where(row => row.IsActive == 0)
                .ToListAsync(cancellationToken);

            return RestrictionsFrom(rows);
        }

        // A league is visible unless Backoffice restricted it (BO-139), so only IsActive 0 is a restriction: a row with a
        // NULL IsActive (the column is NULL-able, Backoffice never writes one) is not, and neither is a row without a
        // client or a league, which names nothing to restrict. Such a row must not fail the whole read.
        public static IReadOnlyList<LeagueRestriction> RestrictionsFrom(IEnumerable<ClientLeagueConfig> rows)
        {
            return rows
                .Where(row => row.IsActive == 0 && !string.IsNullOrWhiteSpace(row.ClientCode)
                    && row.SportId.HasValue && row.SegmentId.HasValue && row.LeagueId.HasValue)
                .Select(row => new LeagueRestriction
                {
                    ClientCode = row.ClientCode,
                    League = new LeagueKey(row.SportId.Value, row.SegmentId.Value, row.LeagueId.Value)
                })
                .ToList();
        }

        private HttpClient GetHttpClient()
        {
            if (_httpClient != null)
                return _httpClient;

            var client = _messageHandler == null ? new HttpClient() : new HttpClient(_messageHandler, disposeHandler: false);
            client.BaseAddress = new Uri(_configuration["API:PlatformAPI"]);
            client.Timeout = TimeSpan.FromSeconds(_configuration.GetValue("Custom:ClientVisibilityApiTimeoutSeconds", 15));
            client.DefaultRequestHeaders.Add("x-api-key", _configuration["API:APIKey"]);

            _httpClient = client;
            return client;
        }

        // only the client code is needed
        private sealed class PlatformClient
        {
            public string ClientCode { get; set; }
        }
    }
}
