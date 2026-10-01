using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Job.Cleanup.Data;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OE.Job.Cleanup.Repository
{
    // In-memory set of fixture ids that have at least one persisted bet. Preloaded from the
    // platform Betting SQL database at startup, then kept current by BetPlacementWorker
    // consuming r.bet.placement. A missed live message self-heals on the next restart's preload.
    public class BetFixtureCache : IBetFixtureCache
    {
        private readonly ConcurrentDictionary<long, byte> _fixtureIds = new ConcurrentDictionary<long, byte>();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BetFixtureCache> _logger;

        private volatile bool _isLoaded;

        public BetFixtureCache(IServiceScopeFactory scopeFactory, ILogger<BetFixtureCache> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public bool IsLoaded => _isLoaded;

        public int Count => _fixtureIds.Count;

        public async Task Preload(CancellationToken cancellationToken)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<BettingContext>();

                // .AsQueryable() disambiguates from the in-box System.Linq.AsyncEnumerable on net10.0 (CS0121)
                var fixtureIds = await context.BetSelection
                    .AsQueryable()
                    .Select(b => b.FixtureId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                foreach (var fixtureId in fixtureIds)
                    _fixtureIds.TryAdd(fixtureId, 0);
            }

            _isLoaded = true;

            _logger.LogInformation("Preloaded {Count} fixture id(s) with bet placements", _fixtureIds.Count);
        }

        public void Add(long fixtureId)
        {
            _fixtureIds.TryAdd(fixtureId, 0);
        }

        public bool Contains(long fixtureId)
        {
            return _fixtureIds.ContainsKey(fixtureId);
        }
    }
}
