using IdGen;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Data.Sportsbook.MongoDB.Projections;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public class SportsbookRepository : ISportsbookRepository
    {
        #region Fields

        private readonly IConfiguration _configuration;
        private readonly IMemCache _memCache;
        private readonly IHelper _helper;

        private IMongoDatabase _db;

        private IMongoCollection<BettingStatus> _bettingStatusCollection;
        private IMongoCollection<Fixture> _fixtureCollection;
        private IMongoCollection<Market> _marketCollection;
        private IMongoCollection<MarketMapping> _marketMappingCollection;
        private IMongoCollection<Selection> _selectionCollection;
        private IMongoCollection<SelectionMapping> _selectionMappingCollection;

        private const string FIXTURE_PREFIX = "fi";
        private const string MARKET_MAPPING_PREFIX = "mama";
        private const string SELECTION_MAPPING_PREFIX = "sema";

        private static IdGenerator _defaultIdGenerator;

        #endregion

        #region Ctor

        public SportsbookRepository(IConfiguration configuration, IMemCache memCache, IHelper helper)
        {
            _configuration = configuration;
            _memCache = memCache;
            _helper = helper;

            PrepareDatabaseConnection();
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
        {
            var expiration = DateTime.UtcNow.AddHours(6);
            var fixtureMappingKey = $"{FIXTURE_PREFIX}:{message.ChTyId}:{message.PrFiId}";

            var (fixture, updateCache) = PreloadFixture(
                message.ChTyId, 
                message.PrFiId, 
                fixtureMappingKey, 
                expiration);

            if (fixture == null) return;

            if (updateCache)
            {
                fixture.SpId = message.SpId;
                fixture.SegId = message.SegId;
                fixture.LeId = message.LeId;
                fixture.FiStDa = message.FiStDa;

                _memCache.AddOrUpdateCache(fixtureMappingKey, fixture, expiration);
            }

            message.FiId = fixture.FiId;

            BulkWrite(_fixtureCollection, ToFixture(message));
        }

        public void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
        {
            if (!message.Ma.Any())
                return;

            var expiration = DateTime.UtcNow.AddHours(6);
            var fixtureMappingKey = $"{FIXTURE_PREFIX}:{message.ChTyId}:{message.PrFiId}";

            var (fixture, updateCache) = PreloadFixture(
                message.ChTyId, 
                message.PrFiId,
                fixtureMappingKey,
                expiration, true);

            if(fixture == null) return;

            foreach (var market in message.Ma)
            {
                var marketGeneration = GetMarketId(message.ChTyId, message.PrFiId, market.MaTeId, market.MaSp);
                
                if (marketGeneration.Item2 && market.Se.Count == 0)
                    continue;

                market.MaId = marketGeneration.Item1;

                var favorite = GetFavoriteTeam();

                foreach (var selection in market.Se)
                {
                    selection.SeId = GetSelectionId(message.ChTyId, message.PrFiId, market.MaTeId, selection.SeTeId, market.MaSp);
                }
            }

            var data = ToMarketAndSelection(fixture, message, correlationId);

            if (data.Item1.Length > 0)
                BulkWrite(_marketCollection, data.Item1);

            if (data.Item2.Length > 0)
                BulkWrite(_selectionCollection, data.Item2);

            // TODO: Betstop Logic

            //var fixture = _memCache.GetCache($"{FIXTURE_PREFIX}:{message.PrFiId}", () => GetFixtureProject(message.PrFiId), 24);

            //TODO: Publish Message using OnInternalOddsChangeMessage
        }
        public void ProcessOnSettlementMessage(IMessageBroker messageBroker, OnSettlementChangeMessage message, string correlationId)
        {
            // Todo: update mongodb market status and selection status based on settlement message
            
            throw new NotImplementedException();
        }
        #endregion

        #region Utilities

        private void PrepareDatabaseConnection()
        {
            var client = new MongoClient(_configuration["ConnectionStrings:MongoDB-Sportsbook"]);
            _db = client.GetDatabase("Sportsbook");

            _marketMappingCollection = _db.GetCollection<MarketMapping>("MarketMapping");
            _selectionMappingCollection = _db.GetCollection<SelectionMapping>("SelectionMapping");

            _fixtureCollection = _db.GetCollection<Fixture>("Fixture");
            _marketCollection = _db.GetCollection<Market>("Market");
            _selectionCollection = _db.GetCollection<Selection>("Selection");
            _bettingStatusCollection = _db.GetCollection<BettingStatus>("BettingStatus");
        }

        private (FixtureProject?, bool) PreloadFixture(int chTyId, string prFiId, string fixtureMappingKey, DateTime expiration, bool isPreloadOnly = false)
        {            
            var cache = _memCache.GetCache<FixtureProject>(fixtureMappingKey);

            if (cache != null)
                return (cache, false);

            var fixture = GetFixtureProject(chTyId, prFiId);

            if (fixture == null)
            {
                if(isPreloadOnly)
                    return (null, false);

                var generatedId = GenerateId(prFiId);

                fixture = new FixtureProject
                {
                    FiId = generatedId,
                    PrFiId = prFiId,
                    ChTyId = chTyId
                };

                return (fixture, true);
            }

            _memCache.AddOrUpdateCache(fixtureMappingKey, fixture, expiration);

            return (fixture, false);
        }

        private void PreloadMapping(int chTyId, string providerFixtureId)
        {
            var expiration = DateTime.UtcNow.AddHours(6);
            var marketMappings = GetMarketMapping(chTyId, providerFixtureId);

            foreach (var marketMapping in marketMappings)
            {
                var marketMappingKey = $"{MARKET_MAPPING_PREFIX}:{chTyId}:{providerFixtureId}:{marketMapping.MaTeId}:{marketMapping.MaSp}";
                _memCache.AddOrUpdateCache(marketMappingKey, marketMapping, expiration);
            }

            var selectionMappings = GetSelectionMapping(chTyId, providerFixtureId);

            foreach (var selection in selectionMappings)
            {
                var selectionMappingKey = $"{SELECTION_MAPPING_PREFIX}:{chTyId}:{providerFixtureId}:{selection.MaTeId}:{selection.SeTeId}:{selection.MaSp}";
                _memCache.AddOrUpdateCache(selectionMappingKey, selection, expiration);
            }
        }

        private Tuple<long, bool> GetMarketId(int chTyId, string providerFixtureId, string marketTemplateId, string marketSpecifier)
        {
            var marketMappingKey = $"{MARKET_MAPPING_PREFIX}:{chTyId}:{providerFixtureId}:{marketTemplateId}:{marketSpecifier}";
            Tuple<long, bool> market;

            var result = _memCache.GetCache<MarketMapping>(marketMappingKey);

            if (result == null)
            {
                var generatedId = GenerateId(providerFixtureId);
                var expiration = DateTime.UtcNow.AddHours(6);

                market = new Tuple<long, bool>(generatedId, true);

                _memCache.AddOrUpdateCache(marketMappingKey, new MarketMapping
                {
                    MaId = generatedId,
                    PrFiId = providerFixtureId,
                    MaTeId = marketTemplateId,
                    MaSp = marketSpecifier
                }, expiration);
            }
            else
            {
                market = new Tuple<long, bool>(result.MaId, false);
            }

            return market;
        }

        private long GetSelectionId(int chTyId, string providerFixtureId, string marketTemplateId, string selectionTemplateId, string marketSpecifier)
        {
            var expiration = DateTime.UtcNow.AddHours(6);
            var selectionMappingKey = $"{SELECTION_MAPPING_PREFIX}:{chTyId}:{providerFixtureId}:{marketTemplateId}:{selectionTemplateId}:{marketSpecifier}";

            var result = _memCache.GetCache<SelectionMapping>(selectionMappingKey);

            if (result == null)
            {
                var generatedId = GenerateId(providerFixtureId);

                _memCache.AddOrUpdateCache(selectionMappingKey, new SelectionMapping
                {
                    SeId = generatedId,
                    PrFiId = providerFixtureId,
                    MaTeId = marketTemplateId,
                    SeTeId = selectionTemplateId,
                    MaSp = marketSpecifier
                }, expiration);

                return generatedId;
            }
            else
            {
                return result.SeId;
            }
        }

        private long GenerateId(string providerFixtureId)
        {
            if (_defaultIdGenerator == null)
                _defaultIdGenerator = new IdGenerator(Convert.ToInt32(_helper.GetInstanceName(providerFixtureId)));

            return _defaultIdGenerator.CreateId();
        }

        private FixtureProject GetFixtureProject(int chTyId, string providerFixtureId)
        {
            var filter = Builders<Fixture>.Filter.Eq(f => f.ChTyId, chTyId) &
                         Builders<Fixture>.Filter.Eq(f => f.PrFiId, providerFixtureId);
            var project = Builders<Fixture>.Projection
                            .Include(c => c.FiId)
                            .Include(c => c.ChTyId)
                            .Include(c => c.PrFiId)
                            .Include(c => c.SpId)
                            .Include(c => c.SegId)
                            .Include(c => c.LeId)
                            .Include(c => c.FiStDa)
                            .Include(c => c.FiStId);

            var result = _fixtureCollection.Find(filter).Project<FixtureProject>(project).FirstOrDefault();

            return result;
        }

        private List<MarketMapping> GetMarketMapping(int chTyId, string providerFixtureId)
        {
            var filter = Builders<MarketMapping>.Filter.Eq(f => f.ChTyId, chTyId) & 
                         Builders<MarketMapping>.Filter.Eq("PrFiId", providerFixtureId);
            var result = _marketMappingCollection.Find(filter).ToList();

            return result;
        }

        private List<SelectionMapping> GetSelectionMapping(int chTyId, string providerFixtureId)
        {
            var filter = Builders<SelectionMapping>.Filter.Eq(f => f.ChTyId, chTyId) & 
                         Builders<SelectionMapping>.Filter.Eq("PrFiId", providerFixtureId);
            var result = _selectionMappingCollection.Find(filter).ToList();

            return result;
        }

        private WriteModel<Fixture>[] ToFixture(OnFixtureChangeMessage message)
        {
            var filter = Builders<Fixture>.Filter.And(
                Builders<Fixture>.Filter.Eq(f => f.ChTyId, message.ChTyId),
                Builders<Fixture>.Filter.Eq(f => f.PrFiId, message.PrFiId)
            );

            var updateFixture = Builders<Fixture>.Update
                .Set(s => s.FiId, message.FiId)
                .Set(s => s.ChTyId, message.ChTyId)
                .Set(s => s.PrFiId, message.PrFiId)
                .Set(s => s.SpId, message.SpId)
                .Set(s => s.SpNa, message.SpNa)
                .Set(s => s.SegId, message.SegId)
                .Set(s => s.SegNa, message.SegNa)
                .Set(s => s.LeId, message.LeId)
                .Set(s => s.LeNa, message.LeNa)
                .Set(s => s.FiNa, message.FiNa)
                .Set(s => s.FiStDa, message.FiStDa)
                .Set(s => s.FiStId, message.FiStId)
                .Set(s => s.FiStNa, message.FiStNa)
                .Set(s => s.FiTyId, message.FiTyId)
                .Set(s => s.FiTyNa, message.FiTyNa)
                .Set(s => s.Sc, message.Sc)
                .Set(s => s.IsOfLi, message.IsOfLi)
                .Set(s => s.HoReCa, message.HoReCa)
                .Set(s => s.AwReCa, message.AwReCa)
                .Set(s => s.CuPe, message.CuPeId)
                .Set(s => s.CuMi, message.CuMi)
                .Set(s => s.FiSp, message.FiSp)
                .Set(s => s.FiSoOr, message.SoOr)
                .Set(s => s.ToTi, message.ToTi)
                .Set(s => s.IsSu, message.IsSu)
                .Set(s => s.ClCo, message.ClCo)
                .Set(s => s.HoId, message.HoId)
                .Set(s => s.AwId, message.AwId);
                //s.Set(s => s.LiId, message.LiId) TODO
                //.Set(s => s.StUr, message.StUr) TODO
                //.Set(s => s.ClMa, message.ClMa) TODO
                //.Set(s => s.GeAl, message.GeAl) TODO
                //.Set(s => s.IsBo, message.IsBo) TODO
                //.Set(s => s.ClBo, message.ClBo) TODO
                //.Set(s => s.SpNaTr, message.SpNaTr) TODO
                //.Set(s => s.SegNaTr, message.SegNaTr) TODO
                //.Set(s => s.LeNaTr, message.LeNaTr) TODO
                //.Set(s => s.FiNaTr, message.FiNaTr) TODO
                //.Set(s => s.HoBeId, message.HoBeId) TODO
                //.Set(s => s.AwBeId, message.AwBeId) TODO
                //.Set(s => s.LeBeId, message.LeBeId) TODO
                //.Set(s => s.PeSc, message.PeSc) TODO
                //.Set(s => s.CuSe, message.CuSe) TODO

            if (message.Fa != FavoriteSelection.None.ToString())
            {
                updateFixture = updateFixture.Set(
                    s => s.IsHoFa, 
                    message.Fa == FavoriteSelection.Home.ToString());
            }

            return new WriteModel<Fixture>[]
            {
                    new UpdateOneModel<Fixture>(filter, updateFixture)
                    { IsUpsert = true }
            };
        }

        private Tuple<WriteModel<Market>[], WriteModel<Selection>[]> ToMarketAndSelection(FixtureProject fixture, OnMarketChangeMessage message, string correlationId)
        {
            var markets = new List<WriteModel<Market>>();
            var selections = new List<WriteModel<Selection>>();

            foreach (var market in message.Ma)
            {
                markets.Add(new UpdateOneModel<Market>(Builders<Market>.Filter.Eq("MaId", market.MaId),
                    Builders<Market>.Update
                        .Set(s => s.MaId, market.MaId)
                        .Set(s => s.ChTyId, message.ChTyId)
                        .Set(s => s.SpId, fixture.SpId)
                        .Set(s => s.SegId, fixture.SegId)
                        .Set(s => s.LeId, fixture.LeId)
                        .Set(s => s.FiId, fixture.FiId)
                        .Set(s => s.MaTeId, market.MaTeId)
                        .Set(s => s.MaNa, market.MaNa)
                        .Set(s => s.Li, market.Li)
                        .Set(s => s.MaStId, market.MaStId)
                        .Set(s => s.MaStNa, market.MaStNa)
                        .Set(s => s.MaSoOr, market.SoOr)
                        .Set(s => s.FiStDa, fixture.FiStDa)
                        .Set(s => s.FiStId, fixture.FiStId)
                        .Set(s => s.CaStId, market.CaStId)
                        .Set(s => s.CaStNa, market.CaStNa)
                        .Set(s => s.ClCo, market.ClCo)
                        .Set(s => s.ClCoMa, market.ClCoMa)
                        //TODO
                        //.AddToSetEach(s => s.MaNaTr, 
                        //    market.MarketTranslations.Select(
                        //        ss => new MarketTranslationMDB 
                        //            { LaCo = ss.LanguageCode, MaNa = ss.MarketName }).ToArray())
                ) { IsUpsert = true });

                foreach (var selection in market.Se)
                {
                    selections.Add(new UpdateOneModel<Selection>(Builders<Selection>.Filter.Eq("SeId", selection.SeId),
                        Builders<Selection>.Update
                            .Set(s => s.SeId, selection.SeId)
                            .Set(s => s.ChTyId, message.ChTyId)
                            .Set(s => s.SpId, fixture.SpId)
                            .Set(s => s.SegId, fixture.SegId)
                            .Set(s => s.LeId, fixture.LeId)
                            .Set(s => s.FiId, fixture.FiId)
                            .Set(s => s.MaId, market.MaId)
                            .Set(s => s.MaTeId, market.MaTeId)
                            .Set(s => s.SeTeId, selection.SeTeId)
                            .Set(s => s.SeNa, selection.SeNa)
                            .Set(s => s.Od, selection.Od)
                            .Set(s => s.IsAc, selection.IsAc)
                            //.Set(s => s.SeSoOr, selection.SeSoOr) TODO
                            //.Set(s => s.SeStId, 0)  TODO
                            //.Set(s => s.SeStNa, "Pending")  TODO
                            .Set(s => s.SeSp, selection.SeSp)
                            .Set(s => s.FiStDa, fixture.FiStDa)
                            .Set(s => s.FiStId, fixture.FiStId)
                            .Set(s => s.MaStId, market.MaStId)
                            .Set(s => s.CoId, correlationId)
                            .Set(s => s.Pr, selection.Pr)
                            .Set(s => s.WoPr, selection.WoPr)
                            .Set(s => s.LoPr, selection.LoPr)
                            .Set(s => s.VoPr, selection.VoPr)
                            .Set(s => s.HaWoPr, selection.HaWoPr)
                            .Set(s => s.HaLoPr, selection.HaLoPr)
                            //TODO
                            //.AddToSetEach(s => s.SeNaTr, selection.SelectionTranslations.Select(ss => new SelectionTranslationMDB { LaCo = ss.LanguageCode, SeNa = ss.SelectionName }).ToArray())
                    ) { IsUpsert = true });
                }
            }

            return new Tuple<WriteModel<Market>[], WriteModel<Selection>[]>(markets.ToArray(), selections.ToArray());
        }

        private void BulkWrite<T>(IMongoCollection<T> collection, WriteModel<T>[] data)
        {
            collection.BulkWrite(data, new BulkWriteOptions { IsOrdered = false, BypassDocumentValidation = false });
        }

        private string GetFavoriteTeam()
        {
            // TODO

            return string.Empty;
        }

        #endregion
    }
}
