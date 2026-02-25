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
using System.Linq;
using OE.Shared.Providers;
using Microsoft.Extensions.DependencyInjection;
using OE.Shared.Messages.OutgoingAdapterMessages;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace OE.Job.LiveStream.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {
        private readonly IBetGeniusRepository _bgRepo;
        private readonly ILogger<SportsbookRepository> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;

        private IMongoCollection<Fixture> _fixtureCollection;

        private readonly Counter<long> _jobRunFixturesUpdated;

        public SportsbookRepository(IConfiguration configuration, ILogger<SportsbookRepository> logger, Instrumentation instrumentation, IServiceScopeFactory scopeFactory, IBetGeniusRepository bgRepo)
        {
            _configuration = configuration;
            _logger = logger;
            _jobRunFixturesUpdated = instrumentation.JobRunFixturesUpdated;

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

            if (!fixtures.Any())
                _logger.LogInformation($"ProcessLiveStream: Exiting");


            foreach(var ch in Enum.GetValues(typeof(ChannelType)))
            {
                _logger.LogInformation($"ProcessLiveStream: {ch}");

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var providerFactory =
                        scope.ServiceProvider.GetRequiredService<ILiveStreamFactory>();

                    var provider = providerFactory.Resolve((int)ch);

                    var liveStreams = await provider.GetLiveStreams().ConfigureAwait(false);

                    if (!liveStreams.Any())
                    {
                        _logger.LogInformation($"ProcessLiveStream: {ch} GetLiveStreams No livestream fixtures found");
                        continue;
                    }

                    var liveStreamFixtures = fixtures.Where(f => f.ChTyId == (int)ch && liveStreams.Contains(f.PrFiId)).ToList();

                    if (!liveStreamFixtures.Any())
                    {
                        _logger.LogInformation($"ProcessLiveStream: {ch} No livestream matches the active fixtures");
                        continue;
                    }

                    foreach (var fixture in liveStreamFixtures)
                    {
                        _logger.LogInformation($"ProcessLiveStream: {ch} fixture {fixture.Id}-{fixture.PrFiId}");

                        var (isLiveStream, liveStream) = await provider.GetLiveStream(fixture.PrFiId).ConfigureAwait(false);
                        if (isLiveStream)
                            _logger.LogInformation($"ProcessLiveStream: {ch} fixture {fixture.Id}-{fixture.PrFiId} have {liveStream} permission.");

                        var result = await _fixtureCollection.BulkWriteAsync(ToFixture(fixture.Id, isLiveStream, liveStream), new BulkWriteOptions { IsOrdered = false, BypassDocumentValidation = false });
                        if (_jobRunFixturesUpdated.Enabled)
                            _jobRunFixturesUpdated.Add(1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"ProcessLiveStream: Error processing {ex.Message}-{ex.StackTrace}");
                    continue;
                }

            }

            _logger.LogInformation($"ProcessLiveStream: Finished processing {fixtures.Count} fixtures.");
        }

        private WriteModel<Fixture>[] ToFixture(string id, bool isLiveStream, string liveStream)
        {
           return new WriteModel<Fixture>[]
           {
                new UpdateOneModel<Fixture>(Builders<Fixture>.Filter.Eq(f => f.Id, id),
                    Builders<Fixture>.Update
                        .Set(s => s.LiId, isLiveStream.ToString())
                        .Set(s => s.GeAl, liveStream))
                { IsUpsert = true }
           };
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
