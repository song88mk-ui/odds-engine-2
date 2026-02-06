using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OE.Shared.Repositories;
using OE.Job.LiveStream.Telemetry;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using System;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using OE.Shared.Enums;
using System.Text.Json;
using System.Linq;
using OE.Shared.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace OE.Job.LiveStream.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {
        private readonly IBetGeniusRepository _bgRepo;
        private readonly ILogger<SportsbookRepository> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;

        private IMongoCollection<Fixture> _fixtureCollection;

        private readonly Counter<long> _jobRunUpdatedFixtures;

        public SportsbookRepository(IConfiguration configuration, ILogger<SportsbookRepository> logger, Instrumentation instrumentation, IServiceScopeFactory scopeFactory, IBetGeniusRepository bgRepo)
        {
            _configuration = configuration;
            _logger = logger;
            _jobRunUpdatedFixtures = instrumentation.JobRunUpdatedFixtures;

            var mongoClient = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            var db = mongoClient.GetDatabase("Sportsbook");

            _fixtureCollection = db.GetCollection<Fixture>("Fixture");
            _scopeFactory = scopeFactory;
            _bgRepo = bgRepo;
        }

        public async Task ProcessLiveStream()
        {
            var statusToBeChecked = new[] { 0, 1 };

            var fixtureStatusFilter = Builders<Fixture>.Filter.In(f => f.FiStId, statusToBeChecked);
            var fixtureTypeFilter = Builders<Fixture>.Filter.Eq(f => f.FiTyId, 0);

            var fixtureFilter = fixtureStatusFilter & fixtureTypeFilter;

            var fixtureProjection = Builders<Fixture>.Projection
                .Include(c => c.Id)
                .Include(c => c.ChTyId)
                .Include(c => c.PrFiId)
                .Include(c => c.SpId)
                .Include(c => c.FiStDa);

            _logger.LogDebug("ProcessLiveStream: Retrieving list of active fixtures");

            var fixtures = await _fixtureCollection.Find(fixtureFilter).Project<FixtureProjection>(fixtureProjection).ToListAsync();

            _logger.LogInformation($"ProcessLiveStream: Got {fixtures.Count} fixture record(s)");
            foreach (var fixture in fixtures)
            {
                using var scope = _scopeFactory.CreateScope();
                var providerFactory =
                    scope.ServiceProvider.GetRequiredService<ILiveStreamFactory>();

                var provider = providerFactory.Resolve(fixture.ChTyId);

                var (isLiveStream, liveStream) = await provider.GetLiveStream(fixture.PrFiId).ConfigureAwait(false);

                if (isLiveStream)
                {
                    _logger.LogInformation($"ProcessLiveStream: fixture {fixture} have {liveStream} permission.");
                }
                //return result;
            }

            _logger.LogInformation($"ProcessLiveStream: Finished processing {fixtures.Count} fixtures.");
        }

    }

    public class FixtureProjection
    {
        [BsonId]
        public string Id { get; set; }
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public int SpId { get; set; }
        public DateTime FiStDa { get; set; }
    }
}
