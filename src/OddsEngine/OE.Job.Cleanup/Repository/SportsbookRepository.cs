
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OE.Job.Cleanup.Telemetry;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using System.Linq;

namespace OE.Job.Cleanup.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {

        private readonly ILogger<SportsbookRepository> _logger;
        private readonly IConfiguration _configuration;

        private int _sportRetention;

        private IMongoCollection<Fixture> _fixtureCollection;
        private IMongoCollection<Market> _marketCollection;
        private IMongoCollection<Selection> _selectionCollection;

        //private IMongoCollection<FixtureMapping> _fixtureMappingCollection;
        //private IMongoCollection<MarketMapping> _marketMappingCollection;
        //private IMongoCollection<SelectionMapping> _selectionMappingCollection;

        private readonly Counter<long> _jobRunFixturesDeletedTotal;

        public SportsbookRepository(IConfiguration configuration, ILogger<SportsbookRepository> logger, Instrumentation instrumentation)
        {
            _configuration = configuration;
            _logger = logger;
            _jobRunFixturesDeletedTotal = instrumentation.JobRunFixturesDeletedTotal;

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

        public async Task Cleanup()
        {
            // all fixtures:
            // type: match
            // status: ended, closed, cancelled, abandoned, unknown, postponed, interrupted etc
            // non virtual sports
            // started 30 days ago (retentionValue)
            var statusToBeDeleted = new[]{ 2,3,4,5,6,7,8,9,10,99}; // > 2 will include MatchABoutToStart, so we exclude it?

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
                .Project<FixtureProjection>(fixtureProjection).ToListAsync();

            var sortedFixtures = fixtures.OrderBy(o => o.FiStDa).ToList();
            var totalCount = sortedFixtures.Count;

            _logger.LogInformation("Got {count} fixture record(s) for deletion", totalCount);

            foreach (var fixture in sortedFixtures)
            {
                _logger.LogDebug("removing markets for fixture id: {id} [{fixtureId}]", fixture.Id, fixture.FiId);
                var marketCollectionFilter = Builders<Market>.Filter.Where(c => c.FiId == fixture.FiId);
                await _marketCollection.DeleteManyAsync(marketCollectionFilter);

                _logger.LogDebug("removing selections");
                var selectionCollectionFilter = Builders<Selection>.Filter.Where(c => c.FiId == fixture.FiId);
                await _selectionCollection.DeleteManyAsync(selectionCollectionFilter);

                // mappings collections

                // finally remove the fixture
                var fixtureCollectionFilter = Builders<Fixture>.Filter.Where(c => c.FiId == fixture.FiId);
                await _fixtureCollection.DeleteOneAsync(fixtureCollectionFilter);

                if (_jobRunFixturesDeletedTotal.Enabled)
                    _jobRunFixturesDeletedTotal.Add(1);

                _logger.LogInformation("Removed from Fixture: SportId={sportId} Remaining = {remaining}",
                        fixture.SpId, totalCount--);

                // delete in batch of 1000s
                if (totalCount % 1000 == 0)
                {
                    _logger.LogInformation("sleeping for 60 seconds before proceeding");
                    await Task.Delay(60000);
                }
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
