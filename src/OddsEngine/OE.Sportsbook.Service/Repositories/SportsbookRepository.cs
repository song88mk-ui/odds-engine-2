using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public class SportsbookRepository : ISportsbookRepository
    {
        #region Fields

        private readonly IConfiguration _configuration;

        private IMongoDatabase _db;

        private IMongoCollection<BettingStatus> bettingStatusCollection;
        private IMongoCollection<Fixture> fixtureCollection;
        private IMongoCollection<FixtureMapping> fixtureMappingCollection;
        private IMongoCollection<Market> marketCollection;
        private IMongoCollection<MarketMapping> marketMappingCollection;
        private IMongoCollection<Selection> selectionCollection;
        private IMongoCollection<SelectionMapping> selectionMappingCollection;

        #endregion

        #region Ctor

        public SportsbookRepository(IConfiguration configuration)
        {
            _configuration = configuration;

            PrepareDatabaseConnection();
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureSetMessage(IMessageBroker messageBroker, OnFixtureSetMessage payload, string correlationId)
        {
            throw new NotImplementedException();
        }

        public void ProcessOnMarketSetMessage(IMessageBroker messageBroker, OnMarketSetMessage payload, string correlationId)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Utilities

        private void PrepareDatabaseConnection()
        {
            var client = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            _db = client.GetDatabase("Sportsbook");

            fixtureMappingCollection = _db.GetCollection<FixtureMapping>("FixtureMapping");
            marketMappingCollection = _db.GetCollection<MarketMapping>("MarketMapping");
            selectionMappingCollection = _db.GetCollection<SelectionMapping>("SelectionMapping");

            fixtureCollection = _db.GetCollection<Fixture>("Fixture");
            marketCollection = _db.GetCollection<Market>("Market");
            selectionCollection = _db.GetCollection<Selection>("Selection");
            bettingStatusCollection = _db.GetCollection<BettingStatus>("BettingStatus");
        }

        #endregion
    }
}
