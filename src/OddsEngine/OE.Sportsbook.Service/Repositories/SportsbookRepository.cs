using IdGen;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Data.Sportsbook.MongoDB.Projections;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
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
        private IMongoCollection<FixtureMapping> _fixtureMappingCollection;
        private IMongoCollection<Market> _marketCollection;
        private IMongoCollection<MarketMapping> _marketMappingCollection;
        private IMongoCollection<Selection> _selectionCollection;
        private IMongoCollection<SelectionMapping> _selectionMappingCollection;

        private const string FIXTURE_PREFIX = "fima";
        private const string FIXTURE_MAPPING_PREFIX = "fima";
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
            PreloadMapping(message.PrFiId);

            var fixtureGeneration = GetFixtureId(message.PrFiId);

            message.FiId = fixtureGeneration.Item1;
            message.IsIn = fixtureGeneration.Item2;

            BulkWrite(_fixtureMappingCollection, ToFixtureMapping(message));
            BulkWrite(_fixtureCollection, ToFixture(message));
        }

        public void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
        {
            if (!message.Ma.Any())
                return;

            PreloadMapping(message.PrFiId);

            foreach (var market in message.Ma)
            {
                if (IsMarketInsertAndHasNoSelections(message.PrFiId, market.MaTeId, market.MaSp, market.Se))
                    continue;

                var marketGeneration = GetMarketId(message.PrFiId, market.MaTeId, market.MaSp);

                market.MaId = marketGeneration.Item1;
                market.IsIn = marketGeneration.Item2;

                var favorite = GetFavoriteTeam();

                foreach (var selection in market.Se)
                {
                    var selectionGeneration = GetSelectionId(message.PrFiId, market.MaTeId, selection.SeTeId, market.MaSp);

                    selection.SeId = selectionGeneration.Item1;
                    selection.IsIn = selectionGeneration.Item2;
                }
            }

            // TODO: Betstop Logic

            var fixture = _memCache.GetCache($"{FIXTURE_PREFIX}:{message.PrFiId}", () => GetFixtureProject(message.PrFiId), 24);

            // TODO: MongoDB Upsert
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

        private Tuple<long, bool> GetFixtureId(string providerFixtureId)
        {
            var expiration = DateTime.UtcNow.AddHours(6);
            var fixtureMappingKey = $"{FIXTURE_MAPPING_PREFIX}:{providerFixtureId}";

            Tuple<long, bool> fixture;

            var cache = _memCache.GetCache<FixtureMapping>(fixtureMappingKey);

            if (cache == null)
            {
                var generatedId = GenerateId(providerFixtureId);

                fixture = new Tuple<long, bool>(generatedId, true);

                _memCache.AddOrUpdateCache(fixtureMappingKey, new FixtureMapping
                {
                    FiId = generatedId,
                    PrFiId = providerFixtureId
                }, expiration);
            }
            else
            {
                fixture = new Tuple<long, bool>(cache.FiId, false);
            }

            return fixture;
        }

        private Tuple<long, bool> GetMarketId(string providerFixtureId, string marketTemplateId, string marketSpecifier)
        {
            var expiration = DateTime.UtcNow.AddHours(6);
            var marketMappingKey = $"{MARKET_MAPPING_PREFIX}:{providerFixtureId}:{marketTemplateId}:{marketSpecifier}";

            Tuple<long, bool> market;

            var result = _memCache.GetCache<MarketMapping>(marketMappingKey);

            if (result == null)
            {
                var generatedId = GenerateId(providerFixtureId);

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
        private Tuple<long, bool> GetSelectionId(string providerFixtureId, string marketTemplateId, string selectionTemplateId, string marketSpecifier)
        {
            var expiration = DateTime.UtcNow.AddHours(6);
            var selectionMappingKey = $"{SELECTION_MAPPING_PREFIX}:{providerFixtureId}:{marketTemplateId}:{selectionTemplateId}:{marketSpecifier}";

            Tuple<long, bool> selection;

            var result = _memCache.GetCache<SelectionMapping>(selectionMappingKey);

            if (result == null)
            {
                var generatedId = GenerateId(providerFixtureId);

                selection = new Tuple<long, bool>(generatedId, true);

                _memCache.AddOrUpdateCache(selectionMappingKey, new SelectionMapping
                {
                    SeId = generatedId,
                    PrFiId = providerFixtureId,
                    MaTeId = marketTemplateId,
                    SeTeId = selectionTemplateId,
                    MaSp = marketSpecifier
                }, expiration);
            }
            else
            {
                selection = new Tuple<long, bool>(result.SeId, false);
            }

            return selection;
        }

        private long GenerateId(string providerFixtureId)
        {
            if (_defaultIdGenerator == null)
                _defaultIdGenerator = new IdGenerator(Convert.ToInt32(_helper.GetInstanceName(providerFixtureId)));

            return _defaultIdGenerator.CreateId();
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

        private FixtureProject GetFixtureProject(string providerFixtureId)
        {
            var filter = Builders<Fixture>.Filter.Eq("PrFiId", providerFixtureId);
            var project = Builders<Fixture>.Projection.Include(c => c.PrFiId).Include(c => c.SpId).Include(c => c.SegId).Include(c => c.LeId);
            var result = _fixtureCollection.Find(filter).Project<FixtureProject>(project).FirstOrDefault();

            return result;
        }

        private WriteModel<FixtureMapping>[] ToFixtureMapping(OnFixtureChangeMessage message)
        {
            return new WriteModel<FixtureMapping>[]
            {
                new UpdateOneModel<FixtureMapping>(Builders<FixtureMapping>.Filter.Eq("FiId", message.FiId),
                    Builders<FixtureMapping>.Update
                        .Set(s => s.FiId, message.FiId)
                        .Set(s => s.PrFiId, message.PrFiId))
                { IsUpsert = true }
            };
        }

        private WriteModel<Fixture>[] ToFixture(OnFixtureChangeMessage message)
        {
            UpdateDefinition<Fixture> updateFixture = Builders<Fixture>.Update
                            .Set(s => s.FiId, message.FiId)
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

            if (message.Fa != FavoriteSelection.None.ToString())
            {
                updateFixture = updateFixture.Set(s => s.IsHoFa, message.Fa == FavoriteSelection.Home.ToString() ? true : false);
            }

            return new WriteModel<Fixture>[]
            {
                    new UpdateOneModel<Fixture>(Builders<Fixture>.Filter.Eq("FiId", message.FiId), updateFixture)
                    { IsUpsert = true }
            };
        }

        private Tuple<WriteModel<Market>[], WriteModel<Selection>[]> ToMarketAndSelection(OnMarketChangeMessage message, string correlationId)
        {
            var markets = new List<WriteModel<Market>>();
            var selections = new List<WriteModel<Selection>>();

            foreach (var market in message.Ma)
            {
                markets.Add(new UpdateOneModel<Market>(Builders<Market>.Filter.Eq("MaId", market.MaId),
                    Builders<Market>.Update
                        .Set(s => s.MaId, market.MaId)
                        .Set(s => s.SpId, message.SportId)
                        .Set(s => s.SegId, message.SegmentId)
                        .Set(s => s.LeId, message.LeagueId)
                        .Set(s => s.FiId, message.FixtureId)
                        .Set(s => s.MaTeId, market.MarketTemplateId)
                        .Set(s => s.MaNa, market.MarketName)
                        .Set(s => s.Li, market.Line)
                        .Set(s => s.MaStId, (int)market.MarketStatus)
                        .Set(s => s.MaStNa, market.MarketStatus.ToString())
                        .Set(s => s.MaSoOr, market.SortOrder)
                        .Set(s => s.FiStDa, message.FixtureStartDate)
                        .Set(s => s.FiStId, message.FixtureStatusId)
                        .Set(s => s.CaStId, (int)market.CashoutStatus)
                        .Set(s => s.CaStNa, market.CashoutStatus.ToString())
                        .Set(s => s.ClCo, market.ClientCodes)
                        .Set(s => s.ClCoMa, market.ClientCodeMargin)
                        .AddToSetEach(s => s.MaNaTr, market.MarketTranslations.Select(ss => new MarketTranslationMDB { LaCo = ss.LanguageCode, MaNa = ss.MarketName }).ToArray()))
                { IsUpsert = true });

                foreach (var selection in market.Selections)
                {
                    selections.Add(new UpdateOneModel<Selection>(Builders<Selection>.Filter.Eq("SeId", selection.SelectionId),
                        Builders<Selection>.Update
                            .Set(s => s.SeId, selection.SelectionId)
                            .Set(s => s.SpId, message.SportId)
                            .Set(s => s.SegId, message.SegmentId)
                            .Set(s => s.LeId, message.LeagueId)
                            .Set(s => s.FiId, message.FixtureId)
                            .Set(s => s.MaId, market.MarketId)
                            .Set(s => s.MaTeId, market.MarketTemplateId)
                            .Set(s => s.SeTeId, selection.SelectionTemplateId)
                            .Set(s => s.SeNa, selection.SelectionName)
                            .Set(s => s.Od, selection.Odds)
                            .Set(s => s.IsAc, selection.IsActive)
                            .Set(s => s.SeSoOr, selection.SortOrder)
                            .Set(s => s.SeStId, 0)
                            .Set(s => s.SeStNa, "Pending")
                            .Set(s => s.SeSp, selection.SelectionSpecifier)
                            .Set(s => s.FiStDa, message.FixtureStartDate)
                            .Set(s => s.FiStId, message.FixtureStatusId)
                            .Set(s => s.MaStId, (int)market.MarketStatus)
                            .Set(s => s.CoId, correlationId)
                            .Set(s => s.Pr, selection.Probability)
                            .Set(s => s.WoPr, selection.WonProbability)
                            .Set(s => s.LoPr, selection.LostProbability)
                            .Set(s => s.VoPr, selection.VoidProbability)
                            .Set(s => s.HaWoPr, selection.HalfWonProbability)
                            .Set(s => s.HaLoPr, selection.HalfLostProbability)
                            .AddToSetEach(s => s.SeNaTr, selection.SelectionTranslations.Select(ss => new SelectionTranslationMDB { LaCo = ss.LanguageCode, SeNa = ss.SelectionName }).ToArray()))
                    { IsUpsert = true });
                }
            }

            return new Tuple<WriteModel<Market>[], WriteModel<Selection>[]>(markets.ToArray(), selections.ToArray());
        }

        private void BulkWrite<T>(IMongoCollection<T> collection, WriteModel<T>[] data)
        {
            collection.BulkWrite(data, new BulkWriteOptions { IsOrdered = false, BypassDocumentValidation = false });
        }

        private bool IsMarketInsertAndHasNoSelections(string providerFixtureId, string marketTemplateId, string marketSpecifier, List<SelectionMessage> selections)
        {
            var marketMappingKey = $"{MARKET_MAPPING_PREFIX}:{providerFixtureId}:{marketTemplateId}:{marketSpecifier}";

            var result = _memCache.GetCache<MarketMapping>(marketMappingKey);

            if (result == null && selections.Count == 0)
                return true;
            else
                return false;
        }

        private string GetFavoriteTeam()
        {
            // TODO

            return string.Empty;
        }

        #endregion
    }
}
