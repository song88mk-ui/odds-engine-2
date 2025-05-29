using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public class SportsbookRepository : ISportsbookRepository
    {
        #region Fields

        private readonly IConfiguration _configuration;
        private readonly IMemCache _memCache;

        private IMongoDatabase _db;

        private IMongoCollection<BettingStatus> _bettingStatusCollection;
        private IMongoCollection<Fixture> _fixtureCollection;
        private IMongoCollection<FixtureMapping> _fixtureMappingCollection;
        private IMongoCollection<Market> _marketCollection;
        private IMongoCollection<MarketMapping> _marketMappingCollection;
        private IMongoCollection<Selection> _selectionCollection;
        private IMongoCollection<SelectionMapping> _selectionMappingCollection;

        private const string FIXTURE_MAPPING_PREFIX = "fima";
        private const string MARKET_MAPPING_PREFIX = "mama";
        private const string SELECTION_MAPPING_PREFIX = "sema";

        #endregion

        #region Ctor

        public SportsbookRepository(IConfiguration configuration, IMemCache memCache)
        {
            _configuration = configuration;
            _memCache = memCache;
            
            PrepareDatabaseConnection();
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureSetMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
        {
            PreloadMapping(message.PrFiId);
        }

        public void ProcessOnMarketSetMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region Utilities

        private void PrepareDatabaseConnection()
        {
            var client = new MongoClient(_configuration["ConnectionStrings:MongoDB-Sportsbook"]);
            _db = client.GetDatabase("Sportsbook");

            _fixtureMappingCollection = _db.GetCollection<FixtureMapping>("FixtureMapping");
            _marketMappingCollection = _db.GetCollection<MarketMapping>("MarketMapping");
            _selectionMappingCollection = _db.GetCollection<SelectionMapping>("SelectionMapping");

            _fixtureCollection = _db.GetCollection<Fixture>("Fixture");
            _marketCollection = _db.GetCollection<Market>("Market");
            _selectionCollection = _db.GetCollection<Selection>("Selection");
            _bettingStatusCollection = _db.GetCollection<BettingStatus>("BettingStatus");
        }

        private void PreloadMapping(string providerFixtureId)
        {
            var expiration = DateTime.UtcNow.AddHours(6);
            var fixtureMappingKey = $"{FIXTURE_MAPPING_PREFIX}:{providerFixtureId}";

            var cache = _memCache.GetCache<FixtureMapping>(fixtureMappingKey);

            if (cache != null)
                return;

            var fixtureMapping = GetFixtureMapping(providerFixtureId);

            if (fixtureMapping == null)
                return;

            _memCache.AddOrUpdateCache(fixtureMappingKey, new FixtureMapping
            {
                FiId = fixtureMapping.FiId,
                PrFiId = providerFixtureId
            }, expiration);

            var marketMappings = GetMarketMapping(providerFixtureId);

            foreach (var marketMapping in marketMappings)
            {
                var marketMappingKey = $"{MARKET_MAPPING_PREFIX}:{providerFixtureId}:{marketMapping.MaTeId}:{marketMapping.MaSp}";

                _memCache.AddOrUpdateCache(marketMappingKey, new MarketMapping
                {
                    MaId = marketMapping.MaId,
                    PrFiId = providerFixtureId,
                    MaTeId = marketMapping.MaTeId,
                    MaSp = marketMapping.MaSp
                }, expiration);
            }

            var selectionMappings = GetSelectionMapping(providerFixtureId);

            foreach (var selection in selectionMappings)
            {
                var selectionMappingKey = $"{SELECTION_MAPPING_PREFIX}:{providerFixtureId}:{selection.MaTeId}:{selection.SeTeId}:{selection.MaSp}";

                _memCache.AddOrUpdateCache(selectionMappingKey, new SelectionMapping
                {
                    SeId = selection.SeId,
                    PrFiId = providerFixtureId,
                    MaTeId = selection.MaTeId,
                    SeTeId = selection.SeTeId,
                    MaSp = selection.MaSp
                }, expiration);
            }
        }

        private FixtureMapping GetFixtureMapping(string providerFixtureId)
        {
            var filter = Builders<FixtureMapping>.Filter.Eq("PrFiId", providerFixtureId);
            var result = _fixtureMappingCollection.Find(filter).FirstOrDefault();

            return result;
        }
        private List<MarketMapping> GetMarketMapping(string providerFixtureId)
        {
            var filter = Builders<MarketMapping>.Filter.Eq("PrFiId", providerFixtureId);
            var result = _marketMappingCollection.Find(filter).ToList();

            return result;
        }
        private List<SelectionMapping> GetSelectionMapping(string providerFixtureId)
        {
            var filter = Builders<SelectionMapping>.Filter.Eq("PrFiId", providerFixtureId);
            var result = _selectionMappingCollection.Find(filter).ToList();

            return result;
        }

        #endregion
    }
}
