using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using System.Security.Cryptography;
using System.Text;

namespace OE.Translation.Service.Repositories
{
    // Ported from bet-genius BG.Translation.Service (2026-07 translation-flow redesign — service
    // migrated into odds-engine-2 as the single translation owner). The flat TranslationCache2 is
    // the ONLY SQL store: one row per template/team String_ID carrying the EN source text, the
    // per-language TARGET_TEXT_* columns (owned by the external translation pipeline; empty =
    // untranslated), and HAS_BET_PLACEMENT (flagged by OE.Translation.Job). The retired
    // SportsbookDynamic / SportsbookGlossary tables were removed with the redesign — the external
    // script's worklist is now "TC2 rows with an empty target column".
    public class TranslationRepository : ITranslationRepository
    {
        #region Fields

        private readonly ILogger<TranslationRepository> _logger;
        private readonly IMemCache _memCache;
        private readonly IServiceScopeFactory _scopeFactory;

        // Master switch for source seeding into TranslationCache2. Enable via
        // Custom:Translation:CacheSeed.
        private readonly bool _cacheSeed;

        // Temporary bridge (ported from the removed BG.Translation.Service, adapted to the flat
        // schema): after seeding, copy already-translated strings from the external, SOURCE_HASH-
        // keyed TranslationCache table into TranslationCache2's EMPTY TARGET_TEXT_* columns so
        // they aren't re-translated under the String_ID key space. Runs off the seeded rows, so
        // it only does anything while _cacheSeed is also on. Default OFF; remove once the
        // external pipeline has drained the empty-target worklist. Enable via
        // Custom:Translation:CacheBackfill.
        private readonly bool _cacheBackfill;

        // Cache namespace for the per-key write throttle (~one DB round-trip per key per TTL).
        // The "T#" prefix is kept for cache-key continuity across the migration.
        private const string TranslationCachePrefix = "T#";

        // Cache namespace throttling the TranslationCache backfill lookup per String_ID, separate
        // from TranslationCachePrefix so a liveness-only touch doesn't skip a pending backfill.
        private const string BackfillCachePrefix = "T#BF#";

        // Fixed language set of the flat TranslationCache2 schema (TARGET_TEXT_ZH/_ES/_FR);
        // legacy TranslationCache rows in any other language have no column to land in and are
        // skipped by the backfill.
        private const string LANGUAGE_ZH = "ZH";
        private const string LANGUAGE_ES = "ES";
        private const string LANGUAGE_FR = "FR";

        #endregion

        #region Ctor

        public TranslationRepository(
            ILogger<TranslationRepository> logger,
            IMemCache memCache,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _memCache = memCache;
            _scopeFactory = scopeFactory;
            _cacheSeed = configuration.GetValue("Custom:Translation:CacheSeed", false);
            _cacheBackfill = configuration.GetValue("Custom:Translation:CacheBackfill", false);
        }

        #endregion

        #region Public Methods

        public async Task OnProcessFixtureSetUpdate(OnFixtureChangeMessage message)
        {
            try
            {
                if (message == null)
                {
                    _logger.LogWarning("Fixture message is null");
                    return;
                }

                if (!_cacheSeed)
                    return;

                // Template/team keys: sport/segment/league by id, and the fixture reduced to its
                // two teams (keyed by team id) instead of the per-fixture name.
                var items = new List<(string id, string value)>();

                AddIfValid(items, $"SpId:{message.SpId}", message.SpNa);
                AddIfValid(items, $"SegId:{message.SegId}", message.SegNa);
                AddIfValid(items, $"LeId:{message.LeId}", message.LeNa);
                AddIfValid(items, $"Te:{message.HoId}", message.HoNa);
                AddIfValid(items, $"Te:{message.AwId}", message.AwNa);

                // Player roster (GM-528 per-player prop markets): one row per player, keyed by the
                // vendor competitor id. Deliberately NOT Co:{id} — Co: rows carry a selection's
                // SeNa as SOURCE_TEXT, wrong semantics for roster names.
                if (message.Pl != null)
                {
                    foreach (var player in message.Pl)
                    {
                        if (player == null)
                            continue;

                        AddIfValid(items, $"Pl:{player.PlId}", player.PlNa);
                    }
                }

                if (items.Count == 0)
                {
                    _logger.LogDebug("No new fixture data for message {Id} (invalid or already cached)", message.Id);
                    return;
                }

                await BulkSeed(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed processing fixture update for FiId: {Id}",
                    message?.Id);

                throw;
            }
        }

        public async Task OnProcessMarketSetUpdate(OnMarketChangeMessage message)
        {
            try
            {
                if (message?.Ma == null || message.Ma.Count == 0)
                    return;

                if (!_cacheSeed)
                    return;

                var items = new List<(string id, string value)>();

                foreach (var market in message.Ma)
                {
                    if (market == null)
                        continue;

                    // Market template (deduped): MaTe:{MaTeId} -> template name. Throttled PER
                    // WORDING, not per key: the feed emits multiple concurrent wordings for one
                    // template id (verified 2026-07-16, MaTe:259 = "Total Goals Over / Under" AND
                    // "Over/Under" simultaneously), and a key-only throttle swallowed every
                    // wording after the first for the cache TTL — BulkSeed never even saw the
                    // second wording to create its variant row.
                    AddIfValid(items, $"MaTe:{market.MaTeId}", market.MaTeNa,
                        throttleKey: MarketTemplateVariantId.Compute(market.MaTeId, market.MaTeNa));

                    // Placeholder template for player-prop markets (GM-528 interpolation):
                    // MaTeP:{MaTeId} -> the generic template with the vendor's player-slot phrase
                    // replaced by {player} ("1st Innings {player} to Reach 50 Runs Yes/No"), so
                    // the translation keeps a substitutable marker. Seeded only for templates the
                    // feed actually uses as player markets (CompId present); templates without a
                    // single recognizable slot (e.g. two-player "Player A and Player B") derive
                    // null and keep the join-format fallback. Seeded PROCESSED=1 — deliberately
                    // OFF the DeepL worklist until {player}-marker survival is confirmed; see
                    // IsPlayerPlaceholderKey.
                    if (market.CompId.HasValue)
                        AddIfValid(items, $"MaTeP:{market.MaTeId}",
                            PlayerMarketNameComposer.DerivePlaceholderSource(market.MaTeNa));

                    if (market.Se == null)
                        continue;

                    foreach (var selection in market.Se)
                    {
                        if (selection == null)
                            continue;

                        // Which TranslationCache2 key backs this selection's translation — see
                        // OE.Shared.Utilities.SelectionTranslationKeyResolver, the single shared
                        // implementation also used by the read side (MongoTranslationRepository,
                        // TradingRepository). This used to be a hand-duplicated "CompId != null ->
                        // Co: else SeTe:" branch here with its own copies of the participant-match
                        // and team-name-fused skip guards, but no digit-only guard at all — it
                        // silently drifted out of sync and kept seeding bare numeric SeTeNa codes
                        // (e.g. "29") into SeTe: rows even after the shared resolver was fixed to
                        // reject them (the exact same class of bug ReadActiveSnapshot had). Routing
                        // through the shared resolver here too means the seeding and read sides can
                        // never diverge again.
                        var key = SelectionTranslationKeyResolver.Resolve(
                            selection.CompId, selection.SeNa, selection.SeTeNa, selection.SeTeId, market.MaTeId, market.IsHa,
                            message.HoId, message.HoNa, message.AwId, message.AwNa);

                        // Te:{HoId}/{AwId} is OnProcessFixtureSetUpdate's exclusive responsibility,
                        // seeded once from the plain HoNa/AwNa. Never seed it here even when Resolve
                        // redirects a selection to it: for the non-handicap case Resolve's SourceText
                        // is the FULL fused SeNa (e.g. "Karpaty Lviv 1-0", needed for the read-side
                        // splice), and if that ever raced ahead of the fixture-level seed for a
                        // brand-new fixture, BulkSeed's rename handling would treat it as a genuine
                        // source change and stamp the stable Te: row's SOURCE_TEXT with the fused
                        // text — the same corruption this whole fix is meant to prevent, just moved
                        // one key over.
                        if (key.StringId.StartsWith("Te:", StringComparison.Ordinal))
                            continue;

                        AddIfValid(items, key.StringId, key.SourceText);
                    }
                }

                if (items.Count > 0)
                    await BulkSeed(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed processing market update for FiId: {FiId}",
                    message?.FiId);

                throw;
            }
        }

        // One-time migration (Custom:Translation:SeedActiveSnapshot): insert-only — filters out
        // any id that already has a TranslationCache2 row (regardless of whether its SOURCE_TEXT
        // matches) before calling BulkSeed, so this never resets PROCESSED/clears targets on an
        // existing row. That's a deliberate scope decision: fixing already-seeded-but-stale rows
        // (e.g. a wording change) is the message-driven path's job, not this startup sweep's.
        public async Task SeedActiveSnapshotAsync(List<(string id, string value)> items)
        {
            if (items == null || items.Count == 0)
                return;

            var distinctItems = items
                .GroupBy(x => x.id)
                .Select(g => g.Last())
                .ToList();

            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider
                .GetRequiredService<TranslationContext>();

            var ids = distinctItems
                .Select(x => x.id)
                .ToList();

            var existingIds = new HashSet<string>();

            foreach (var chunk in ids.Chunk(1000))
            {
                var chunkIds = chunk.ToList();

                // .AsQueryable() disambiguates from the in-box System.Linq.AsyncEnumerable on net10.0 (CS0121)
                var found = await context.TranslationCache2
                    .AsQueryable()
                    .Where(x => chunkIds.Contains(x.StringId))
                    .Select(x => x.StringId)
                    .ToListAsync();

                foreach (var id in found)
                    existingIds.Add(id);
            }

            var missingOnly = distinctItems
                .Where(x => !existingIds.Contains(x.id))
                .ToList();

            _logger.LogInformation(
                "SeedActiveSnapshot: {MissingCount} of {TotalCount} active entities have no TranslationCache2 row yet",
                missingOnly.Count, distinctItems.Count);

            if (missingOnly.Count == 0)
                return;

            await BulkSeed(missingOnly);
        }

        #endregion

        #region Utilities

        // The caller composes the full String_ID key (MaTe:/SeTe:/Te:/SpId:/...); the memcache
        // check throttles writes to ~once per key per TTL. throttleKey overrides the cache key
        // when the throttle must be finer than the row key — MaTe: throttles per WORDING (see
        // OnProcessMarketSetUpdate) so concurrent template wordings each reach BulkSeed.
        private void AddIfValid(
            List<(string id, string value)> list,
            string key,
            string? value,
            string? throttleKey = null)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (_memCache.GetCache<string>(TranslationCachePrefix + (throttleKey ?? key)) != null)
                return;

            list.Add((key, value));
        }

        // Seeds the source side of TranslationCache2: insert-if-missing with empty TARGET_TEXT_*
        // columns and PROCESSED = 0 (the pipeline's worklist flag); on a genuine source-name
        // change update SOURCE_TEXT, clear the now-stale targets and reset PROCESSED so the
        // external pipeline re-translates; otherwise a liveness touch bumps UPDATED_AT so
        // OE.Translation.Job's retention sweep never reaps a row the feed still emits.
        // TARGET_TEXT_*/PROCESSED are the external pipeline's columns only (see
        // Scripts/TranslationCache2.sql) — this service must never set PROCESSED = 1 or write a
        // target itself. update.py already does its own same-SOURCE_TEXT/SOURCE_LANGUAGE fan-out
        // (IX_TC2_SOURCE_TEXT) once it translates a row, and only *it* can correctly call
        // v1/cachepreload afterward; writing a target here would pull the row off the
        // PROCESSED = 0 worklist before update.py ever sees it, silently breaking that
        // notification (verified 2026-07-09 against a live fixture: Mongo/adapter never learned a
        // content-matched translation because the row was never visited by the pipeline).
        private async Task BulkSeed(List<(string id, string value)> items)
        {
            // Dedup per row IDENTITY: for MaTe: items that's id + normalized wording — the feed
            // emits multiple concurrent wordings for one template id, and each is its own row
            // (see MarketTemplateVariantId) — for every other key the id alone, as before.
            var distinctItems = items
                .GroupBy(SeedIdentity)
                .Select(g => g.Last())
                .ToList();

            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider
                .GetRequiredService<TranslationContext>();

            // Fetch base rows AND, for MaTe: items, their wording-variant candidate in the same
            // round trip (the variant id is deterministic from the wording, so it is computable
            // before knowing whether the primary row's wording matches).
            var ids = new List<string>();

            foreach (var item in distinctItems)
            {
                ids.Add(item.id);

                if (IsMarketTemplateKey(item.id))
                    ids.Add(MarketTemplateVariantId.Compute(MarketTemplateIdSegment(item.id), item.value));
            }

            var existing = await context.TranslationCache2
                .AsQueryable()
                .Where(x => ids.Contains(x.StringId))
                .ToListAsync();

            var dict = existing.ToDictionary(x => x.StringId);

            var savedEntities = new List<TranslationCache2>();

            foreach (var item in distinctItems)
            {
                // Defensive: SOURCE_TEXT is NVARCHAR(250) NOT NULL.
                var sourceText = item.value.Length <= 250
                    ? item.value
                    : item.value.Substring(0, 250);

                // A market template whose primary row holds a DIFFERENT wording: never rename or
                // wipe the primary (the old behavior flip-flopped the single row between the
                // concurrent wordings, wiping and re-translating on every flip while the losing
                // wording rendered untranslated) — this wording gets its own variant row instead,
                // translated independently and served through the caches' content-addressed index.
                if (IsMarketTemplateKey(item.id)
                    && dict.TryGetValue(item.id, out var primary)
                    && !string.Equals(primary.SourceText, sourceText, StringComparison.OrdinalIgnoreCase))
                {
                    var variantId = MarketTemplateVariantId.Compute(MarketTemplateIdSegment(item.id), sourceText);

                    if (dict.TryGetValue(variantId, out var variant))
                    {
                        if (!string.Equals(variant.SourceText, sourceText, StringComparison.Ordinal))
                            variant.SourceText = sourceText; // case-only alignment, targets kept

                        variant.UpdatedAt = DateTime.UtcNow; // liveness
                    }
                    else
                    {
                        variant = new TranslationCache2
                        {
                            StringId = variantId,
                            SourceLanguage = "EN",
                            SourceText = sourceText,
                            TargetTextZh = string.Empty,
                            TargetTextEs = string.Empty,
                            TargetTextFr = string.Empty,
                            HasBetPlacement = false,
                            Processed = false,
                            CreatedAt = DateTime.UtcNow
                        };

                        context.TranslationCache2.Add(variant);
                    }

                    savedEntities.Add(variant);

                    _memCache.AddOrUpdateCache(TranslationCachePrefix + variantId, item.value, 24);
                    continue;
                }

                if (dict.TryGetValue(item.id, out var entity))
                {
                    if (!string.Equals(entity.SourceText, sourceText, StringComparison.OrdinalIgnoreCase))
                    {
                        // Genuine name change (non-MaTe keys only — MaTe wording changes branch
                        // to a variant row above): stale targets are cleared and the row goes
                        // back on the pipeline's worklist (PROCESSED = 0) so every configured
                        // language is re-translated from the new SOURCE_TEXT. MaTeP: rows stay
                        // off the worklist even on a wording change — see IsPlayerPlaceholderKey.
                        entity.SourceText = sourceText;
                        entity.TargetTextZh = string.Empty;
                        entity.TargetTextEs = string.Empty;
                        entity.TargetTextFr = string.Empty;
                        entity.Processed = IsPlayerPlaceholderKey(item.id);
                    }
                    else if (!string.Equals(entity.SourceText, sourceText, StringComparison.Ordinal))
                    {
                        // Case-only drift ("Odd or Even Total" -> the feed's "Odd Or Even Total"):
                        // align SOURCE_TEXT with the live feed WITHOUT discarding the targets —
                        // translations are case-independent, and clearing them here would wipe
                        // valid work for a cosmetic difference (2026-07-14 prod incident).
                        entity.SourceText = sourceText;
                    }

                    // Liveness touch (throttled to ~once per cache TTL because cached keys never
                    // reach here — see AddIfValid).
                    entity.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    entity = new TranslationCache2
                    {
                        StringId = item.id,
                        SourceLanguage = "EN",
                        SourceText = sourceText,
                        TargetTextZh = string.Empty,
                        TargetTextEs = string.Empty,
                        TargetTextFr = string.Empty,
                        HasBetPlacement = false,
                        Processed = IsPlayerPlaceholderKey(item.id),
                        CreatedAt = DateTime.UtcNow
                    };

                    context.TranslationCache2.Add(entity);
                }

                savedEntities.Add(entity);

                // Throttle-set must mirror AddIfValid's throttle key: per wording for MaTe:,
                // per key for everything else.
                _memCache.AddOrUpdateCache(
                    TranslationCachePrefix + (IsMarketTemplateKey(item.id)
                        ? MarketTemplateVariantId.Compute(MarketTemplateIdSegment(item.id), item.value)
                        : item.id),
                    item.value,
                    24);
            }

            await context.SaveChangesAsync();

            if (_cacheBackfill)
                await BackfillTranslationCache2(context, savedEntities);
        }

        // MaTeP:{MaTeId} placeholder templates are seeded PROCESSED=1: deliberately OFF
        // update.py's DeepL worklist until {player}-marker survival through machine translation
        // is confirmed — they are translated manually via backoffice, whose edits announce
        // normally. This is the ONE deliberate exception to the "never set PROCESSED=1" rule
        // (see BulkSeed's header comment): that rule protects the translate-then-announce chain
        // of rows that SHOULD be machine-translated; MaTeP rows must not enter it at all.
        // Handing them to DeepL later = flip the stored rows to PROCESSED=0 and drop this
        // exception. update.py's missing-language reset skips all-empty PROCESSED=1 rows so it
        // cannot re-open these (see reset_for_missing_languages).
        private static bool IsPlayerPlaceholderKey(string id) =>
            id.StartsWith("MaTeP:", StringComparison.Ordinal);

        private static bool IsMarketTemplateKey(string id) =>
            id.StartsWith("MaTe:", StringComparison.Ordinal);

        private static string MarketTemplateIdSegment(string id) =>
            id.Substring("MaTe:".Length);

        // Grouping identity for BulkSeed dedup — MaTe: items are distinct per wording.
        private static string SeedIdentity((string id, string value) item) =>
            IsMarketTemplateKey(item.id)
                ? MarketTemplateVariantId.Compute(MarketTemplateIdSegment(item.id), item.value)
                : item.id;

        // TEMPORARY bridge (ported from the removed BG.Translation.Service, adapted to the flat
        // schema): the external TranslationCache table is keyed by SOURCE_HASH, TranslationCache2
        // by String_ID. For each row just seeded/touched that still has an empty target column,
        // hash its SOURCE_TEXT the way SQL Server computed the legacy persisted column and copy
        // any matching EN-source targets into the EMPTY TARGET_TEXT_* columns only — a target the
        // external pipeline already wrote is never overwritten. A row that received a backfilled
        // target is marked PROCESSED = 1 (the legacy cache satisfied it — no DeepL pass needed).
        // Throttled per String_ID via BackfillCachePrefix so a hot, already-checked key isn't
        // re-queried on every message. Remove once the external pipeline has drained the worklist.
        internal async Task BackfillTranslationCache2(TranslationContext context, List<TranslationCache2> entities)
        {
            var candidates = entities
                .Where(e =>
                    string.IsNullOrEmpty(e.TargetTextZh) ||
                    string.IsNullOrEmpty(e.TargetTextEs) ||
                    string.IsNullOrEmpty(e.TargetTextFr))
                .Where(e => _memCache.GetCache<string>(BackfillCachePrefix + e.StringId) == null)
                .GroupBy(e => e.StringId)
                .Select(g => g.Last())
                .ToList();

            if (candidates.Count == 0)
                return;

            var hashByStringId = candidates.ToDictionary(e => e.StringId, e => SourceHash(e.SourceText));

            var hashes = hashByStringId.Values
                .Distinct()
                .ToList();

            var matches = await context.TranslationCache
                .AsNoTracking()
                .Where(x => hashes.Contains(x.SourceHash) && x.SourceLanguage == "EN")
                .ToListAsync();

            var matchesByHash = matches
                .GroupBy(x => x.SourceHash)
                .ToDictionary(g => g.Key, g => g.ToList());

            var backfilled = 0;

            foreach (var candidate in candidates)
            {
                var candidateBackfilled = 0;

                if (matchesByHash.TryGetValue(hashByStringId[candidate.StringId], out var hits))
                {
                    foreach (var hit in hits)
                    {
                        if (string.IsNullOrEmpty(hit.TargetText))
                            continue; // legacy row not translated yet

                        // Defensive: legacy TARGET_TEXT is unbounded, the flat columns are
                        // NVARCHAR(250).
                        var targetText = hit.TargetText.Length <= 250
                            ? hit.TargetText
                            : hit.TargetText.Substring(0, 250);

                        switch (hit.TargetLanguage?.ToUpperInvariant())
                        {
                            case LANGUAGE_ZH when string.IsNullOrEmpty(candidate.TargetTextZh):
                                candidate.TargetTextZh = targetText;
                                candidateBackfilled++;
                                break;

                            case LANGUAGE_ES when string.IsNullOrEmpty(candidate.TargetTextEs):
                                candidate.TargetTextEs = targetText;
                                candidateBackfilled++;
                                break;

                            case LANGUAGE_FR when string.IsNullOrEmpty(candidate.TargetTextFr):
                                candidate.TargetTextFr = targetText;
                                candidateBackfilled++;
                                break;

                                // Any other language has no column in the flat schema — skipped.
                        }
                    }
                }

                if (candidateBackfilled > 0)
                {
                    // The legacy cache already carried this translation, so the row doesn't need
                    // a DeepL pass — take it off the pipeline's worklist. If a configured language
                    // is still empty, the pipeline's startup missing-language reset re-opens it.
                    candidate.Processed = true;
                    backfilled += candidateBackfilled;
                }

                _memCache.AddOrUpdateCache(BackfillCachePrefix + candidate.StringId, candidate.StringId, 24);
            }

            if (backfilled > 0)
            {
                await context.SaveChangesAsync();

                _logger.LogInformation(
                    "Backfilled {Count} translation targets from the legacy TranslationCache",
                    backfilled);
            }
        }

        // Matches the SQL Server formula behind the legacy persisted SOURCE_HASH column —
        // CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', SOURCE_TEXT), 2): SHA-256 over the NVARCHAR
        // (UTF-16LE) bytes, uppercase hex. Must stay byte-identical or the backfill join finds
        // nothing.
        internal static string SourceHash(string text)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(text)));
        }

        #endregion
    }
}
