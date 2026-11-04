using OE.Adapter.Service.ClientVisibility;

namespace OE.Adapter.Service.Tests
{
    // The lists of which clients may see which league, without Trading or the Platform API. A league with no entry
    // gets DefaultCodes, which is null (not loaded) unless a test sets it.
    public sealed class FakeClientVisibility : IClientVisibility
    {
        private readonly Dictionary<LeagueKey, string> _codes = new();

        public bool IsConfigured { get; set; } = true;
        public bool IsLoaded { get; set; } = true;
        public string? DefaultCodes { get; set; }
        public bool ThrowOnPreload { get; set; }
        public bool ThrowOnRefresh { get; set; }
        public int PreloadCalls { get; private set; }
        public int RefreshCalls { get; private set; }

        // what the next RefreshAsync reports as changed
        public List<LeagueKey> Changed { get; } = new();

        public void Set(int sportId, int segmentId, int leagueId, string clientCodes) => _codes[new LeagueKey(sportId, segmentId, leagueId)] = clientCodes;

        public string ClientCodesFor(int sportId, int segmentId, int leagueId)
            => _codes.TryGetValue(new LeagueKey(sportId, segmentId, leagueId), out var clientCodes) ? clientCodes : DefaultCodes!;

        public Task PreloadAsync(CancellationToken cancellationToken)
        {
            PreloadCalls++;

            if (ThrowOnPreload)
                throw new InvalidOperationException("preload failed");

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LeagueKey>> RefreshAsync(CancellationToken cancellationToken)
        {
            RefreshCalls++;

            if (ThrowOnRefresh)
                throw new InvalidOperationException("refresh failed");

            return Task.FromResult<IReadOnlyList<LeagueKey>>(Changed.ToList());
        }
    }

    public sealed class FakeLeagueRefreshRequests : ILeagueRefreshRequests
    {
        public List<LeagueKey> Requests { get; } = new();

        public void Request(int sportId, int segmentId, int leagueId) => Requests.Add(new LeagueKey(sportId, segmentId, leagueId));
    }
}
