using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OE.Job.LiveStream.Telemetry;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using System;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;

namespace OE.Job.LiveStream.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {
        private readonly ILogger<SportsbookRepository> _logger;
        private readonly IConfiguration _configuration;

        private IMongoCollection<Fixture> _fixtureCollection;

        private readonly Counter<long> _jobRunUpdatedFixtures;

        public SportsbookRepository(IConfiguration configuration, ILogger<SportsbookRepository> logger, Instrumentation instrumentation)
        {
            _configuration = configuration;
            _logger = logger;
            _jobRunUpdatedFixtures = instrumentation.JobRunUpdatedFixtures;

            var mongoClient = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            var db = mongoClient.GetDatabase("Sportsbook");

            _fixtureCollection = db.GetCollection<Fixture>("Fixture");

        }

        public async Task ProcessLiveStream()
        {
            //TODO
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
