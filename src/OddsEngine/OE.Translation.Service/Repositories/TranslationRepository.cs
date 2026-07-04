using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;

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

        // Cache namespace for the per-key write throttle (~one DB round-trip per key per TTL).
        // The "T#" prefix is kept for cache-key continuity across the migration.
        private const string TranslationCachePrefix = "T#";

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

                    // Market template (deduped): MaTe:{MaTeId} -> template name.
                    AddIfValid(items, $"MaTe:{market.MaTeId}", market.MaTeNa);

                    if (market.Se == null)
                        continue;

                    foreach (var selection in market.Se)
                    {
                        if (selection == null)
                            continue;

                        // Selection template (deduped, namespaced by market template to avoid
                        // cross-market outcome-id / numerator collisions): SeTe:{MaTeId}:{SeTeId}.
                        AddIfValid(items, $"SeTe:{market.MaTeId}:{selection.SeTeId}", selection.SeTeNa);
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

        #endregion

        #region Utilities

        // The caller composes the full String_ID key (MaTe:/SeTe:/Te:/SpId:/...); the memcache
        // check throttles writes to ~once per key per TTL.
        private void AddIfValid(
            List<(string id, string value)> list,
            string key,
            string? value)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (_memCache.GetCache<string>(TranslationCachePrefix + key) != null)
                return;

            list.Add((key, value));
        }

        // Seeds the source side of TranslationCache2: insert-if-missing with empty TARGET_TEXT_*
        // columns; on a genuine source-name change update SOURCE_TEXT only (targets stay stale
        // until the external pipeline re-translates); otherwise a liveness touch bumps UPDATED_AT
        // so OE.Translation.Job's retention sweep never reaps a row the feed still emits.
        // The TARGET_TEXT_* columns are owned by the external translation pipeline and are never
        // written here.
        private async Task BulkSeed(List<(string id, string value)> items)
        {
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

            var existing = await context.TranslationCache2
                .Where(x => ids.Contains(x.StringId))
                .ToListAsync();

            var dict = existing.ToDictionary(x => x.StringId);

            foreach (var item in distinctItems)
            {
                // Defensive: SOURCE_TEXT is NVARCHAR(1000) NOT NULL.
                var sourceText = item.value.Length <= 1000
                    ? item.value
                    : item.value.Substring(0, 1000);

                if (dict.TryGetValue(item.id, out var entity))
                {
                    if (entity.SourceText != sourceText)
                    {
                        // Genuine name change; the external pipeline re-reads SOURCE_TEXT and
                        // decides for itself whether to re-translate.
                        entity.SourceText = sourceText;
                    }

                    // Liveness touch (throttled to ~once per cache TTL because cached keys never
                    // reach here — see AddIfValid).
                    entity.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    context.TranslationCache2.Add(new TranslationCache2
                    {
                        StringId = item.id,
                        SourceLanguage = "EN",
                        SourceText = sourceText,
                        TargetTextZh = string.Empty,
                        TargetTextEs = string.Empty,
                        TargetTextFr = string.Empty,
                        HasBetPlacement = false,
                        GlossaryVersion = 1,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                _memCache.AddOrUpdateCache(
                    TranslationCachePrefix + item.id,
                    item.value,
                    24);
            }

            await context.SaveChangesAsync();
        }

        #endregion
    }
}
