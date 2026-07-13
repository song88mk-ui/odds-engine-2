
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OE.Job.Cleanup.Telemetry;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace OE.Job.Cleanup.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {

        private readonly ILogger<SportsbookRepository> _logger;
        private readonly IConfiguration _configuration;
        private readonly IBetFixtureCache _betFixtureCache;

        private int _sportRetention;

        private IMongoCollection<Fixture> _fixtureCollection;
        private IMongoCollection<Market> _marketCollection;
        private IMongoCollection<Selection> _selectionCollection;

        //private IMongoCollection<FixtureMapping> _fixtureMappingCollection;
        //private IMongoCollection<MarketMapping> _marketMappingCollection;
        //private IMongoCollection<SelectionMapping> _selectionMappingCollection;

        private readonly Counter<long> _jobRunFixturesDeleted;

        public SportsbookRepository(IConfiguration configuration, ILogger<SportsbookRepository> logger, IBetFixtureCache betFixtureCache, Instrumentation instrumentation)
        {
            _configuration = configuration;
            _logger = logger;
            _betFixtureCache = betFixtureCache;
            _jobRunFixturesDeleted = instrumentation.JobRunFixturesDeleted;

            _sportRetention = _configuration.GetValue<int>("Custom:SportRetention", -30);

            var mongoClient = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            var db = mongoClient.GetDatabase("Sportsbook");

            _fixtureCollection = db.GetCollection<Fixture>("Fixture");
            _marketCollection = db.GetCollection<Market>("Market");
            _selectionCollection = db.GetCollection<Selection>("Selection");

            // _fixtureMappingCollection
            // _marketMappingCollection
            // _selectionMappingCollection
        }

        public async Task Cleanup(CancellationToken cancellationToken)
        {
            // fixtures with bet placements must be retained for bet history; without the
            // preloaded set every fixture would look bet-free and could be deleted
            if (!_betFixtureCache.IsLoaded)
                throw new InvalidOperationException("Bet placement preload has not completed; refusing to clean up.");

            // all fixtures:
            // type: match
            // status: ended, closed, cancelled, abandoned, unknown, postponed, interrupted etc
            // non virtual sports
            // started 30 days ago (retentionValue)
            var statusToBeDeleted = new[]{ 2,3,4,5,6,7,8,9,10,99}; // > 2 will include MatchABoutToStart, so we exclude it?

            var batchSize = _configuration.GetValue<int>("Custom:BatchSize", 5000);
            var batchDelayMs = _configuration.GetValue<int>("Custom:BatchDelayMs", 2000);
            var startHour = _configuration.GetValue<uint>("Custom:UtcWindowHours:Start", 1);
            var endHour = _configuration.GetValue<uint>("Custom:UtcWindowHours:End", 10);

            var fixtureStatusFilter = Builders<Fixture>.Filter.In(f => f.FiStId, statusToBeDeleted);
            var fixtureTypeFilter = Builders<Fixture>.Filter.Eq(f => f.FiTyId, 0);
            var fixtureDateFilter = Builders<Fixture>.Filter.Lt(f => f.FiStDa, DateTime.UtcNow.AddDays(_sportRetention));

            var fixtureFilter = fixtureStatusFilter & fixtureTypeFilter & fixtureDateFilter;

            var fixtureProjection = Builders<Fixture>.Projection
                .Include(c => c.Id)
                .Include(c => c.FiId)
                .Include(c => c.FiSp)
                .Include(c => c.SpId)
                .Include(c => c.FiStDa);

            _logger.LogDebug("Retrieving list of fixtures");

            var fixtures = await _fixtureCollection.Find(fixtureFilter)
                .Project<FixtureProjection>(fixtureProjection).ToListAsync(cancellationToken);

            var sortedFixtures = fixtures
                .Where(f => !_betFixtureCache.Contains(f.FiId))
                .OrderBy(o => o.FiStDa).ToList();
            var totalCount = sortedFixtures.Count;

            _logger.LogInformation("Got {count} fixture record(s) for deletion ({skipped} retained due to bet placements)",
                    totalCount, fixtures.Count - totalCount);

            for (var offset = 0; offset < sortedFixtures.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // stop when the maintenance window closes; the worker re-dispatches in the next window
                var hour = DateTime.UtcNow.TimeOfDay.Hours;
                if (hour < startHour || hour >= endHour)
                {
                    _logger.LogInformation("UTC window closed, stopping with {remaining} fixture(s) left for the next run",
                            sortedFixtures.Count - offset);
                    break;
                }

                var batch = sortedFixtures.Skip(offset).Take(batchSize).ToList();
                var fixtureIds = batch.Select(b => b.FiId).ToArray();

                _logger.LogDebug("removing markets for {count} fixture(s)", batch.Count);
                var marketCollectionFilter = Builders<Market>.Filter.In(c => c.FiId, fixtureIds);
                var marketResult = await _marketCollection.DeleteManyAsync(marketCollectionFilter, cancellationToken);

                _logger.LogDebug("removing selections");
                var selectionCollectionFilter = Builders<Selection>.Filter.In(c => c.FiId, fixtureIds);
                var selectionResult = await _selectionCollection.DeleteManyAsync(selectionCollectionFilter, cancellationToken);

                // mappings collections

                // finally remove the fixtures
                var fixtureCollectionFilter = Builders<Fixture>.Filter.In(c => c.FiId, fixtureIds);
                var fixtureResult = await _fixtureCollection.DeleteManyAsync(fixtureCollectionFilter, cancellationToken);

                if (_jobRunFixturesDeleted.Enabled)
                    _jobRunFixturesDeleted.Add(fixtureResult.DeletedCount);

                _logger.LogInformation("Removed batch: Fixtures={fixtures} Markets={markets} Selections={selections} Remaining={remaining}",
                        fixtureResult.DeletedCount, marketResult.DeletedCount, selectionResult.DeletedCount,
                        Math.Max(0, sortedFixtures.Count - offset - batch.Count));

                if (offset + batchSize < sortedFixtures.Count)
                    await Task.Delay(batchDelayMs, cancellationToken);
            }

            _logger.LogDebug("cleanup crew completed the job");
        }
    }

    public class FixtureProjection
    {
        [BsonId]
        public string    Id    { get; set; }
        public long     FiId   { get; set; }
        public string   FiSp   { get; set; }
        public int      SpId   { get; set; }
        public DateTime FiStDa { get; set; }
    }
}
