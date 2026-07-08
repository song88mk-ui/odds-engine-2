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

namespace OE.Translation.Service.Repositories
{
    public class MongoTranslationRepository : IMongoTranslationRepository
    {
        #region Fields

        private readonly ILogger<MongoTranslationRepository> _logger;
        private readonly ITranslationCache _translationCache;

        private readonly IMongoCollection<Fixture> _fixtureCollection;
        private readonly IMongoCollection<Market> _marketCollection;
        private readonly IMongoCollection<Selection> _selectionCollection;

        // Fixtures still shown to players — the only ones worth a translation-triggered odds
        // update (persistence itself is unscoped).
        private static readonly List<int> _openFixtureStatusIds = new()
        {
            (int)FixtureStatus.Prematch,
            (int)FixtureStatus.Live,
            (int)FixtureStatus.Suspended,
            (int)FixtureStatus.MatchAboutToStart
        };

        #endregion

        #region Ctor

        public MongoTranslationRepository(
            IConfiguration configuration,
            ITranslationCache translationCache,
            ILogger<MongoTranslationRepository> logger)
        {
            _logger = logger;
            _translationCache = translationCache;

            var client = new MongoClient(configuration["Custom:MongoDB-Sportsbook"]);
            var db = client.GetDatabase("Sportsbook");

            _fixtureCollection = db.GetCollection<Fixture>("Fixture");
            _marketCollection = db.GetCollection<Market>("Market");
            _selectionCollection = db.GetCollection<Selection>("Selection");
        }

        #endregion

        #region Live path — stamp *NaTr onto documents as the raw feed flows

        // Update-only by design: OE.Sportsbook.Service's upsert creates the documents. If this
        // message beats the upsert for a brand-new document, the write matches nothing and the
        // next message for the entity heals it (accepted trade-off of the ownership split).
        public void WriteFixtureTranslations(OnFixtureChangeMessage message)
        {
            if (message == null)
                return;

            UpdateDefinition<Fixture>? update = null;

            var spTr = _translationCache.GetTranslations($"SpId:{message.SpId}");
            if (spTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.SpNaTr, spTr.Select(t => new SportTranslationMDB { LaCo = t.LaCo, SpNa = t.Tx }).ToList()));

            var segTr = _translationCache.GetTranslations($"SegId:{message.SegId}");
            if (segTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.SegNaTr, segTr.Select(t => new SegmentTranslationMDB { LaCo = t.LaCo, SegNa = t.Tx }).ToList()));

            var leTr = _translationCache.GetTranslations($"LeId:{message.LeId}");
            if (leTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.LeNaTr, leTr.Select(t => new LeagueTranslationMDB { LaCo = t.LaCo, LeNa = t.Tx }).ToList()));

            var hoTr = _translationCache.GetTranslations($"Te:{message.HoId}");
            if (hoTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.HoNaTr, hoTr.Select(t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx }).ToList()));

            var awTr = _translationCache.GetTranslations($"Te:{message.AwId}");
            if (awTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.AwNaTr, awTr.Select(t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx }).ToList()));

            if (update == null)
                return; // nothing translated for this fixture's keys — no write at all

            _fixtureCollection.UpdateOne(
                Builders<Fixture>.Filter.Eq(f => f.Id, message.Id),
                update,
                new UpdateOptions { IsUpsert = false });
        }

        public void WriteMarketTranslations(OnMarketChangeMessage message)
        {
            if (message?.Ma == null || message.Ma.Count == 0)
                return;

            // Fixture-level keys ride the market message too (same taxonomy fields).
            WriteFixtureTranslations(message);

            var marketWrites = new List<WriteModel<Market>>();
            var selectionWrites = new List<WriteModel<Selection>>();

            foreach (var market in message.Ma)
            {
                if (market == null)
                    continue;

                var maTeTr = _translationCache.GetTranslations($"MaTe:{market.MaTeId}");

                if (maTeTr != null)
                {
                    marketWrites.Add(new UpdateOneModel<Market>(
                        Builders<Market>.Filter.Eq("Id", market.Id),
                        Builders<Market>.Update.Set(m => m.MaNaTr, maTeTr.Select(t => new MarketTranslationMDB { LaCo = t.LaCo, MaNa = t.Tx }).ToList()))
                    { IsUpsert = false });
                }

                if (market.Se == null)
                    continue;

                foreach (var selection in market.Se)
                {
                    if (selection == null)
                        continue;

                    var seTeTr = _translationCache.GetTranslations($"SeTe:{market.MaTeId}:{selection.SeTeId}");

                    if (seTeTr != null)
                    {
                        selectionWrites.Add(new UpdateOneModel<Selection>(
                            Builders<Selection>.Filter.Eq("Id", selection.Id),
                            Builders<Selection>.Update.Set(s => s.SeNaTr, seTeTr.Select(t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx }).ToList()))
                        { IsUpsert = false });
                    }
                }
            }

            if (marketWrites.Count > 0)
                _marketCollection.BulkWrite(marketWrites, new BulkWriteOptions { IsOrdered = false });

            if (selectionWrites.Count > 0)
                _selectionCollection.BulkWrite(selectionWrites, new BulkWriteOptions { IsOrdered = false });
        }

        #endregion

        #region Change path — ON_TRANSLATION_CHANGE (moved from OE.Sportsbook.Service)

        // Persists the changed translations into the Mongo documents, then emits a translation-only
        // ON_ODDS_CHANGE per affected OPEN fixture — built from Mongo like the fixture-change path —
        // carrying just the changed *Tr deltas. Single consumer (one queue), so this runs exactly
        // once per change, replacing the retired 10-instance sportsbook fan-out.
        public void ProcessTranslationChange(IMessageBroker messageBroker, OnTranslationChangeMessage message, string correlationId)
        {
            if (message?.Tr == null || message.Tr.Count == 0)
                return;

            // One delta list per glossary key, all changed languages together. An empty Tx stays
            // in the delta as a removal so consumers revert to the English name.
            var deltasByKey = new Dictionary<string, List<TranslationMessage>>();

            foreach (var item in message.Tr)
            {
                if (item == null || string.IsNullOrEmpty(item.StId) || string.IsNullOrEmpty(item.LaCo))
                    continue;

                if (!deltasByKey.TryGetValue(item.StId, out var deltas))
                    deltasByKey[item.StId] = deltas = new List<TranslationMessage>();

                deltas.Add(new TranslationMessage { LaCo = item.LaCo.ToUpperInvariant(), Tx = item.Tx ?? string.Empty });
            }

            if (deltasByKey.Count == 0)
                return;

            var sportDeltas = new Dictionary<int, List<TranslationMessage>>();
            var segmentDeltas = new Dictionary<int, List<TranslationMessage>>();
            var leagueDeltas = new Dictionary<int, List<TranslationMessage>>();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>>();
            var marketTemplateDeltas = new Dictionary<string, List<TranslationMessage>>();
            var selectionTemplateDeltas = new Dictionary<(string MaTeId, string SeTeId), List<TranslationMessage>>();

            foreach (var entry in deltasByKey)
            {
                var separator = entry.Key.IndexOf(':');

                if (separator <= 0 || separator == entry.Key.Length - 1)
                    continue;

                var prefix = entry.Key.Substring(0, separator);
                var id = entry.Key.Substring(separator + 1);

                switch (prefix)
                {
                    case "SpId" when int.TryParse(id, out var spId):
                        sportDeltas[spId] = entry.Value;
                        UpdateTranslationArray(_fixtureCollection, Builders<Fixture>.Filter.Eq(f => f.SpId, spId), "SpNaTr", entry.Value, t => new SportTranslationMDB { LaCo = t.LaCo, SpNa = t.Tx });
                        break;
                    case "SegId" when int.TryParse(id, out var segId):
                        segmentDeltas[segId] = entry.Value;
                        UpdateTranslationArray(_fixtureCollection, Builders<Fixture>.Filter.Eq(f => f.SegId, segId), "SegNaTr", entry.Value, t => new SegmentTranslationMDB { LaCo = t.LaCo, SegNa = t.Tx });
                        break;
                    case "LeId" when int.TryParse(id, out var leId):
                        leagueDeltas[leId] = entry.Value;
                        UpdateTranslationArray(_fixtureCollection, Builders<Fixture>.Filter.Eq(f => f.LeId, leId), "LeNaTr", entry.Value, t => new LeagueTranslationMDB { LaCo = t.LaCo, LeNa = t.Tx });
                        break;
                    case "Te":
                        teamDeltas[id] = entry.Value;
                        UpdateTranslationArray(_fixtureCollection, Builders<Fixture>.Filter.Eq(f => f.HoId, id), "HoNaTr", entry.Value, t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx });
                        UpdateTranslationArray(_fixtureCollection, Builders<Fixture>.Filter.Eq(f => f.AwId, id), "AwNaTr", entry.Value, t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx });
                        break;
                    case "MaTe":
                        marketTemplateDeltas[id] = entry.Value;
                        UpdateTranslationArray(_marketCollection, Builders<Market>.Filter.Eq(m => m.MaTeId, id), "MaNaTr", entry.Value, t => new MarketTranslationMDB { LaCo = t.LaCo, MaNa = t.Tx });
                        break;
                    case "SeTe":
                        // "SeTe:{MaTeId}:{SeTeId}" — SeTeId is the segment after the LAST colon.
                        var lastSeparator = id.LastIndexOf(':');

                        if (lastSeparator <= 0 || lastSeparator == id.Length - 1)
                            break;

                        var maTeId = id.Substring(0, lastSeparator);
                        var seTeId = id.Substring(lastSeparator + 1);

                        selectionTemplateDeltas[(maTeId, seTeId)] = entry.Value;
                        UpdateTranslationArray(
                            _selectionCollection,
                            Builders<Selection>.Filter.And(
                                Builders<Selection>.Filter.Eq(s => s.MaTeId, maTeId),
                                Builders<Selection>.Filter.Eq(s => s.SeTeId, seTeId)),
                            "SeNaTr", entry.Value, t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx });
                        break;
                    default:
                        _logger.LogWarningEx($"OnTranslationChange: unrecognized STRING_ID format '{entry.Key}'");
                        break;
                }
            }

            PublishTranslationOnlyOddsChanges(messageBroker, correlationId,
                sportDeltas, segmentDeltas, leagueDeltas, teamDeltas, marketTemplateDeltas, selectionTemplateDeltas);
        }

        #endregion

        #region Utilities

        // Per-language upsert into a translation array (SpNaTr/SegNaTr/LeNaTr/HoNaTr/AwNaTr/MaNaTr/
        // SeNaTr): pull the language entry, push the replacement (skip the push on removal).
        // Two-step because Mongo rejects pull+push on the same field in one update; the *Tr MDB
        // items carry [BsonId] on LaCo, hence the "_id" element filter.
        private void UpdateTranslationArray<TDoc, TItem>(
            IMongoCollection<TDoc> collection,
            FilterDefinition<TDoc> filter,
            FieldDefinition<TDoc> arrayField,
            List<TranslationMessage> deltas,
            Func<TranslationMessage, TItem> toItem)
        {
            foreach (var delta in deltas)
            {
                collection.UpdateMany(filter, Builders<TDoc>.Update.PullFilter(arrayField, Builders<TItem>.Filter.Eq("_id", delta.LaCo)));

                if (!string.IsNullOrEmpty(delta.Tx))
                    collection.UpdateMany(filter, Builders<TDoc>.Update.Push(arrayField, toItem(delta)));
            }
        }

        // Emits a translation-only ON_ODDS_CHANGE per affected OPEN fixture (persistence above is
        // unscoped; only the notification fan-out is bounded), carrying just the changed *Tr deltas.
        private void PublishTranslationOnlyOddsChanges(
            IMessageBroker messageBroker,
            string correlationId,
            Dictionary<int, List<TranslationMessage>> sportDeltas,
            Dictionary<int, List<TranslationMessage>> segmentDeltas,
            Dictionary<int, List<TranslationMessage>> leagueDeltas,
            Dictionary<string, List<TranslationMessage>> teamDeltas,
            Dictionary<string, List<TranslationMessage>> marketTemplateDeltas,
            Dictionary<(string MaTeId, string SeTeId), List<TranslationMessage>> selectionTemplateDeltas)
        {
            var marketStatusIds = new List<int> { (int)MarketStatus.Active, (int)MarketStatus.Suspended };
            var fixtureFilters = new List<FilterDefinition<Fixture>>();

            if (sportDeltas.Count > 0)
                fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.SpId, sportDeltas.Keys));

            if (segmentDeltas.Count > 0)
                fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.SegId, segmentDeltas.Keys));

            if (leagueDeltas.Count > 0)
                fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.LeId, leagueDeltas.Keys));

            if (teamDeltas.Count > 0)
            {
                fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.HoId, teamDeltas.Keys));
                fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.AwId, teamDeltas.Keys));
            }

            if (marketTemplateDeltas.Count > 0)
            {
                var maTeIds = marketTemplateDeltas.Keys.ToList();

                var fixtureIds = _marketCollection
                    .Find(m => maTeIds.Contains(m.MaTeId) && marketStatusIds.Contains(m.MaStId))
                    .Project(m => m.FiId)
                    .ToList()
                    .Distinct()
                    .ToList();

                if (fixtureIds.Count > 0)
                    fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.FiId, fixtureIds));
            }

            if (selectionTemplateDeltas.Count > 0)
            {
                var pairFilters = selectionTemplateDeltas.Keys
                    .Select(p => Builders<Selection>.Filter.And(
                        Builders<Selection>.Filter.Eq(s => s.MaTeId, p.MaTeId),
                        Builders<Selection>.Filter.Eq(s => s.SeTeId, p.SeTeId)))
                    .ToList();

                var fixtureIds = _selectionCollection
                    .Find(Builders<Selection>.Filter.Or(pairFilters))
                    .Project(s => s.FiId)
                    .ToList()
                    .Distinct()
                    .ToList();

                if (fixtureIds.Count > 0)
                    fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.FiId, fixtureIds));
            }

            if (fixtureFilters.Count == 0)
                return;

            var affectedFilter = Builders<Fixture>.Filter.And(
                Builders<Fixture>.Filter.In(f => f.FiStId, _openFixtureStatusIds),
                Builders<Fixture>.Filter.Or(fixtureFilters));

            var affectedFixtures = _fixtureCollection.Find(affectedFilter).ToList();

            _logger.LogInformationEx($"OnTranslationChange: notifying {affectedFixtures.Count} open fixtures");

            foreach (var fixture in affectedFixtures)
            {
                var marketFilter = Builders<Market>.Filter.Where(c => c.FiId == fixture.FiId && marketStatusIds.Contains(c.MaStId));
                var markets = _marketCollection.Find(marketFilter).ToList();

                var selectionFilter = Builders<Selection>.Filter.Where(c => c.FiId == fixture.FiId && marketStatusIds.Contains(c.MaStId));
                var selections = _selectionCollection.Find(selectionFilter).ToList();

                var payload = PrepareTranslationOddsChangeMessage(fixture, markets, selections, correlationId);

                ApplyTranslationDeltas(payload, fixture, sportDeltas, segmentDeltas, leagueDeltas, teamDeltas, marketTemplateDeltas, selectionTemplateDeltas);

                messageBroker.Publish(payload, MessageType.ON_ODDS_CHANGE, $"r.oe.sb.engine.{MessageType.ON_ODDS_CHANGE.ToLower()}", correlationId);
            }
        }

        // Pure mapping from the changed-glossary-key deltas onto one fixture's payload — split out
        // of PublishTranslationOnlyOddsChanges so it's unit-testable without a live Mongo connection.
        // Each *Tr is set only when this fixture/market/selection actually matches a changed key;
        // everything else stays null so consumers keep whatever they already have.
        internal static void ApplyTranslationDeltas(
            OnInternalOddsChangeMessage payload,
            Fixture fixture,
            Dictionary<int, List<TranslationMessage>> sportDeltas,
            Dictionary<int, List<TranslationMessage>> segmentDeltas,
            Dictionary<int, List<TranslationMessage>> leagueDeltas,
            Dictionary<string, List<TranslationMessage>> teamDeltas,
            Dictionary<string, List<TranslationMessage>> marketTemplateDeltas,
            Dictionary<(string MaTeId, string SeTeId), List<TranslationMessage>> selectionTemplateDeltas)
        {
            payload.SpTr = sportDeltas.TryGetValue(fixture.SpId, out var spTr) ? spTr : null;
            payload.SegTr = segmentDeltas.TryGetValue(fixture.SegId, out var segTr) ? segTr : null;
            payload.LeTr = leagueDeltas.TryGetValue(fixture.LeId, out var leTr) ? leTr : null;
            payload.HoTr = fixture.HoId != null && teamDeltas.TryGetValue(fixture.HoId, out var hoTr) ? hoTr : null;
            payload.AwTr = fixture.AwId != null && teamDeltas.TryGetValue(fixture.AwId, out var awTr) ? awTr : null;

            foreach (var market in payload.Ma)
            {
                market.MaTeTr = market.MaTeId != null && marketTemplateDeltas.TryGetValue(market.MaTeId, out var maTeTr) ? maTeTr : null;

                foreach (var selection in market.Se)
                {
                    selection.SeTeTr = selection.MaTeId != null && selection.SeTeId != null
                        && selectionTemplateDeltas.TryGetValue((selection.MaTeId, selection.SeTeId), out var seTeTr) ? seTeTr : null;
                }
            }
        }

        // Builds the internal odds payload from the Mongo documents, mirroring
        // OE.Sportsbook.Service's doc-driven PrepareInternalOddsChangeMessage — but with NO change
        // gate: this service publishes only explicit deltas, set by the caller after this returns.
        private OnInternalOddsChangeMessage PrepareTranslationOddsChangeMessage(
            Fixture fixture,
            List<Market> filteredMarkets,
            List<Selection> filteredSelections,
            string correlationId)
        {
            var message = ToFixtureChangeMessage(fixture);

            var markets = new List<InternalMarketMessage>();

            foreach (var market in filteredMarkets)
            {
                var marketSelections = filteredSelections.Where(s => s.MaId == market.MaId).ToList();
                if (marketSelections.Count == 0) continue;

                var selections = new List<InternalSelectionMessage>();

                foreach (var selection in marketSelections)
                {
                    selections.Add(new InternalSelectionMessage
                    {
                        PrFiId = message.PrFiId,
                        MaTeId = market.MaTeId,
                        SeTeId = selection.SeTeId,
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
                        SoOr = selection.SeSoOr,
                        SeStId = selection.SeStId,
                    });
                }

                markets.Add(new InternalMarketMessage
                {
                    PrFiId = message.PrFiId,
                    MaId = market.MaId,
                    MaTeId = market.MaTeId,
                    Li = market.Li,
                    IsHa = market.IsHa,
                    MaStId = market.MaStId,
                    MaStNa = market.MaStNa,
                    CaStId = market.CaStId,
                    MaNa = market.MaNa,
                    SoOr = GetSortOrder(market.MaTeId),
                    ClCo = market.ClCo,
                    ClCoMa = market.ClCoMa,
                    CaStNa = market.CaStNa,
                    PrMaId = market.PrMaId,
                    InPl = market.InPl,
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
                CuPe = message.CuPe,
                CuMi = message.CuMi,
                CuSe = message.CuSe,
                FiTyId = message.FiTyId,
                FiTyNa = ((FixtureType)message.FiTyId).ToString(),
                ToTi = message.ToTi,
                Fa = FavoriteSelection.None.ToString(),
                SoOr = 1,
                ClCo = message.ClCo,
                CoId = correlationId,
                IsSu = message.IsSu,
                HoId = message.HoId,
                AwId = message.AwId,
                StUr = message.StUr,
                Ma = markets
            };
        }

        // Synthesizes the adapter-shaped fixture message from a Mongo document (moved from
        // OE.Sportsbook.Service). Internal for OE.Translation.Service.Tests.
        internal static OnFixtureChangeMessage ToFixtureChangeMessage(Fixture fixture)
        {
            return new OnFixtureChangeMessage
            {
                ChTyId = fixture.ChTyId,
                PrFiId = fixture.PrFiId,
                FiId = fixture.FiId,
                FiSp = fixture.FiSp,
                SpId = fixture.SpId,
                SpNa = fixture.SpNa,
                SegId = fixture.SegId,
                SegNa = fixture.SegNa,
                LeId = fixture.LeId,
                LeNa = fixture.LeNa,
                FiNa = fixture.FiNa,
                FiStDa = fixture.FiStDa,
                FiStId = fixture.FiStId,
                FiStNa = fixture.FiStNa,
                FiTyId = fixture.FiTyId,
                FiTyNa = fixture.FiTyNa,
                Sc = fixture.Sc,
                IsOfLi = fixture.IsOfLi,
                HoReCa = fixture.HoReCa,
                AwReCa = fixture.AwReCa,
                CuPe = fixture.CuPe,
                CuMi = fixture.CuMi,
                CuSe = fixture.CuSe,
                ToTi = fixture.ToTi,
                SoOr = fixture.FiSoOr,
                ClCo = fixture.ClCo,
                IsSu = fixture.IsSu,
                HoId = fixture.HoId,
                AwId = fixture.AwId,
                HoNa = fixture.HoNa,
                AwNa = fixture.AwNa,
                StUr = fixture.StUr,
                Fa = FavoriteSelection.None.ToString()
            };
        }

        private static int GetSortOrder(string id)
        {
            _ = int.TryParse(id, out int result);

            return result;
        }

        private static UpdateDefinition<Fixture> Combine(UpdateDefinition<Fixture>? current, UpdateDefinition<Fixture> next)
        {
            return current == null
                ? next
                : Builders<Fixture>.Update.Combine(current, next);
        }

        #endregion
    }
}
