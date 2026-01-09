using IdGen;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using OE.Shared.Constants;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Enums;
using OE.Shared.Extensions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Telemetry;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;

namespace OE.Sportsbook.Service.Repositories
{
    public class SportsbookRepository : ISportsbookRepository
    {
        #region Fields

        private readonly IConfiguration _configuration;
        private readonly IMemCache _memCache;
        private readonly IHelper _helper;

        private readonly Histogram<double> _bulkWriteDurationHistogram;

        private IMongoDatabase _db;

        private IMongoCollection<BettingStatus> _bettingStatusCollection;
        private IMongoCollection<Fixture> _fixtureCollection;
        private IMongoCollection<Market> _marketCollection;
        private IMongoCollection<Selection> _selectionCollection;

        private readonly ILogger<SportsbookRepository> _logger;

        private const string FIXTURE_PREFIX = "fi";
        private const string MARKET_MAPPING_PREFIX = "mama";
        private const string SELECTION_MAPPING_PREFIX = "sema";
        private const string BETTING_STATUS_MODEL_ID = "betstop";

        private static IdGenerator _defaultIdGenerator;

        private bool _IsBetStopGlobally = false;

        #endregion

        #region Ctor

        public SportsbookRepository(IConfiguration configuration, IMemCache memCache, IHelper helper, Instrumentation instrumentation, ILogger<SportsbookRepository> logger)
        {
            _configuration = configuration;
            _memCache = memCache;
            _helper = helper;
            _bulkWriteDurationHistogram = instrumentation.BulkWriteDurationHistogram;
            _logger = logger;

            PrepareDatabaseConnection();
        }

        #endregion

        #region Methods

        public Task SetInitialData()
        {
            return Task.Run(SetBetStopStatus);
        }

        public void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
        {
            if (string.IsNullOrEmpty(message.SpNa))
            {
                _logger.LogInformationEx($"null SpNa --> {JsonSerializer.Serialize(message)}");
            }

            BulkWrite(_fixtureCollection, ToFixture(message));
        }

        public void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
        {
            if (!message.Ma.Any())
                return;

            if (string.IsNullOrEmpty(message.SpNa))
            {
                _logger.LogInformationEx($"null SpNa --> {JsonSerializer.Serialize(message)}");
            }

            var payload = PrepareInternalOddsChangeMessage(message, correlationId);

            if (payload.Ma.Count > 0
                    && payload.Ma.Count() == payload.Ma.Where(c => c.MaStId == (int)MarketStatus.Inactive).Count()
                    && payload.FiStId == (int)FixtureStatus.Prematch
                    && payload.FiStDa < DateTime.UtcNow)
            {
                //TODO: ToUpdateFixtureCollectionStatus
                //payload.FixtureStatusId = (int)FixtureStatus.Closed;
                //payload.FixtureStatusName = FixtureStatus.Closed.ToString();

                //TODO: publish to onFixtureChange to close fixture
            }

            var data = ToMarketAndSelectionChange(message, correlationId);

            if (data.Item1.Length > 0)
                BulkWrite(_marketCollection, data.Item1);


            if (data.Item2.Length > 0)
                BulkWrite(_selectionCollection, data.Item2);

            // TODO: Betstop Logic

            //var fixture = _memCache.GetCache($"{FIXTURE_PREFIX}:{message.PrFiId}", () => GetFixtureProject(message.PrFiId), 24);

            messageBroker.Publish(payload, MessageType.ON_ODDS_CHANGE, $"r.oe.sb.engine.onoddschange", Guid.NewGuid().ToString());
        }

        public void ProcessOnMarketSettlementMessage(IMessageBroker messageBroker, OnMarketSettlementMessage message, string correlationId)
        {
            if (!message.Ma.Any())
                return;

            var timestamp = DateTime.UtcNow.ToEpochTimestamp();

            var data = ToMarketAndSelection(message, correlationId);


            if (data.Item1.Length > 0)
                BulkWrite(_marketCollection, data.Item1);

            if (data.Item2.Length > 0)
                BulkWrite(_selectionCollection, data.Item2);

            foreach (var market in message.Ma)
            {
                var marketColection = _marketCollection.Find(f => f.Id == market.Id).FirstOrDefault();
                var fixtureSpecifier = message.FiSp;

                if (marketColection is null)
                    continue;

                var payload = new
                {
                    marketColection.Id,
                    marketColection.MaId,
                    marketColection?.SpId,
                    market.MaStId,
                    market.MaStNa,
                    Se = market.Se.Select(selection => new
                    {
                        selection.Id,
                        selection.SeId,
                        selection.SeStId
                    }),
                    message.TiSe,
                    message.TiRe
                };

                messageBroker.Publish(payload, MessageType.ON_BET_SETTLEMENT, $"r.oe.sb.engine.{MessageType.ON_MARKET_SETTLEMENT.ToLower()}", Guid.NewGuid().ToString());
            }
        }

        public void ProcessOnBetStopGlobalMessage(IMessageBroker messageBroker, OnBetStopGlobalMessage message, string correlationId)
        {
            if (message == null || (_IsBetStopGlobally == message.IsBetStop))
                return;

            _logger.LogWarningEx($"Incoming OnBetStopGlobalMessage change the status ! : from {_IsBetStopGlobally} to {message.IsBetStop}");
            BulkWrite(_bettingStatusCollection, ToDBBettingStatus(message));

            var payload = new
            {
                IsBetStop = message.IsBetStop,
                CorrelationId = correlationId
            };

            _IsBetStopGlobally = message.IsBetStop;
            messageBroker.Publish(payload, MessageType.ON_BET_STOP_GLOBAL, $"r.oe.sb.engine.{MessageType.ON_BET_STOP_GLOBAL.ToLower()}", Guid.NewGuid().ToString());
        }

        public void ProcessOnBetStopFixtureMessage(IMessageBroker messageBroker, OnBetStopFixtureMessage message, string correlationId)
        {
            if (message == null || String.IsNullOrEmpty(message.PrFiId))
                return;

            _logger.LogWarningEx($"Incoming OnBetStopFixtureMessage --> {JsonSerializer.Serialize(message)}");

            var fixture = _fixtureCollection.Find(f => f.PrFiId == message.PrFiId).FirstOrDefault();
            if (fixture != null)
            {

                BulkWrite(_fixtureCollection, ToDBFixtureBettingStatus(message));

                if (message.SetActiveMarketsToInactive)
                {
                    List<long> activeMarketList = GetActiveMarketIds(message.PrFiId);
                    if (activeMarketList.Count > 0)
                    {
                        BulkWrite(_marketCollection, ToMarketInactiveStatus(activeMarketList));
                    }
                }

                var payload = new
                {
                    id = fixture.Id,
                    PrFiId = message.PrFiId,
                    TiSe = message.TiSe,
                    TiRe = message.TiRe,
                    FiId = message.FiId,
                    ChTyId = message.ChTyId,
                    FiSp = message.FiSp,
                    SpId = message.SpId,
                    FiStId = fixture.FiStId,
                    SegId = fixture.SegId,
                    LeId = fixture.LeId,
                    FiTyId = fixture.FiTyId,
                    FiStDa = fixture.FiStDa,
                    IsBetStop = message.IsBetStop,
                    SetActiveMarketsToInactive = message.SetActiveMarketsToInactive,
                    CorrelationId = correlationId
                };

                messageBroker.Publish(payload, MessageType.ON_BET_STOP_FIXTURE, $"r.oe.sb.engine.{MessageType.ON_BET_STOP_FIXTURE.ToLower()}", Guid.NewGuid().ToString());
            }
        }
        public void ProcessOnMarketCancellationMessage(IMessageBroker messageBroker, OnMarketCancellationMessage message, string correlationId)
        {
            var fixtureId = message.FiId;

            if (fixtureId == 0) return;

            var marketIds = new List<string>();

            foreach (var market in message.Ma)
            {
                var marketId = market.MaId;

                var onBetCancellationPayload = new
                {
                    MarketId = marketId,
                    MarketStatus = BetStatus.Cancelled,
                    Reason = market.VoRe,
                    SettlementScore = string.Empty, // TODO: check with product team
                    StartTime = market.StTi,
                    EndTime = market.EnTi
                };

                if (!marketIds.Contains(marketId))
                {
                    var topMarketMessage = new
                    {
                        SportId = message.SpId,
                        FixtureId = fixtureId,
                        MarketId = marketId,
                        MarketStatusId = (int)MarketStatus.Cancelled,
                        MarketStatusName = MarketStatus.Cancelled.ToString(),
                    };

                    marketIds.Add(marketId);

                    messageBroker.Publish(onBetCancellationPayload, MessageType.ON_MARKET_CANCEL, $"r.client.topmarket.settlement", Guid.NewGuid().ToString());
                }

                messageBroker.Publish(onBetCancellationPayload, MessageType.ON_BET_CANCELLATION, "r.oe.bet.settlement", Guid.NewGuid().ToString().ToUpper());
            }
        }
        #endregion

        #region Utilities

        private void PrepareDatabaseConnection()
        {
            var client = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            _db = client.GetDatabase("Sportsbook");

            _fixtureCollection = _db.GetCollection<Fixture>("Fixture");
            _marketCollection = _db.GetCollection<Market>("Market");
            _selectionCollection = _db.GetCollection<Selection>("Selection");
            _bettingStatusCollection = _db.GetCollection<BettingStatus>("BettingStatus");
        }

        private WriteModel<Fixture>[] ToFixture(OnFixtureChangeMessage message)
        {
            var filter = Builders<Fixture>.Filter.Eq(f => f.Id, message.Id);

            var updateFixture = Builders<Fixture>.Update
                .Set(s => s.Id, message.Id)
                .SetOnInsert(s => s.FiId, message.FiId)
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

            if(!string.IsNullOrEmpty(message.Sc))
                updateFixture = updateFixture.Set(
                    s => s.Sc, message.Sc);

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

        private WriteModel<Fixture>[] ToDBFixtureBettingStatus(OnBetStopFixtureMessage message)
        {
            return new WriteModel<Fixture>[]
            {
                new UpdateOneModel<Fixture>(Builders<Fixture>.Filter.Eq("PrFiId", message.PrFiId),
                    Builders<Fixture>.Update
                        .Set(s => s.IsSu, message.IsBetStop))
                { IsUpsert = false }
            };
        }

        private WriteModel<Market>[] ToMarketInactiveStatus(List<long> markets)
        {
            var marketResult = new List<WriteModel<Market>>();

            foreach (var marketId in markets)
            {
                marketResult.Add(new UpdateOneModel<Market>(Builders<Market>.Filter.Eq("MaId", marketId),
                    Builders<Market>.Update
                        .Set(s => s.MaId, marketId)
                        .Set(s => s.MaStId, (int)MarketStatus.Inactive)
                        .Set(s => s.MaStNa, MarketStatus.Inactive.ToString()))
                { IsUpsert = false });
            }

            return marketResult.ToArray();
        }

        private WriteModel<BettingStatus>[] ToDBBettingStatus(OnBetStopGlobalMessage message)
        {
            return new WriteModel<BettingStatus>[]
           {
                new UpdateOneModel<BettingStatus>(Builders<BettingStatus>.Filter.Eq("BeSo", BETTING_STATUS_MODEL_ID),
                    Builders<BettingStatus>.Update
                        .Set(s => s.IsBeSo, message.IsBetStop))
                { IsUpsert = true }
           };
        }

        private Tuple<WriteModel<Market>[], WriteModel<Selection>[]> ToMarketAndSelectionChange(OnMarketChangeMessage message, string correlationId)
        {
            var markets = new List<WriteModel<Market>>();
            var selections = new List<WriteModel<Selection>>();

            foreach (var market in message.Ma)
            {
                markets.Add(new UpdateOneModel<Market>(Builders<Market>.Filter.Eq("Id", market.Id),
                    Builders<Market>.Update
                        .Set(s => s.Id, market.Id)
                        .Set(s => s.ChTyId, message.ChTyId)
                        .Set(s => s.PrFiId, message.PrFiId)
                        .Set(s => s.PrMaId, market.PrMaId)
                        .SetOnInsert(s => s.FiId, message.FiId)
                        .SetOnInsert(s => s.MaId, market.MaId)
                        .Set(s => s.SpId, message.SpId)
                        .Set(s => s.SegId, message.SegId)
                        .Set(s => s.LeId, message.LeId)
                        .Set(s => s.MaTeId, market.MaTeId)
                        .Set(s => s.MaNa, market.MaNa)
                        .Set(s => s.Li, market.Li)
                        .Set(s => s.MaStId, market.MaStId)
                        .Set(s => s.MaStNa, market.MaStNa)
                        .Set(s => s.MaSoOr, market.SoOr)
                        .Set(s => s.FiStDa, message.FiStDa)
                        .Set(s => s.FiStId, message.FiStId)
                        .Set(s => s.CaStId, market.CaStId)
                        .Set(s => s.CaStNa, market.CaStNa)
                        .Set(s => s.ClCo, market.ClCo)
                        .Set(s => s.ClCoMa, market.ClCoMa)
                //TODO
                //.AddToSetEach(s => s.MaNaTr, 
                //    market.MarketTranslations.Select(
                //        ss => new MarketTranslationMDB 
                //            { LaCo = ss.LanguageCode, MaNa = ss.MarketName }).ToArray())
                )
                { IsUpsert = true });

                foreach (var selection in market.Se)
                {
                    selections.Add(new UpdateOneModel<Selection>(Builders<Selection>.Filter.Eq("Id", selection.Id),
                        Builders<Selection>.Update
                            .Set(s => s.ChTyId, message.ChTyId)
                            .Set(s => s.PrFiId, message.PrFiId)
                            .Set(s => s.PrMaId, market.PrMaId)
                            .Set(s => s.PrSeId, selection.PrSeId)
                            .SetOnInsert(s => s.FiId, message.FiId)
                            .SetOnInsert(s => s.MaId, market.MaId)
                            .SetOnInsert(s => s.SeId, selection.SeId)
                            .Set(s => s.SpId, message.SpId)
                            .Set(s => s.SegId, message.SegId)
                            .Set(s => s.LeId, message.LeId)
                            .Set(s => s.MaTeId, market.MaTeId)
                            .Set(s => s.SeTeId, selection.SeTeId)
                            .Set(s => s.SeNa, selection.SeNa)
                            .Set(s => s.Od, selection.Od)
                            .Set(s => s.IsAc, selection.IsAc)
                            //.Set(s => s.SeSoOr, selection.SeSoOr) TODO
                            //.Set(s => s.SeStId, 0)  TODO
                            //.Set(s => s.SeStNa, "Pending")  TODO
                            .Set(s => s.SeSp, selection.SeSp)
                            .Set(s => s.FiStDa, message.FiStDa)
                            .Set(s => s.FiStId, message.FiStId)
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
                    )
                    { IsUpsert = true });
                }
            }

            return new Tuple<WriteModel<Market>[], WriteModel<Selection>[]>(markets.ToArray(), selections.ToArray());
        }


        private Tuple<WriteModel<Market>[], WriteModel<Selection>[]> ToMarketAndSelection(OnMarketSettlementMessage message, string correlationId)
        {
            var markets = new List<WriteModel<Market>>();
            var selections = new List<WriteModel<Selection>>();

            foreach (var market in message.Ma)
            {
                markets.Add(new UpdateOneModel<Market>(Builders<Market>.Filter.Eq("Id", market.Id),
                    Builders<Market>.Update
                        .Set(s => s.MaStId, market.MaStId)
                        .Set(s => s.MaStNa, market.MaStNa)
                //TODO
                //.AddToSetEach(s => s.MaNaTr, 
                //    market.MarketTranslations.Select(
                //        ss => new MarketTranslationMDB 
                //            { LaCo = ss.LanguageCode, MaNa = ss.MarketName }).ToArray())
                )
                { IsUpsert = true });

                foreach (var selection in market.Se)
                {
                    selections.Add(new UpdateOneModel<Selection>(Builders<Selection>.Filter.Eq("Id", selection.Id),
                        Builders<Selection>.Update
                            //.Set(s => s.Od, selection.Od)
                            //.Set(s => s.IsAc, selection.IsAc)
                            //.Set(s => s.SeSoOr, selection.SeSoOr) TODO
                            .Set(s => s.SeStId, selection.SeStId)
                    //TODO
                    //.AddToSetEach(s => s.SeNaTr, selection.SelectionTranslations.Select(ss => new SelectionTranslationMDB { LaCo = ss.LanguageCode, SeNa = ss.SelectionName }).ToArray())
                    )
                    { IsUpsert = true });
                }
            }

            return new Tuple<WriteModel<Market>[], WriteModel<Selection>[]>(markets.ToArray(), selections.ToArray());
        }

        private void BulkWrite<T>(IMongoCollection<T> collection, WriteModel<T>[] data)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                var result = collection.BulkWrite(data, new BulkWriteOptions { IsOrdered = false, BypassDocumentValidation = false });
            }
            catch (Exception ex)
            {
                _logger.LogErrorEx($"error: {ex.Message}-{ex.StackTrace}");
                throw;
            }
            finally
            {
                sw.Stop();
                _bulkWriteDurationHistogram.Record(sw.Elapsed.TotalMilliseconds);
            }
        }


        private OnInternalOddsChangeMessage PrepareInternalOddsChangeMessage(OnMarketChangeMessage message, string correlationId)
        {
            //var fixtureGeneration = GetFixtureId(message.FixtureSpecifier);            
            var markets = new List<InternalMarketMessage>();

            foreach (var market in message.Ma)
            {
                if (market.Se.Count == 0) continue;

                var selections = new List<InternalSelectionMessage>();

                foreach (var selection in market.Se)
                {
                    selections.Add(new InternalSelectionMessage
                    {
                        PrFiId = message.PrFiId,
                        MaTeId = market.MaTeId,
                        SeTeId = selection.SeTeId,
                        MaSp = market.MaSp,
                        SeId = selection.SeId,
                        Od = selection.Od,
                        Pr = selection.Pr,
                        WoPr = selection.WoPr,
                        LoPr = selection.LoPr,
                        VoPr = selection.VoPr,
                        HaWoPr = selection.HaWoPr,
                        HaLoPr = selection.HaLoPr,
                        IsAc = selection.IsAc,
                        SeSp = selection.SeSp,
                        SeNa = selection.SeNa,
                        SoOr = GetSortOrder(selection.SeTeId),
                        ClOd = selection.ClOd,
                        //SelectionTranslations = selection.SelectionTranslations //TODO
                    });

                    //activity?.SetTag($"selection.id.{selectionGeneration.Item1}", selection.Odds);
                }

                //if (_matchWinnerMarkets.Contains(market.MarketTemplateId))
                //{
                //    if (market.Selections.Count > 0 && payload.FixtureStatusId == (int)FixtureStatus.Prematch)
                //    {
                //        decimal home = 0m;
                //        decimal away = 0m;

                //        foreach (var selection in selections)
                //        {
                //            if (selection.SelectionTemplateId == "1")
                //                home = selection.Odds;

                //            if (selection.SelectionTemplateId == "4")
                //                home = selection.Odds;

                //            if (selection.SelectionTemplateId == "3")
                //                away = selection.Odds;

                //            if (selection.SelectionTemplateId == "5")
                //                away = selection.Odds;
                //        }

                //        if (home < away)
                //            payload.Favorite = FavoriteSelection.Home.ToString();

                //        if (home > away)
                //            payload.Favorite = FavoriteSelection.Away.ToString();
                //    }
                //}

                markets.Add(new InternalMarketMessage
                {
                    PrFiId = message.PrFiId,
                    MaId = market.MaId,
                    MaTeId = market.MaTeId,
                    Li = market.Li,
                    MaStId = market.MaStId,
                    CaStId = market.CaStId,
                    MaSp = market.MaSp,
                    MaNa = market.MaNa,
                    SoOr = GetSortOrder(market.MaTeId),
                    ClCo = market.ClCo,
                    ClCoMa = market.ClCoMa,
                    //MarketTranslations = market.MarketTranslations, //TODO
                    Se = selections
                });
            }


            return new OnInternalOddsChangeMessage
            {
                PrFiId = message.PrFiId,
                FiId = message.FiId,
                SpId = message.SpId,
                SpNa = message.SpNa,
                SegId = message.SegId,
                SegNa = message.SegNa,
                LeId = message.LeId,
                LeNa = message.LeNa,
                FiNa = message.FiNa,
                FiStDa = message.FiStDa,
                FiStId = message.FiStId,
                FiStNa = ((FixtureStatus)message.FiStId).ToString(),
                Sc = message.Sc,
                FiSp = message.FiSp,
                IsOfLi = message.IsOfLi,
                HoReCa = message.HoReCa,
                AwReCa = message.AwReCa,
                CuPeId = message.CuPeId,
                CuPe = message.CuPe,
                CuMi = message.CuMi,
                FiTyId = message.FiTyId,
                FiTyNa = ((FixtureType)message.FiTyId).ToString(),
                ToTi = message.ToTi,
                Fa = FavoriteSelection.None.ToString(),
                SoOr = 1,
                ClCo = message.ClCo,
                CoId = correlationId,
                IsSu = message.IsSu,
                //SportTranslations = message.SportTranslations, //TODO
                //SegmentTranslations = message.SegmentTranslations,  //TODO
                //LeagueTranslations = message.LeagueTranslations,  //TODO
                //FixtureTranslations = message.FixtureTranslations,  //TODO
                //IsInsert = fixtureGeneration.Item2,
                //IsInsert = true,
                HoId = message.HoId,
                AwId = message.AwId,
                //LeagueBetradarId = message.LeagueBetradarId,
                //HomeBetradarId = message.HomeBetradarId,
                //AwayBetradarId = message.AwayBetradarId,
                //CuSer = message.CuSer,
                //PeriodScores = message.PeriodScores.Select(s => new InternalPeriodScore
                //{
                //    Home = s.Home,
                //    Away = s.Away,
                //    MatchStatusCode = s.MatchStatusCode,
                //    PeriodType = s.PeriodType
                //}).ToList()
                Ma = markets
            };
        }

        private int GetSortOrder(string id)
        {
            _ = int.TryParse(id, out int result);

            return result;
        }

        private async Task SetBetStopStatus()
        {
            var filter = Builders<BettingStatus>.Filter.Where(bs => bs.BeSo.Equals(BETTING_STATUS_MODEL_ID));
            var existingBettingStatusData = _bettingStatusCollection.Find(filter).FirstOrDefault();

            _IsBetStopGlobally = existingBettingStatusData?.IsBeSo ?? false;
            _logger.LogWarningEx($"BetStopGlobally exhibited in: {_IsBetStopGlobally}");
        }

        private List<long> GetActiveMarketIds(String fixtureId)
        {
            var marketStatusIds = new int[] { (int)MarketStatus.Active, (int)MarketStatus.Suspended };

            var filter = Builders<Market>.Filter.Where(c => c.PrFiId == fixtureId && marketStatusIds.Contains(c.MaStId));
            var projection = Builders<Market>.Projection.Include(c => c.MaId).Include(c => c.PrFiId);

            var result = _marketCollection.Find(filter).Project<MarketStatusProjection>(projection).ToList();

            return result != null ? result.Select(s => s.MaId).ToList() : new List<long>();
        }

        #endregion
    }
}
