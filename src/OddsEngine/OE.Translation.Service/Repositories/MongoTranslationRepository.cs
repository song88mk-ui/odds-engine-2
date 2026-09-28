using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
        private readonly IMongoCollection<MarketLookup> _marketLookupCollection;

        // Bounded retry for the update-only *NaTr stamp (see WriteFixtureTranslations/
        // BulkWriteWithSingleRetry remarks): a brand-new document may not exist yet when this
        // write runs, racing OE.Sportsbook.Service's own upsert for the same message. ONE retry
        // after a short delay — the previous 5 x 200ms per-document loop multiplied across the
        // selections of every market message and crawled the consumer under backlog
        // (2026-07-15 prod incident). A message that still misses heals on the next message.
        private const int TR_WRITE_RETRY_ATTEMPTS = 2;
        private static readonly TimeSpan TR_WRITE_RETRY_DELAY = TimeSpan.FromMilliseconds(200);

        // "Active" per docs/BusinessGlossary.md — excludes fixtures that have concluded one way or
        // another; used by ReadActiveSnapshot (SeedActiveSnapshot migration), distinct from
        // _openFixtureStatusIds above (which is scoped to "still shown to players").
        private static readonly List<int> _terminalFixtureStatusIds = new()
        {
            (int)FixtureStatus.Ended,
            (int)FixtureStatus.Closed,
            (int)FixtureStatus.Cancelled,
            (int)FixtureStatus.Abandoned
        };

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
            _marketLookupCollection = db.GetCollection<MarketLookup>("MarketLookup");
        }

        #endregion

        #region Live path — stamp *NaTr onto documents as the raw feed flows

        // Update-only by design: OE.Sportsbook.Service's upsert creates the documents. If this
        // message beats the upsert for a brand-new document, the write matches nothing —
        // UpdateOneWithRetry below retries briefly (~800ms worst case), and a message that still
        // finds nothing after that falls back to the next message for the entity healing it
        // (accepted trade-off of the ownership split; never upserts here — see UpdateOneWithRetry).
        public void WriteFixtureTranslations(OnFixtureChangeMessage message)
        {
            if (message == null)
                return;

            UpdateDefinition<Fixture>? update = null;

            var spTr = _translationCache.GetTranslations($"SpId:{message.SpId}", message.SpNa);
            if (spTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.SpNaTr, spTr.Select(t => new SportTranslationMDB { LaCo = t.LaCo, SpNa = t.Tx }).ToList()));

            var segTr = _translationCache.GetTranslations($"SegId:{message.SegId}", message.SegNa);
            if (segTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.SegNaTr, segTr.Select(t => new SegmentTranslationMDB { LaCo = t.LaCo, SegNa = t.Tx }).ToList()));

            var leTr = _translationCache.GetTranslations($"LeId:{message.LeId}", message.LeNa);
            if (leTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.LeNaTr, leTr.Select(t => new LeagueTranslationMDB { LaCo = t.LaCo, LeNa = t.Tx }).ToList()));

            var hoTr = _translationCache.GetTranslations($"Te:{message.HoId}", message.HoNa);
            if (hoTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.HoNaTr, hoTr.Select(t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx }).ToList()));

            var awTr = _translationCache.GetTranslations($"Te:{message.AwId}", message.AwNa);
            if (awTr != null)
                update = Combine(update, Builders<Fixture>.Update.Set(f => f.AwNaTr, awTr.Select(t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx }).ToList()));

            if (update == null)
                return; // nothing translated for this fixture's keys — no write at all

            UpdateOneWithRetry(_fixtureCollection, Builders<Fixture>.Filter.Eq(f => f.Id, message.Id), update, $"Fixture {message.Id}");
        }

        public void WriteMarketTranslations(OnMarketChangeMessage message)
        {
            if (message?.Ma == null || message.Ma.Count == 0)
                return;

            // Fixture-level keys ride the market message too (same taxonomy fields).
            WriteFixtureTranslations(message);

            // Some feed paths deliver market messages WITHOUT the fixture's team ids — composing a
            // positional token template with them missing stamps the GENERIC text over an already
            // composed name (2026-07-15: the backlog heal composed ~1k selections and the next
            // id-less message reverted them). The fixture document reliably carries HoId/AwId even
            // when its name fields were nulled (AR-16 footprint), so fall back to it; one indexed
            // read, only on the degraded path.
            var hoId = message.HoId;
            var hoNa = message.HoNa;
            var awId = message.AwId;
            var awNa = message.AwNa;

            if (string.IsNullOrEmpty(hoId) || string.IsNullOrEmpty(awId))
            {
                var fixtureDoc = _fixtureCollection
                    .Find(Builders<Fixture>.Filter.Eq(f => f.Id, message.Id))
                    .Project(f => new { f.HoId, f.HoNa, f.AwId, f.AwNa })
                    .FirstOrDefault();

                if (fixtureDoc != null)
                {
                    if (string.IsNullOrEmpty(hoId)) { hoId = fixtureDoc.HoId; hoNa = fixtureDoc.HoNa; }
                    if (string.IsNullOrEmpty(awId)) { awId = fixtureDoc.AwId; awNa = fixtureDoc.AwNa; }
                }
            }

            // One unordered BulkWrite per collection per MESSAGE, not one round-trip per document:
            // per-selection UpdateOneWithRetry throttled the consumer to a fraction of the feed's
            // message rate and starved q.oe.translation (2026-07-15 prod incident) — a market
            // message can carry hundreds of selections. Same writes, same _id filters (indexed),
            // same update-only semantics; see BulkWriteWithSingleRetry for the race handling.
            var marketWrites = new List<WriteModel<Market>>();
            var selectionWrites = new List<WriteModel<Selection>>();

            foreach (var market in message.Ma)
            {
                if (market == null)
                    continue;

                var maTeTr = _translationCache.GetTranslations($"MaTe:{market.MaTeId}", market.MaTeNa);

                // Per-player prop market (GM-528): MaNaTr is COMPOSED from the player's
                // Pl:{CompId} translation and the template translation — never the raw template
                // text, which would collapse the N per-player markets into one indistinguishable
                // translated block (see PlayerMarketNameComposer, shared with the adapter's wire
                // enrichment). Interpolated via the MaTeP:{MaTeId} placeholder row when its
                // translation kept the {player} marker; join format otherwise. Missing inputs ->
                // no stamp; English MaNa (player-inclusive) remains the fallback.
                var maNaTr = market.CompId.HasValue
                    ? PlayerMarketNameComposer.Compose(
                        _translationCache.GetTranslations($"Pl:{market.CompId.Value}", null),
                        maTeTr,
                        _translationCache.GetTranslations($"MaTeP:{market.MaTeId}", null))
                    : maTeTr;

                if (maNaTr != null)
                {
                    marketWrites.Add(new UpdateOneModel<Market>(
                        Builders<Market>.Filter.Eq("Id", market.Id),
                        Builders<Market>.Update.Set(m => m.MaNaTr, maNaTr.Select(t => new MarketTranslationMDB { LaCo = t.LaCo, MaNa = t.Tx }).ToList()))
                    { IsUpsert = false });
                }

                if (market.Se == null)
                    continue;

                foreach (var selection in market.Se)
                {
                    if (selection == null)
                        continue;

                    var seNaTr = ResolveSelectionTranslations(
                        selection.CompId, selection.SeNa, selection.SeTeNa, selection.SeTeId, market.MaTeId, market.IsHa,
                        hoId, hoNa, awId, awNa);

                    if (seNaTr != null)
                    {
                        selectionWrites.Add(new UpdateOneModel<Selection>(
                            Builders<Selection>.Filter.Eq("Id", selection.Id),
                            Builders<Selection>.Update.Set(s => s.SeNaTr, seNaTr.Select(t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx }).ToList()))
                        { IsUpsert = false });
                    }
                }
            }

            BulkWriteWithSingleRetry(_marketCollection, marketWrites, $"markets of fixture {message.FiId}");
            BulkWriteWithSingleRetry(_selectionCollection, selectionWrites, $"selections of fixture {message.FiId}");
        }

        // Update-only stamp for a whole message: one unordered bulk attempt, and — only when some
        // documents matched nothing (the brand-new-document race against OE.Sportsbook.Service's
        // upsert) — ONE short delay and one idempotent re-run of the same bulk. Bounds the race
        // wait to a single TR_WRITE_RETRY_DELAY per MESSAGE instead of the retired
        // per-document retry loop (5 x 200ms per selection), which is what crawled under backlog.
        // A still-unmatched document falls back to the documented heal-on-next-message behavior.
        private void BulkWriteWithSingleRetry<TDoc>(IMongoCollection<TDoc> collection, List<WriteModel<TDoc>> writes, string description)
        {
            if (writes.Count == 0)
                return;

            var result = collection.BulkWrite(writes, new BulkWriteOptions { IsOrdered = false });

            if (result.MatchedCount >= writes.Count)
                return;

            Task.Delay(TR_WRITE_RETRY_DELAY).GetAwaiter().GetResult();

            result = collection.BulkWrite(writes, new BulkWriteOptions { IsOrdered = false });

            if (result.MatchedCount < writes.Count)
            {
                _logger.LogWarning(
                    "Translation stamp for {Description}: {Missing} of {Total} documents not found — will heal on the next message",
                    description, writes.Count - result.MatchedCount, writes.Count);
            }
        }

        // Single lookup path for a selection's SeNaTr: which TranslationCache2 key backs it (see
        // OE.Shared.Utilities.SelectionTranslationKeyResolver), then positional token
        // personalization — a "Home or Draw"-style template gets this fixture's translated team
        // names spliced over the shared template text (see SelectionTranslationComposer). Must stay
        // behaviorally identical to OE.Adapter.Service.TradingRepository.GetSelectionTranslations
        // (the live message enrichment) or Mongo-served REST text diverges from websocket text.
        private List<TranslationMessage>? ResolveSelectionTranslations(
            int? compId, string seNa, string seTeNa, string seTeId, string maTeId, bool isHa,
            string hoId, string hoNa, string awId, string awNa)
        {
            var key = SelectionTranslationKeyResolver.Resolve(compId, seNa, seTeNa, seTeId, maTeId, isHa, hoId, hoNa, awId, awNa);

            var seNaTr = _translationCache.GetTranslations(key.StringId, key.SourceText);

            if (seNaTr != null && SelectionTranslationComposer.RequiresTeamTokenComposition(key.StringId, seTeNa))
            {
                seNaTr = SelectionTranslationComposer.ComposeTeamTokens(
                    seNaTr,
                    seTeNa,
                    _translationCache.GetTranslations($"Te:{hoId}", hoNa),
                    _translationCache.GetTranslations($"Te:{awId}", awNa));
            }

            return seNaTr;
        }

        // Writes the update-only *NaTr stamp, retrying briefly on a zero-match result instead of
        // relying solely on an unrelated future message to heal a lost race against
        // OE.Sportsbook.Service's own upsert for a brand-new document (see class remarks above).
        // Still never upserts — a permanently-unmatched write just falls back to today's
        // heal-on-next-message behavior, so this can only shrink the untranslated window, never
        // create a malformed document.
        private bool UpdateOneWithRetry<TDoc>(IMongoCollection<TDoc> collection, FilterDefinition<TDoc> filter, UpdateDefinition<TDoc> update, string entityDescription)
        {
            var matched = RetryUntilMatched(
                attempt: () => collection.UpdateOne(filter, update, new UpdateOptions { IsUpsert = false }).MatchedCount > 0,
                delay: d => Task.Delay(d).GetAwaiter().GetResult(),
                TR_WRITE_RETRY_ATTEMPTS,
                TR_WRITE_RETRY_DELAY);

            if (!matched)
            {
                _logger.LogWarning(
                    "Translation stamp for {Entity} found no matching document after {Attempts} retries — will heal on the next message for this entity",
                    entityDescription, TR_WRITE_RETRY_ATTEMPTS);
            }

            return matched;
        }

        // Pure retry loop, decoupled from Mongo so it's unit-testable without a live collection
        // (this class otherwise constructs its own MongoClient in the ctor — not DI-friendly).
        // Calls attempt() up to maxAttempts times, invoking delay() between tries, stopping as
        // soon as attempt() reports a match; never delays after the final attempt.
        internal static bool RetryUntilMatched(Func<bool> attempt, Action<TimeSpan> delay, int maxAttempts, TimeSpan delayBetweenAttempts)
        {
            for (var i = 1; i <= maxAttempts; i++)
            {
                if (attempt())
                    return true;

                if (i < maxAttempts)
                    delay(delayBetweenAttempts);
            }

            return false;
        }

        #endregion

        #region One-time migration — active snapshot for SeedActiveSnapshot

        // Every currently-active sport/segment/league/team/market-template/selection-template name,
        // deduped by STRING_ID (first value wins — these are recurring entities, so any active
        // document carries the same name). Read-only; TranslationRepository.SeedActiveSnapshotAsync
        // decides what (if anything) gets written to TranslationCache2.
        public List<(string Id, string Text)> ReadActiveSnapshot()
        {
            var result = new Dictionary<string, string>();

            void AddIfNew(string id, string text)
            {
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(text) && !result.ContainsKey(id))
                    result[id] = text;
            }

            var activeFixtureFilter = Builders<Fixture>.Filter.Nin(f => f.FiStId, _terminalFixtureStatusIds);

            var fixtures = _fixtureCollection
                .Find(activeFixtureFilter)
                .Project(f => new { f.FiId, f.SpId, f.SpNa, f.SegId, f.SegNa, f.LeId, f.LeNa, f.HoId, f.HoNa, f.AwId, f.AwNa })
                .ToList();

            foreach (var f in fixtures)
            {
                AddIfNew($"SpId:{f.SpId}", f.SpNa);
                AddIfNew($"SegId:{f.SegId}", f.SegNa);
                AddIfNew($"LeId:{f.LeId}", f.LeNa);
                AddIfNew($"Te:{f.HoId}", f.HoNa);
                AddIfNew($"Te:{f.AwId}", f.AwNa);
            }

            // SelectionTranslationKeyResolver needs the fixture's HoId/HoNa/AwId/AwNa (same reason
            // as BackfillSelections below) — build the lookup from the fixtures already fetched
            // above instead of a second query.
            var fixturesById = fixtures.ToDictionary(f => f.FiId);

            var activeMaTeIds = _marketCollection
                .Find(Builders<Market>.Filter.And(
                    Builders<Market>.Filter.Eq(m => m.MaStId, (int)MarketStatus.Active),
                    Builders<Market>.Filter.Nin(m => m.FiStId, _terminalFixtureStatusIds)))
                .Project(m => m.MaTeId)
                .ToList()
                .Distinct()
                .ToList();

            var marketTemplates = _marketLookupCollection
                .Find(Builders<MarketLookup>.Filter.In(ml => ml.MaTeId, activeMaTeIds))
                .Project(ml => new { ml.MaTeId, ml.MaTeNa })
                .ToList();

            foreach (var mt in marketTemplates)
                AddIfNew($"MaTe:{mt.MaTeId}", mt.MaTeNa);

            var selections = _selectionCollection
                .Find(Builders<Selection>.Filter.And(
                    Builders<Selection>.Filter.Eq(s => s.IsAc, true),
                    Builders<Selection>.Filter.Eq(s => s.MaStId, (int)MarketStatus.Active),
                    Builders<Selection>.Filter.Nin(s => s.FiStId, _terminalFixtureStatusIds)))
                .Project(s => new { s.FiId, s.MaId, s.MaTeId, s.SeTeId, s.SeTeNa, s.CompId, s.SeNa })
                .ToList();

            var selectionMaIds = selections.Select(s => s.MaId).Distinct().ToList();
            var isHaByMaId = _marketCollection
                .Find(Builders<Market>.Filter.In(m => m.MaId, selectionMaIds))
                .Project(m => new { m.MaId, m.IsHa })
                .ToList()
                .ToDictionary(m => m.MaId, m => m.IsHa);

            // Which TranslationCache2 key backs each selection — see
            // OE.Shared.Utilities.SelectionTranslationKeyResolver (shared with
            // OnProcessMarketSetUpdate's live seeding and TradingRepository's live enrichment). This
            // used to be a hand-duplicated "CompId != null -> Co: else SeTe:" branch here that never
            // went through the shared resolver, so it never got the CompId-matches-participant /
            // SeTeNa-starts-with-team-name redirects — a real production bug (this startup sweep
            // re-seeded Co:{CompId} rows with fixture-specific fused "team + score" text on every
            // service restart, undoing the live-path fix).
            foreach (var s in selections)
            {
                if (!fixturesById.TryGetValue(s.FiId, out var fixture))
                    continue; // no active fixture found — nothing to resolve Te:/positional keys against

                var isHa = isHaByMaId.TryGetValue(s.MaId, out var marketIsHa) && marketIsHa;

                var key = SelectionTranslationKeyResolver.Resolve(
                    s.CompId, s.SeNa, s.SeTeNa, s.SeTeId, s.MaTeId, isHa,
                    fixture.HoId, fixture.HoNa, fixture.AwId, fixture.AwNa);

                AddIfNew(key.StringId, key.SourceText);
            }

            return result.Select(kv => (kv.Key, kv.Value)).ToList();
        }

        #endregion

        #region One-time migration — Mongo backfill for BackfillActiveSnapshot

        // Insert-missing-only, same scope decision as SeedActiveSnapshotAsync: only stamps a *NaTr
        // that's currently null/empty. Fixing an already-stamped-but-stale value (e.g. a wording
        // change) is the live ON_TRANSLATION_CHANGE path's job, not this startup sweep's.
        public void BackfillActiveSnapshot()
        {
            var fixturesStamped = BackfillFixtures();
            var marketsStamped = BackfillMarkets();
            var selectionsStamped = BackfillSelections();

            _logger.LogInformation(
                "BackfillActiveSnapshot: stamped {FixtureCount} fixtures, {MarketCount} markets, {SelectionCount} selections",
                fixturesStamped, marketsStamped, selectionsStamped);
        }

        private int BackfillFixtures()
        {
            var activeFixtureFilter = Builders<Fixture>.Filter.Nin(f => f.FiStId, _terminalFixtureStatusIds);
            var fixtures = _fixtureCollection.Find(activeFixtureFilter).ToList();

            var writes = new List<WriteModel<Fixture>>();

            foreach (var fixture in fixtures)
            {
                UpdateDefinition<Fixture>? update = null;

                if (IsMissing(fixture.SpNaTr))
                {
                    var spTr = _translationCache.GetTranslations($"SpId:{fixture.SpId}", fixture.SpNa);
                    if (spTr != null)
                        update = Combine(update, Builders<Fixture>.Update.Set(f => f.SpNaTr, spTr.Select(t => new SportTranslationMDB { LaCo = t.LaCo, SpNa = t.Tx }).ToList()));
                }

                if (IsMissing(fixture.SegNaTr))
                {
                    var segTr = _translationCache.GetTranslations($"SegId:{fixture.SegId}", fixture.SegNa);
                    if (segTr != null)
                        update = Combine(update, Builders<Fixture>.Update.Set(f => f.SegNaTr, segTr.Select(t => new SegmentTranslationMDB { LaCo = t.LaCo, SegNa = t.Tx }).ToList()));
                }

                if (IsMissing(fixture.LeNaTr))
                {
                    var leTr = _translationCache.GetTranslations($"LeId:{fixture.LeId}", fixture.LeNa);
                    if (leTr != null)
                        update = Combine(update, Builders<Fixture>.Update.Set(f => f.LeNaTr, leTr.Select(t => new LeagueTranslationMDB { LaCo = t.LaCo, LeNa = t.Tx }).ToList()));
                }

                if (IsMissing(fixture.HoNaTr))
                {
                    var hoTr = _translationCache.GetTranslations($"Te:{fixture.HoId}", fixture.HoNa);
                    if (hoTr != null)
                        update = Combine(update, Builders<Fixture>.Update.Set(f => f.HoNaTr, hoTr.Select(t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx }).ToList()));
                }

                if (IsMissing(fixture.AwNaTr))
                {
                    var awTr = _translationCache.GetTranslations($"Te:{fixture.AwId}", fixture.AwNa);
                    if (awTr != null)
                        update = Combine(update, Builders<Fixture>.Update.Set(f => f.AwNaTr, awTr.Select(t => new TeamTranslationMDB { LaCo = t.LaCo, TeNa = t.Tx }).ToList()));
                }

                if (update != null)
                    writes.Add(new UpdateOneModel<Fixture>(Builders<Fixture>.Filter.Eq(f => f.Id, fixture.Id), update) { IsUpsert = false });
            }

            if (writes.Count > 0)
                _fixtureCollection.BulkWrite(writes, new BulkWriteOptions { IsOrdered = false });

            return writes.Count;
        }

        private int BackfillMarkets()
        {
            var activeMarketFilter = Builders<Market>.Filter.And(
                Builders<Market>.Filter.Eq(m => m.MaStId, (int)MarketStatus.Active),
                Builders<Market>.Filter.Nin(m => m.FiStId, _terminalFixtureStatusIds));

            var markets = _marketCollection.Find(activeMarketFilter).ToList();
            var candidates = markets.Where(m => IsMissing(m.MaNaTr)).ToList();

            if (candidates.Count == 0)
                return 0;

            var writes = new List<WriteModel<Market>>();

            foreach (var market in candidates)
            {
                // The market document's own MaNa is the wording THIS market displays — the feed
                // emits concurrent wordings per template id, so MarketLookup's single name can
                // belong to a different variant. With the doc's wording the cache resolves the
                // right row: the primary by key, other wordings via the content-addressed index,
                // and the last-known fallback otherwise. EXCEPT player markets (GM-528): their
                // MaNa is the player-inclusive name, never a template wording — pass null so the
                // primary template row is served by key, then compose with the Pl: translation.
                var maNaTr = market.CompId.HasValue
                    ? PlayerMarketNameComposer.Compose(
                        _translationCache.GetTranslations($"Pl:{market.CompId.Value}", null),
                        _translationCache.GetTranslations($"MaTe:{market.MaTeId}", null),
                        _translationCache.GetTranslations($"MaTeP:{market.MaTeId}", null))
                    : _translationCache.GetTranslations($"MaTe:{market.MaTeId}", market.MaNa);

                if (maNaTr != null)
                {
                    writes.Add(new UpdateOneModel<Market>(
                        Builders<Market>.Filter.Eq(m => m.Id, market.Id),
                        Builders<Market>.Update.Set(m => m.MaNaTr, maNaTr.Select(t => new MarketTranslationMDB { LaCo = t.LaCo, MaNa = t.Tx }).ToList()))
                    { IsUpsert = false });
                }
            }

            if (writes.Count > 0)
                _marketCollection.BulkWrite(writes, new BulkWriteOptions { IsOrdered = false });

            return writes.Count;
        }

        private int BackfillSelections()
        {
            var activeSelectionFilter = Builders<Selection>.Filter.And(
                Builders<Selection>.Filter.Eq(s => s.IsAc, true),
                Builders<Selection>.Filter.Eq(s => s.MaStId, (int)MarketStatus.Active),
                Builders<Selection>.Filter.Nin(s => s.FiStId, _terminalFixtureStatusIds));

            var selections = _selectionCollection.Find(activeSelectionFilter).ToList();
            var candidates = selections.Where(s => IsMissing(s.SeNaTr)).ToList();

            if (candidates.Count == 0)
                return 0;

            // SelectionTranslationKeyResolver needs the fixture's HoId/HoNa/AwId/AwNa (the
            // CompId-null positional-template exception and the IsHa+CompId==HoId/AwId exception)
            // and the market's IsHa flag — neither lives on the Selection document itself.
            var fiIds = candidates.Select(s => s.FiId).Distinct().ToList();
            var fixturesById = _fixtureCollection
                .Find(Builders<Fixture>.Filter.In(f => f.FiId, fiIds))
                .Project(f => new { f.FiId, f.HoId, f.HoNa, f.AwId, f.AwNa })
                .ToList()
                .ToDictionary(f => f.FiId);

            var maIds = candidates.Select(s => s.MaId).Distinct().ToList();
            var isHaByMaId = _marketCollection
                .Find(Builders<Market>.Filter.In(m => m.MaId, maIds))
                .Project(m => new { m.MaId, m.IsHa })
                .ToList()
                .ToDictionary(m => m.MaId, m => m.IsHa);

            var writes = new List<WriteModel<Selection>>();

            foreach (var selection in candidates)
            {
                if (!fixturesById.TryGetValue(selection.FiId, out var fixture))
                    continue; // no fixture found — nothing to resolve Te:/positional keys against

                var isHa = isHaByMaId.TryGetValue(selection.MaId, out var marketIsHa) && marketIsHa;

                // Which TranslationCache2 key backs SeNaTr, plus positional token
                // personalization — see ResolveSelectionTranslations (shared with the live stamp).
                var seNaTr = ResolveSelectionTranslations(
                    selection.CompId, selection.SeNa, selection.SeTeNa, selection.SeTeId, selection.MaTeId, isHa,
                    fixture.HoId, fixture.HoNa, fixture.AwId, fixture.AwNa);

                if (seNaTr != null)
                {
                    writes.Add(new UpdateOneModel<Selection>(
                        Builders<Selection>.Filter.Eq(s => s.Id, selection.Id),
                        Builders<Selection>.Update.Set(s => s.SeNaTr, seNaTr.Select(t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx }).ToList()))
                    { IsUpsert = false });
                }
            }

            if (writes.Count > 0)
                _selectionCollection.BulkWrite(writes, new BulkWriteOptions { IsOrdered = false });

            return writes.Count;
        }

        private static bool IsMissing<T>(List<T>? translations) => translations == null || translations.Count == 0;

        // A team-name edit (Te:{teamId}) only ever touched Fixture.HoNaTr/AwNaTr — competitor-name
        // selections (e.g. a Match Winner market whose selections literally are the two teams)
        // resolve to this SAME Te: key per SelectionTranslationKeyResolver (CompId matches one of
        // the fixture's own participants), but their Selection.SeNaTr was never recomputed, so it
        // sat stale until the next unrelated odds/market update happened to rebuild it. Mirrors
        // BackfillSelections' resolver+cache pattern, scoped to just this team instead of a sweep.
        // Also covers CompId-null positional token selections ("Home or Draw") on this team's
        // fixtures: their composed SeNaTr embeds the team's translated name (see
        // SelectionTranslationComposer), so a team edit must recompose them too.
        private int CascadeTeamTranslationToSelections(string teamId)
        {
            if (!int.TryParse(teamId, out var compId))
                return 0;

            var openMarketStatusIds = new[] { (int)MarketStatus.Active, (int)MarketStatus.Suspended };

            var teamFixtureIds = _fixtureCollection
                .Find(Builders<Fixture>.Filter.And(
                    Builders<Fixture>.Filter.Or(
                        Builders<Fixture>.Filter.Eq(f => f.HoId, teamId),
                        Builders<Fixture>.Filter.Eq(f => f.AwId, teamId)),
                    Builders<Fixture>.Filter.Nin(f => f.FiStId, _terminalFixtureStatusIds)))
                .Project(f => f.FiId)
                .ToList();

            // Active + Suspended, matching PublishTranslationOnlyOddsChanges below — a suspended
            // market is still shown to players (just not bettable), so its selection name should
            // stay correct too, not just Active ones. FiId leads the filter so the whole query
            // rides the (FiId, MaStId, MaTeId) index — an unprefixed CompId equality was a full
            // collection scan (2026-07-15 incident class). Scope note: this bounds the cascade to
            // the team's own open fixtures; a competitor selection referencing the team on some
            // OTHER fixture (e.g. an outright) refreshes on that fixture's next market message
            // instead.
            var affectedSelectionFilter = Builders<Selection>.Filter.And(
                Builders<Selection>.Filter.In(s => s.FiId, teamFixtureIds),
                Builders<Selection>.Filter.In(s => s.MaStId, openMarketStatusIds),
                Builders<Selection>.Filter.Or(
                    Builders<Selection>.Filter.Eq(s => s.CompId, compId),
                    Builders<Selection>.Filter.And(
                        Builders<Selection>.Filter.Eq(s => s.CompId, null),
                        Builders<Selection>.Filter.Regex(s => s.SeTeNa, new BsonRegularExpression(SelectionTranslationComposer.PositionalWordPattern, "i")))));

            var selections = _selectionCollection.Find(affectedSelectionFilter).ToList();

            if (selections.Count == 0)
                return 0;

            var fiIds = selections.Select(s => s.FiId).Distinct().ToList();
            var fixturesById = _fixtureCollection
                .Find(Builders<Fixture>.Filter.In(f => f.FiId, fiIds))
                .Project(f => new { f.FiId, f.HoId, f.HoNa, f.AwId, f.AwNa })
                .ToList()
                .ToDictionary(f => f.FiId);

            var maIds = selections.Select(s => s.MaId).Distinct().ToList();
            var isHaByMaId = _marketCollection
                .Find(Builders<Market>.Filter.In(m => m.MaId, maIds))
                .Project(m => new { m.MaId, m.IsHa })
                .ToList()
                .ToDictionary(m => m.MaId, m => m.IsHa);

            var writes = new List<WriteModel<Selection>>();

            foreach (var selection in selections)
            {
                if (!fixturesById.TryGetValue(selection.FiId, out var fixture))
                    continue; // no fixture found — nothing to resolve Te: against

                var isHa = isHaByMaId.TryGetValue(selection.MaId, out var marketIsHa) && marketIsHa;

                var seNaTr = ResolveSelectionTranslations(
                    selection.CompId, selection.SeNa, selection.SeTeNa, selection.SeTeId, selection.MaTeId, isHa,
                    fixture.HoId, fixture.HoNa, fixture.AwId, fixture.AwNa);

                if (seNaTr != null)
                {
                    writes.Add(new UpdateOneModel<Selection>(
                        Builders<Selection>.Filter.Eq(s => s.Id, selection.Id),
                        Builders<Selection>.Update.Set(s => s.SeNaTr, seNaTr.Select(t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx }).ToList()))
                    { IsUpsert = false });
                }
            }

            if (writes.Count > 0)
                _selectionCollection.BulkWrite(writes, new BulkWriteOptions { IsOrdered = false });

            return writes.Count;
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

            var (deltasByKey, sourceTextByKey) = GroupDeltas(message.Tr);

            if (deltasByKey.Count == 0)
                return;

            // Open-fixture scope for every Market/Selection write below, chosen for two reasons:
            // (1) semantics — names on concluded fixtures stay as they stood at closure (bet
            // history reads them; same freeze rule the token-template path already applies), and
            // only Active/Suspended markets are player-visible; (2) performance — prefixing the
            // Selection/Market filters with FiId IN (open fixtures) + MaStId lets them ride the
            // existing (FiId, MaStId, MaTeId) index instead of scanning the full multi-million-
            // document collections, which is what ground a ~100-key change for hours and piled up
            // q.oe.translation (2026-07-15 prod incident). The Fixture collection itself is small
            // (cleanup-bounded) and keeps unbounded updates.
            var openFixtureIds = _fixtureCollection
                .Find(Builders<Fixture>.Filter.Nin(f => f.FiStId, _terminalFixtureStatusIds))
                .Project(f => f.FiId)
                .ToList();

            var openMarketStatusIds = new[] { (int)MarketStatus.Active, (int)MarketStatus.Suspended };

            var stopwatch = Stopwatch.StartNew();
            _logger.LogInformationEx($"OnTranslationChange: applying {deltasByKey.Count} changed keys across {openFixtureIds.Count} open fixtures");

            var sportDeltas = new Dictionary<int, List<TranslationMessage>>();
            var segmentDeltas = new Dictionary<int, List<TranslationMessage>>();
            var leagueDeltas = new Dictionary<int, List<TranslationMessage>>();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>>();
            // Keyed by BASE template id; each entry is (SrcTx, deltas) — SrcTx null means the
            // announce wasn't wording-scoped (older senders) and applies template-wide.
            var marketTemplateDeltas = new Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>>();
            var selectionTemplateDeltas = new Dictionary<(string MaTeId, string SeTeId), List<TranslationMessage>>();
            var competitorDeltas = new Dictionary<int, List<TranslationMessage>>();
            var playerDeltas = new Dictionary<int, List<TranslationMessage>>();
            var playerTemplateDeltas = new Dictionary<string, List<TranslationMessage>>();

            var appliedKeys = 0;

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
                        CascadeTeamTranslationToSelections(id);
                        break;
                    case "MaTe":
                        // May be a wording-variant id ("259~a1b2c3d4", see MarketTemplateVariantId)
                        // — strip to the base template id. When the announce carries SrcTx, the
                        // update applies only to markets whose MaNa is that wording (the feed
                        // emits concurrent wordings per template id); without it, template-wide.
                        sourceTextByKey.TryGetValue(entry.Key, out var maTeSourceText);
                        var baseMaTeId = AddMarketTemplateDelta(marketTemplateDeltas, id, maTeSourceText, entry.Value);

                        // The raw-text update applies to REGULAR markets only: a player market's
                        // MaNaTr is COMPOSED (Pl: + MaTe:, GM-528), so raw template text would
                        // stomp the composed name — and a player market's MaNa (player-inclusive)
                        // never equals a template wording, so a wording-scoped announce could not
                        // reach it anyway. Player markets are recomposed below instead.
                        var maTeFilter = BuildMaTeMarketFilter(openFixtureIds, openMarketStatusIds, baseMaTeId, maTeSourceText);

                        UpdateTranslationArray(_marketCollection, maTeFilter, "MaNaTr", entry.Value, t => new MarketTranslationMDB { LaCo = t.LaCo, MaNa = t.Tx });
                        RecomposePlayerMarketNames(Builders<Market>.Filter.And(
                            Builders<Market>.Filter.In(m => m.FiId, openFixtureIds),
                            Builders<Market>.Filter.In(m => m.MaStId, openMarketStatusIds),
                            Builders<Market>.Filter.Eq(m => m.MaTeId, baseMaTeId),
                            Builders<Market>.Filter.Ne(m => m.CompId, null)));
                        break;
                    case "Pl" when int.TryParse(id, out var plId):
                        // Player-name edit (GM-528): recompose MaNaTr on that player's open
                        // markets. The cache was Apply'd before this path runs (Worker order), so
                        // the compose reads the fully merged Pl:/MaTe: state, not just the delta.
                        playerDeltas[plId] = entry.Value;
                        RecomposePlayerMarketNames(Builders<Market>.Filter.And(
                            Builders<Market>.Filter.In(m => m.FiId, openFixtureIds),
                            Builders<Market>.Filter.In(m => m.MaStId, openMarketStatusIds),
                            Builders<Market>.Filter.Eq(m => m.CompId, plId)));
                        break;
                    case "MaTeP":
                        // Placeholder-template edit (GM-528 interpolation, e.g. a backoffice
                        // translation of "1st Innings {player} to Reach 50 Runs Yes/No"):
                        // recompose every open player market of that template — interpolation
                        // takes precedence over the join format inside the composer.
                        playerTemplateDeltas[id] = entry.Value;
                        RecomposePlayerMarketNames(Builders<Market>.Filter.And(
                            Builders<Market>.Filter.In(m => m.FiId, openFixtureIds),
                            Builders<Market>.Filter.In(m => m.MaStId, openMarketStatusIds),
                            Builders<Market>.Filter.Eq(m => m.MaTeId, id),
                            Builders<Market>.Filter.Ne(m => m.CompId, null)));
                        break;
                    case "Co" when int.TryParse(id, out var compId):
                        // Competitor-specific selection name (as-is, not a template) — mutually
                        // exclusive with SeTe: below, see docs/BusinessRules.md.
                        competitorDeltas[compId] = entry.Value;
                        UpdateTranslationArray(
                            _selectionCollection,
                            Builders<Selection>.Filter.And(
                                Builders<Selection>.Filter.In(s => s.FiId, openFixtureIds),
                                Builders<Selection>.Filter.In(s => s.MaStId, openMarketStatusIds),
                                Builders<Selection>.Filter.Eq(s => s.CompId, compId)),
                            "SeNaTr", entry.Value, t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx });
                        break;
                    case "SeTe":
                        // "SeTe:{MaTeId}:{SeTeId}" — SeTeId is the segment after the LAST colon.
                        var lastSeparator = id.LastIndexOf(':');

                        if (lastSeparator <= 0 || lastSeparator == id.Length - 1)
                            break;

                        var maTeId = id.Substring(0, lastSeparator);
                        var seTeId = id.Substring(lastSeparator + 1);

                        selectionTemplateDeltas[(maTeId, seTeId)] = entry.Value;
                        UpdateSelectionTemplateTranslations(maTeId, seTeId, entry.Value, openFixtureIds, openMarketStatusIds);
                        break;
                    default:
                        _logger.LogWarningEx($"OnTranslationChange: unrecognized STRING_ID format '{entry.Key}'");
                        break;
                }

                appliedKeys++;

                // Progress heartbeat for LARGE changes only (script announces, mass edits) — the
                // 2026-07-15 grind was invisible precisely because nothing logged between start
                // and the 30-minute broker timeout.
                if (deltasByKey.Count > 20 && appliedKeys % 20 == 0)
                    _logger.LogInformationEx($"OnTranslationChange: {appliedKeys}/{deltasByKey.Count} keys applied ({stopwatch.ElapsedMilliseconds}ms elapsed)");
            }

            PublishTranslationOnlyOddsChanges(messageBroker, correlationId,
                sportDeltas, segmentDeltas, leagueDeltas, teamDeltas, marketTemplateDeltas, selectionTemplateDeltas, competitorDeltas, playerDeltas, playerTemplateDeltas, openFixtureIds);

            _logger.LogInformationEx($"OnTranslationChange: completed {appliedKeys} keys in {stopwatch.ElapsedMilliseconds}ms");
        }

        // A SeTe: row is fixture-independent, but positional token templates ("Home or Draw")
        // stamp a COMPOSED SeNaTr that embeds each fixture's own translated team names (see
        // SelectionTranslationComposer) — one blanket UpdateMany with the raw template text would
        // stomp every fixture's composed name. Split the population (all of it scoped by the
        // caller to open fixtures + Active/Suspended markets, which both freezes concluded
        // fixtures' names for bet history AND rides the (FiId, MaStId, MaTeId) index):
        //  - selections that can't be token-composed (CompId-carrying or non-token SeTeNa) keep
        //    the blanket update: the raw delta text applies to them all identically;
        //  - token selections are recomputed per fixture, composing the delta text with each
        //    fixture's Te: translations — same rule as the stamping side.
        private void UpdateSelectionTemplateTranslations(string maTeId, string seTeId, List<TranslationMessage> deltas, List<long> openFixtureIds, int[] openMarketStatusIds)
        {
            var templatePair = Builders<Selection>.Filter.And(
                Builders<Selection>.Filter.In(s => s.FiId, openFixtureIds),
                Builders<Selection>.Filter.In(s => s.MaStId, openMarketStatusIds),
                Builders<Selection>.Filter.Eq(s => s.MaTeId, maTeId),
                Builders<Selection>.Filter.Eq(s => s.SeTeId, seTeId));

            var tokenSelectionFilter = Builders<Selection>.Filter.And(
                Builders<Selection>.Filter.Eq(s => s.CompId, null),
                Builders<Selection>.Filter.Regex(s => s.SeTeNa, new BsonRegularExpression(SelectionTranslationComposer.PositionalWordPattern, "i")));

            UpdateTranslationArray(
                _selectionCollection,
                Builders<Selection>.Filter.And(templatePair, Builders<Selection>.Filter.Not(tokenSelectionFilter)),
                "SeNaTr", deltas, t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx });

            var tokenSelections = _selectionCollection
                .Find(Builders<Selection>.Filter.And(templatePair, tokenSelectionFilter))
                .Project(s => new { s.Id, s.FiId, s.SeTeNa })
                .ToList();

            if (tokenSelections.Count == 0)
                return;

            var fiIds = tokenSelections.Select(s => s.FiId).Distinct().ToList();
            var fixturesById = _fixtureCollection
                .Find(Builders<Fixture>.Filter.In(f => f.FiId, fiIds))
                .Project(f => new { f.FiId, f.HoId, f.HoNa, f.AwId, f.AwNa })
                .ToList()
                .ToDictionary(f => f.FiId);

            foreach (var selection in tokenSelections)
            {
                if (!fixturesById.TryGetValue(selection.FiId, out var fixture))
                    continue; // no fixture to compose against — heals on the next message

                // Compose over the DELTA (not a cache re-read): an empty Tx entry must survive as
                // a removal marker so that language is pulled, mirroring the blanket path.
                var composed = SelectionTranslationComposer.ComposeTeamTokens(
                    deltas,
                    selection.SeTeNa,
                    _translationCache.GetTranslations($"Te:{fixture.HoId}", fixture.HoNa),
                    _translationCache.GetTranslations($"Te:{fixture.AwId}", fixture.AwNa)) ?? deltas;

                UpdateTranslationArray(
                    _selectionCollection,
                    Builders<Selection>.Filter.Eq(s => s.Id, selection.Id),
                    "SeNaTr", composed, t => new SelectionTranslationMDB { LaCo = t.LaCo, SeNa = t.Tx });
            }
        }

        #endregion

        #region Utilities

        // Per-language upsert into a translation array (SpNaTr/SegNaTr/LeNaTr/HoNaTr/AwNaTr/MaNaTr/
        // SeNaTr): pull the language entry, push the replacement (skip the push on removal).
        // Two-step because Mongo rejects pull+push on the same field in one update; the *Tr MDB
        // items carry [BsonId] on LaCo, hence the "_id" element filter.
        // Recomposes MaNaTr for the player markets matched by the caller's (open-scoped) filter,
        // after a Pl: or MaTe: delta. Both sides are read from the cache, which the Worker
        // Apply'd before this path runs, so a partial-language delta still composes the full
        // merged translation set. Compose failure (either side missing) leaves the stored MaNaTr
        // untouched rather than degrading it to raw template text.
        private void RecomposePlayerMarketNames(FilterDefinition<Market> filter)
        {
            var markets = _marketCollection.Find(filter)
                .Project(m => new { m.Id, m.MaTeId, m.CompId })
                .ToList();

            if (markets.Count == 0)
                return;

            var templateTrByMaTeId = new Dictionary<string, List<TranslationMessage>>();
            var placeholderTrByMaTeId = new Dictionary<string, List<TranslationMessage>>();
            var playerTrByCompId = new Dictionary<int, List<TranslationMessage>>();
            var writes = new List<WriteModel<Market>>();

            foreach (var market in markets)
            {
                if (market.CompId == null || market.MaTeId == null)
                    continue;

                if (!templateTrByMaTeId.TryGetValue(market.MaTeId, out var maTeTr))
                    templateTrByMaTeId[market.MaTeId] = maTeTr = _translationCache.GetTranslations($"MaTe:{market.MaTeId}", null);

                if (!playerTrByCompId.TryGetValue(market.CompId.Value, out var plTr))
                    playerTrByCompId[market.CompId.Value] = plTr = _translationCache.GetTranslations($"Pl:{market.CompId.Value}", null);

                if (!placeholderTrByMaTeId.TryGetValue(market.MaTeId, out var maTePTr))
                    placeholderTrByMaTeId[market.MaTeId] = maTePTr = _translationCache.GetTranslations($"MaTeP:{market.MaTeId}", null);

                var maNaTr = PlayerMarketNameComposer.Compose(plTr, maTeTr, maTePTr);

                if (maNaTr == null)
                    continue;

                writes.Add(new UpdateOneModel<Market>(
                    Builders<Market>.Filter.Eq(m => m.Id, market.Id),
                    Builders<Market>.Update.Set(m => m.MaNaTr, maNaTr.Select(t => new MarketTranslationMDB { LaCo = t.LaCo, MaNa = t.Tx }).ToList()))
                { IsUpsert = false });
            }

            if (writes.Count > 0)
                _marketCollection.BulkWrite(writes, new BulkWriteOptions { IsOrdered = false });
        }

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
            Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>> marketTemplateDeltas,
            Dictionary<(string MaTeId, string SeTeId), List<TranslationMessage>> selectionTemplateDeltas,
            Dictionary<int, List<TranslationMessage>> competitorDeltas,
            Dictionary<int, List<TranslationMessage>> playerDeltas,
            Dictionary<string, List<TranslationMessage>> playerTemplateDeltas,
            List<long> openFixtureIds)
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

            if (playerDeltas.Count > 0)
            {
                var playerCompIds = playerDeltas.Keys.Select(c => (int?)c).ToList();

                var fixtureIds = _marketCollection
                    .Find(Builders<Market>.Filter.And(
                        Builders<Market>.Filter.In(m => m.FiId, openFixtureIds),
                        Builders<Market>.Filter.In(m => m.MaStId, marketStatusIds),
                        Builders<Market>.Filter.In(m => m.CompId, playerCompIds)))
                    .Project(m => m.FiId)
                    .ToList()
                    .Distinct()
                    .ToList();

                if (fixtureIds.Count > 0)
                    fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.FiId, fixtureIds));
            }

            if (playerTemplateDeltas.Count > 0)
            {
                var fixtureIds = _marketCollection
                    .Find(Builders<Market>.Filter.And(
                        Builders<Market>.Filter.In(m => m.FiId, openFixtureIds),
                        Builders<Market>.Filter.In(m => m.MaStId, marketStatusIds),
                        Builders<Market>.Filter.In(m => m.MaTeId, playerTemplateDeltas.Keys),
                        Builders<Market>.Filter.Ne(m => m.CompId, null)))
                    .Project(m => m.FiId)
                    .ToList()
                    .Distinct()
                    .ToList();

                if (fixtureIds.Count > 0)
                    fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.FiId, fixtureIds));
            }

            // All three discovery queries lead with FiId IN (open fixtures) + MaStId so they ride
            // the (FiId, MaStId, MaTeId) index — only open fixtures get notified anyway (the
            // affectedFilter below intersects _openFixtureStatusIds), so nothing is lost by not
            // discovering concluded ones.
            if (marketTemplateDeltas.Count > 0)
            {
                var maTeIds = marketTemplateDeltas.Keys.ToList();

                var fixtureIds = _marketCollection
                    .Find(Builders<Market>.Filter.And(
                        Builders<Market>.Filter.In(m => m.FiId, openFixtureIds),
                        Builders<Market>.Filter.In(m => m.MaStId, marketStatusIds),
                        Builders<Market>.Filter.In(m => m.MaTeId, maTeIds)))
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
                    .Find(Builders<Selection>.Filter.And(
                        Builders<Selection>.Filter.In(s => s.FiId, openFixtureIds),
                        Builders<Selection>.Filter.In(s => s.MaStId, marketStatusIds),
                        Builders<Selection>.Filter.Or(pairFilters)))
                    .Project(s => s.FiId)
                    .ToList()
                    .Distinct()
                    .ToList();

                if (fixtureIds.Count > 0)
                    fixtureFilters.Add(Builders<Fixture>.Filter.In(f => f.FiId, fixtureIds));
            }

            if (competitorDeltas.Count > 0)
            {
                var compIds = competitorDeltas.Keys.Select(c => (int?)c).ToList();

                var fixtureIds = _selectionCollection
                    .Find(Builders<Selection>.Filter.And(
                        Builders<Selection>.Filter.In(s => s.FiId, openFixtureIds),
                        Builders<Selection>.Filter.In(s => s.MaStId, marketStatusIds),
                        Builders<Selection>.Filter.In(s => s.CompId, compIds)))
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

            var notified = 0;

            foreach (var fixture in affectedFixtures)
            {
                notified++;

                // Same rationale as the per-key progress log: a large fan-out (each fixture costs
                // three Mongo reads + a publish) must be visibly progressing, not silent.
                if (affectedFixtures.Count > 200 && notified % 200 == 0)
                    _logger.LogInformationEx($"OnTranslationChange: notified {notified}/{affectedFixtures.Count} fixtures");

                var marketFilter = Builders<Market>.Filter.Where(c => c.FiId == fixture.FiId && marketStatusIds.Contains(c.MaStId));
                var markets = _marketCollection.Find(marketFilter).ToList();

                var selectionFilter = Builders<Selection>.Filter.Where(c => c.FiId == fixture.FiId && marketStatusIds.Contains(c.MaStId));
                var selections = _selectionCollection.Find(selectionFilter).ToList();

                var payload = PrepareTranslationOddsChangeMessage(fixture, markets, selections, correlationId);

                // English template text per (MaTeId, SeTeId), for the token-composition trigger —
                // the payload itself doesn't carry SeTeNa (see ApplyTranslationDeltas).
                var seTeNaByTemplate = selections
                    .Where(s => !string.IsNullOrEmpty(s.MaTeId) && !string.IsNullOrEmpty(s.SeTeId) && !string.IsNullOrEmpty(s.SeTeNa))
                    .GroupBy(s => (s.MaTeId, s.SeTeId))
                    .ToDictionary(g => g.Key, g => g.First().SeTeNa);

                ApplyTranslationDeltas(payload, fixture, sportDeltas, segmentDeltas, leagueDeltas, teamDeltas, marketTemplateDeltas, selectionTemplateDeltas, competitorDeltas, _translationCache, seTeNaByTemplate, playerDeltas, playerTemplateDeltas);

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
            Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>> marketTemplateDeltas,
            Dictionary<(string MaTeId, string SeTeId), List<TranslationMessage>> selectionTemplateDeltas,
            Dictionary<int, List<TranslationMessage>> competitorDeltas = null,
            ITranslationCache translationCache = null,
            // English SeTeNa per (MaTeId, SeTeId) pair, read off the fixture's Mongo Selection
            // docs by the caller — the wire payload deliberately doesn't carry SeTeNa, but the
            // token-composition trigger needs the English template text (see
            // SelectionTranslationComposer). Null-safe: without it, token selections simply
            // fall through to the raw selectionTemplateDeltas assignment (pre-compose behavior).
            IReadOnlyDictionary<(string MaTeId, string SeTeId), string> seTeNaByTemplate = null,
            Dictionary<int, List<TranslationMessage>> playerDeltas = null,
            Dictionary<string, List<TranslationMessage>> playerTemplateDeltas = null)
        {
            competitorDeltas ??= new Dictionary<int, List<TranslationMessage>>();
            playerDeltas ??= new Dictionary<int, List<TranslationMessage>>();
            playerTemplateDeltas ??= new Dictionary<string, List<TranslationMessage>>();

            payload.SpTr = sportDeltas.TryGetValue(fixture.SpId, out var spTr) ? spTr : null;
            payload.SegTr = segmentDeltas.TryGetValue(fixture.SegId, out var segTr) ? segTr : null;
            payload.LeTr = leagueDeltas.TryGetValue(fixture.LeId, out var leTr) ? leTr : null;
            payload.HoTr = fixture.HoId != null && teamDeltas.TryGetValue(fixture.HoId, out var hoTr) ? hoTr : null;
            payload.AwTr = fixture.AwId != null && teamDeltas.TryGetValue(fixture.AwId, out var awTr) ? awTr : null;

            foreach (var market in payload.Ma)
            {
                if (market.CompId != null)
                {
                    // Player market (GM-528): never push the raw template text — its translated
                    // name is COMPOSED (interpolated via MaTeP: when the marker survived, join
                    // format otherwise; all inputs read from the already-Apply'd cache) when any
                    // input changed; otherwise stay null so consumers keep what they have.
                    // The trigger only needs "did this base template change at all": wording
                    // scoping is meaningless here because a player market's MaNa (player-inclusive)
                    // never equals a template wording.
                    var affected = playerDeltas.ContainsKey(market.CompId.Value)
                        || (market.MaTeId != null && marketTemplateDeltas.ContainsKey(market.MaTeId))
                        || (market.MaTeId != null && playerTemplateDeltas.ContainsKey(market.MaTeId));

                    market.MaTeTr = affected && translationCache != null
                        ? PlayerMarketNameComposer.Compose(
                            translationCache.GetTranslations($"Pl:{market.CompId.Value}", null),
                            translationCache.GetTranslations($"MaTe:{market.MaTeId}", null),
                            translationCache.GetTranslations($"MaTeP:{market.MaTeId}", null))
                        : null;
                }
                else
                {
                    // Regular market: wording-aware — an exact-wording (SrcTx) delta beats an
                    // unscoped one (see ResolveMarketTemplateDeltas).
                    market.MaTeTr = ResolveMarketTemplateDeltas(marketTemplateDeltas, market.MaTeId, market.MaNa);
                }

                foreach (var selection in market.Se)
                {
                    // A selection whose CompId is one of the fixture's own two participants (e.g. a
                    // Match Winner market where the selections ARE the two teams) resolves to the
                    // fixture's own Te:{HoId}/{AwId} key, not Co:/SeTe: — see
                    // SelectionTranslationKeyResolver. Recompute through the same resolver+cache path
                    // the persist side (CascadeTeamTranslationToSelections) uses, instead of leaving
                    // it null just because it isn't in competitorDeltas/selectionTemplateDeltas.
                    var participantTeamId = selection.CompId == null ? null
                        : selection.CompId.Value.ToString() == fixture.HoId ? fixture.HoId
                        : selection.CompId.Value.ToString() == fixture.AwId ? fixture.AwId
                        : null;

                    if (participantTeamId != null && translationCache != null && teamDeltas.ContainsKey(participantTeamId))
                    {
                        var key = SelectionTranslationKeyResolver.Resolve(
                            selection.CompId, selection.SeNa, seTeNa: null, selection.SeTeId, market.MaTeId, market.IsHa,
                            fixture.HoId, fixture.HoNa, fixture.AwId, fixture.AwNa);

                        selection.SeNaTr = translationCache.GetTranslations(key.StringId, key.SourceText);
                        continue;
                    }

                    // Positional token selections ("Home or Draw"): the stamped SeNaTr embeds the
                    // fixture's translated team names, so the outgoing delta must carry the same
                    // COMPOSED text — raw template text would visibly regress connected clients to
                    // generic names until the next full odds change. Recompute when this push
                    // changed the template itself or either of the fixture's two team names.
                    var seTeNa = seTeNaByTemplate != null && selection.MaTeId != null && selection.SeTeId != null
                        && seTeNaByTemplate.TryGetValue((selection.MaTeId, selection.SeTeId), out var templateName)
                            ? templateName
                            : null;

                    if (translationCache != null && selection.CompId == null
                        && SelectionTranslationComposer.RequiresTeamTokenComposition($"SeTe:{selection.MaTeId}:{selection.SeTeId}", seTeNa))
                    {
                        List<TranslationMessage> templateDeltas = null;
                        var templateChanged = selectionTemplateDeltas.TryGetValue((selection.MaTeId, selection.SeTeId), out templateDeltas);
                        var teamChanged = (fixture.HoId != null && teamDeltas.ContainsKey(fixture.HoId))
                            || (fixture.AwId != null && teamDeltas.ContainsKey(fixture.AwId));

                        if (templateChanged || teamChanged)
                        {
                            // A template edit composes over the DELTA (preserves empty-Tx removal
                            // markers); a team-only change re-reads the template from the cache,
                            // which was Apply'd before this runs (see Worker's ON_TRANSLATION_CHANGE).
                            var template = templateChanged
                                ? templateDeltas
                                : translationCache.GetTranslations($"SeTe:{selection.MaTeId}:{selection.SeTeId}", seTeNa);

                            selection.SeNaTr = SelectionTranslationComposer.ComposeTeamTokens(
                                template,
                                seTeNa,
                                translationCache.GetTranslations($"Te:{fixture.HoId}", fixture.HoNa),
                                translationCache.GetTranslations($"Te:{fixture.AwId}", fixture.AwNa));
                        }
                        else
                        {
                            selection.SeNaTr = null; // this push isn't about this selection
                        }

                        continue;
                    }

                    // Co:/SeTe: are mutually exclusive lookup keys for the same SeNaTr result,
                    // matching the seeding/enrichment branches elsewhere — a selection's CompId
                    // decides which delta dictionary applies.
                    selection.SeNaTr = selection.CompId != null
                        ? (competitorDeltas.TryGetValue(selection.CompId.Value, out var coTr) ? coTr : null)
                        : (selection.MaTeId != null && selection.SeTeId != null
                            && selectionTemplateDeltas.TryGetValue((selection.MaTeId, selection.SeTeId), out var seTeTr) ? seTeTr : null);
                }
            }
        }

        // One delta list per glossary key, all changed languages together. An empty Tx stays in the
        // delta as a removal so consumers revert to the English name. SourceTextByKey holds the
        // row's SOURCE_TEXT when the sender provides it (SrcTx, set by every producer since GM-852);
        // it scopes MaTe updates to the markets carrying that wording.
        internal static (Dictionary<string, List<TranslationMessage>> DeltasByKey, Dictionary<string, string> SourceTextByKey) GroupDeltas(IEnumerable<TranslationChangeItem> items)
        {
            var deltasByKey = new Dictionary<string, List<TranslationMessage>>();
            var sourceTextByKey = new Dictionary<string, string>();

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.StId) || string.IsNullOrEmpty(item.LaCo))
                    continue;

                if (!deltasByKey.TryGetValue(item.StId, out var deltas))
                    deltasByKey[item.StId] = deltas = new List<TranslationMessage>();

                deltas.Add(new TranslationMessage { LaCo = item.LaCo.ToUpperInvariant(), Tx = item.Tx ?? string.Empty });

                if (!string.IsNullOrEmpty(item.SrcTx))
                    sourceTextByKey[item.StId] = item.SrcTx;
            }

            return (deltasByKey, sourceTextByKey);
        }

        // Records a MaTe announce under its BASE template id (a wording-variant id such as
        // "259~a1b2c3d4" is stripped, see MarketTemplateVariantId) together with its SrcTx, so the
        // push path can pick the entry matching each market's MaNa (ResolveMarketTemplateDeltas).
        // Returns the base id.
        internal static string AddMarketTemplateDelta(
            Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>> marketTemplateDeltas,
            string maTeId,
            string srcTx,
            List<TranslationMessage> deltas)
        {
            var baseMaTeId = MarketTemplateVariantId.GetBaseId(maTeId);

            if (!marketTemplateDeltas.TryGetValue(baseMaTeId, out var entries))
                marketTemplateDeltas[baseMaTeId] = entries = new List<(string SrcTx, List<TranslationMessage> Deltas)>();

            entries.Add((srcTx, deltas));

            return baseMaTeId;
        }

        // The Mongo filter for a MaTe raw-text update: open, player-visible REGULAR markets (CompId
        // null) of the base template, narrowed to the markets whose MaNa is srcTx when one is given.
        internal static FilterDefinition<Market> BuildMaTeMarketFilter(IEnumerable<long> openFixtureIds, IEnumerable<int> openMarketStatusIds, string baseMaTeId, string srcTx)
        {
            var filter = Builders<Market>.Filter.And(
                Builders<Market>.Filter.In(m => m.FiId, openFixtureIds),
                Builders<Market>.Filter.In(m => m.MaStId, openMarketStatusIds),
                Builders<Market>.Filter.Eq(m => m.MaTeId, baseMaTeId),
                Builders<Market>.Filter.Eq(m => m.CompId, null));

            if (string.IsNullOrEmpty(srcTx))
                return filter;

            // Case-insensitive exact match — doc MaNa and TC2 SOURCE_TEXT come from the same feed
            // wording but their casing may drift independently.
            return Builders<Market>.Filter.And(
                filter,
                Builders<Market>.Filter.Regex(m => m.MaNa, new BsonRegularExpression("^" + Regex.Escape(srcTx) + "$", "i")));
        }

        // Every producer now sends SrcTx (GM-852), so base and variant announces alike apply only
        // to markets whose MaNa matches it, ignoring case. An entry with no SrcTx comes only from
        // older senders and applies template-wide. An exact-wording entry wins over an unscoped one.
        internal static List<TranslationMessage> ResolveMarketTemplateDeltas(
            Dictionary<string, List<(string SrcTx, List<TranslationMessage> Deltas)>> marketTemplateDeltas,
            string maTeId,
            string maNa)
        {
            if (maTeId == null || !marketTemplateDeltas.TryGetValue(maTeId, out var entries))
                return null;

            List<TranslationMessage> unscoped = null;

            foreach (var (srcTx, deltas) in entries)
            {
                if (string.IsNullOrEmpty(srcTx))
                    unscoped = deltas;
                else if (maNa != null && string.Equals(srcTx, maNa, StringComparison.OrdinalIgnoreCase))
                    return deltas;
            }

            return unscoped;
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
                        CompId = selection.CompId,
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
