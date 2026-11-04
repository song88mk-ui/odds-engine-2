using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OE.Adapter.Service.ClientVisibility
{
    // Where the lists come from: the clients Backoffice's Config page lists (the Platform API) and the restrictions it
    // saved (Trading.dbo.ClientLeagueConfig).
    public interface IClientVisibilitySource
    {
        Task<IReadOnlyList<string>> ReadClientCodesAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<LeagueRestriction>> ReadRestrictionsAsync(CancellationToken cancellationToken);
    }

    public interface IClientVisibility
    {
        // False when the Trading connection string or the Platform API address is missing: deployments ship them empty
        // until argocd injects the real ones. The adapter then sends ClCo as the provider did, which is empty.
        bool IsConfigured { get; }

        bool IsLoaded { get; }

        // Null until the first load, so a caller leaves the message as it came.
        string ClientCodesFor(int sportId, int segmentId, int leagueId);

        // Must succeed before the adapter consumes (a message stamped from nothing would hide every league); transient
        // failures are retried, then it throws so the orchestrator restarts the service.
        Task PreloadAsync(CancellationToken cancellationToken);

        // Reloads both lists and returns the leagues whose list changed since the load before. A load that fails keeps
        // the lists in use, so a database or API outage never changes what is stamped.
        Task<IReadOnlyList<LeagueKey>> RefreshAsync(CancellationToken cancellationToken);
    }

    public class ClientVisibilityCache : IClientVisibility
    {
        private static readonly IReadOnlyList<LeagueKey> NoLeagues = new LeagueKey[0];

        private readonly IClientVisibilitySource _source;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClientVisibilityCache> _logger;

        private ClientVisibilitySnapshot _current;

        public ClientVisibilityCache(IClientVisibilitySource source, IConfiguration configuration, ILogger<ClientVisibilityCache> logger)
        {
            _source = source;
            _configuration = configuration;
            _logger = logger;
        }

        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_configuration.GetConnectionString("Trading"))
            && !string.IsNullOrWhiteSpace(_configuration["API:PlatformAPI"]);

        public bool IsLoaded => Volatile.Read(ref _current) != null;

        public string ClientCodesFor(int sportId, int segmentId, int leagueId)
        {
            return Volatile.Read(ref _current)?.ClientCodesFor(new LeagueKey(sportId, segmentId, leagueId));
        }

        public async Task PreloadAsync(CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                _logger.LogWarning("No Trading connection string or Platform API address configured, client visibility preload skipped; messages keep the ClCo the provider sent");
                return;
            }

            var retryCount = Math.Max(1, _configuration.GetValue("Custom:ClientVisibilityPreloadRetryCount", 5));
            var retryDelay = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue("Custom:ClientVisibilityPreloadRetryDelaySeconds", 10)));

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _logger.LogInformation("Preloading client visibility");
                    await RefreshAsync(cancellationToken);
                    return;
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error preloading client visibility (attempt {Attempt}/{RetryCount})", attempt, retryCount);

                    if (attempt >= retryCount)
                        throw;

                    await Task.Delay(retryDelay, cancellationToken);
                }
            }
        }

        public async Task<IReadOnlyList<LeagueKey>> RefreshAsync(CancellationToken cancellationToken)
        {
            var clientCodes = await _source.ReadClientCodesAsync(cancellationToken);

            // An empty client list would stamp an empty ClCo, hiding every league from every client, so it is treated
            // as a failed read and the lists in use stay.
            if (clientCodes == null || clientCodes.Count == 0)
                throw new InvalidOperationException("The Platform API returned no clients, so the clients of the leagues are left as they are");

            var restrictions = await _source.ReadRestrictionsAsync(cancellationToken);
            var next = ClientVisibilitySnapshot.Build(clientCodes, restrictions);

            var previous = Interlocked.Exchange(ref _current, next);

            if (previous == null)
            {
                _logger.LogInformation("Client visibility loaded: {ClientCount} clients, {RestrictedLeagueCount} leagues with a restriction",
                        next.ClientCount, next.RestrictedLeagueCount);
                return NoLeagues;
            }

            var changed = ClientVisibilitySnapshot.ChangedLeagues(previous, next);

            if (changed.Count > 0 || previous.ClientCount != next.ClientCount)
                _logger.LogInformation("Client visibility reloaded: {ClientCount} clients, {RestrictedLeagueCount} leagues with a restriction, {ChangedLeagueCount} leagues changed",
                        next.ClientCount, next.RestrictedLeagueCount, changed.Count);

            return changed;
        }
    }
}
