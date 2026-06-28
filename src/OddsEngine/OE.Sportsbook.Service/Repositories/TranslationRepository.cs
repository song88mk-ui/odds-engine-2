using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OE.Shared.Data.Translation;
using OE.Shared.Models;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public class TranslationRepository : ITranslationRepository
    {
        #region Fields

        private readonly TranslationContext _context;
        private readonly IMemCache _memCache;
        private readonly ILogger<TranslationRepository> _logger;

        private const int CACHE_DURATION_MINUTES = 60;
        private const string CACHE_PREFIX = "sb:translation:";

        #endregion

        #region Ctor

        public TranslationRepository(
            TranslationContext context,
            IMemCache memCache,
            ILogger<TranslationRepository> logger)
        {
            _context = context;
            _memCache = memCache;
            _logger = logger;
        }

        #endregion

        #region Methods

        public Dictionary<string, List<TranslationItem>> GetTranslationsAsync(List<string> stringIds)
        {
            if (stringIds == null || stringIds.Count == 0)
                return new Dictionary<string, List<TranslationItem>>();

            var result = new Dictionary<string, List<TranslationItem>>();
            var missingStringIds = new List<string>();

            // =========================
            // 1. GET FROM CACHE FIRST
            // =========================
            foreach (var stringId in stringIds.Distinct())
            {
                var cacheKey = $"{CACHE_PREFIX}{stringId}";

                var cachedValue = _memCache.GetCache<List<TranslationItem>>(cacheKey);

                if (cachedValue != null)
                {
                    result[stringId] = cachedValue;
                }
                else
                {
                    missingStringIds.Add(stringId);
                }
            }

            // =========================
            // 2. GET MISSING FROM SQL
            // =========================
            if (missingStringIds.Count > 0)
            {
                _logger.LogInformation(
                    $"Translation cache miss count: {missingStringIds.Count}");

                var sqlData = _context.SportsbookDynamic
                    .AsNoTracking()
                    .Where(x =>
                        missingStringIds.Contains(x.StringId)
                        && x.Processed
                        && !string.IsNullOrEmpty(x.TargetText))
                    .Select(x => new TranslationItem
                    {
                        StringId = x.StringId,
                        TargetLanguageId = x.TargetLanguageId,
                        TargetText = x.TargetText
                    })
                    .ToList();

                var groupedSqlData = sqlData
                    .GroupBy(x => x.StringId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.ToList());

                // =========================
                // 3. STORE TO CACHE
                // =========================
                foreach (var item in groupedSqlData)
                {
                    var cacheKey = $"{CACHE_PREFIX}{item.Key}";

                    _memCache.AddOrUpdateCache(
                        cacheKey,
                        item.Value,
                        (int)TimeSpan.FromMinutes(CACHE_DURATION_MINUTES).TotalHours);

                    result[item.Key] = item.Value;
                }

                // =========================
                // 4. CACHE EMPTY RESULTS
                // Prevent repetitive SQL hits
                // =========================
                var foundIds = groupedSqlData.Keys.ToHashSet();

                foreach (var missingId in missingStringIds)
                {
                    if (!foundIds.Contains(missingId))
                    {
                        var cacheKey = $"{CACHE_PREFIX}{missingId}";

                        _memCache.AddOrUpdateCache(
                            cacheKey,
                            new List<TranslationItem>(),
                            (int)TimeSpan.FromMinutes(CACHE_DURATION_MINUTES).TotalHours);

                        result[missingId] = new List<TranslationItem>();
                    }
                }
            }

            return result;
        }

        // Content-keyed lookup (proposal: docs/proposals/TranslationContentKeying.md in platform-final).
        // Input maps each entity key ("MaId:123") -> its source text; translations are matched by
        // SOURCE_HASH so identical texts share one translation. Result stays keyed by the entity key,
        // so MapTranslations is unchanged. Returns one TranslationItem per language per key.
        public Dictionary<string, List<TranslationItem>> GetTranslationsByContent(Dictionary<string, string> keyToSourceText)
        {
            var result = new Dictionary<string, List<TranslationItem>>();

            if (keyToSourceText == null || keyToSourceText.Count == 0)
                return result;

            var keyToHash = new Dictionary<string, string>();
            foreach (var kvp in keyToSourceText)
            {
                if (string.IsNullOrWhiteSpace(kvp.Value))
                    continue;

                keyToHash[kvp.Key] = TranslationHashing.SourceHash(kvp.Value);
            }

            if (keyToHash.Count == 0)
                return result;

            var hashToItems = new Dictionary<string, List<TranslationItem>>();
            var missingHashes = new List<string>();

            foreach (var hash in keyToHash.Values.Distinct())
            {
                var cacheKey = $"{CACHE_PREFIX}h:{hash}";
                var cached = _memCache.GetCache<List<TranslationItem>>(cacheKey);

                if (cached != null)
                    hashToItems[hash] = cached;
                else
                    missingHashes.Add(hash);
            }

            if (missingHashes.Count > 0)
            {
                var sqlData = _context.SportsbookDynamic
                    .AsNoTracking()
                    .Where(x =>
                        missingHashes.Contains(x.SourceHash)
                        && x.Processed
                        && !string.IsNullOrEmpty(x.TargetText))
                    .Select(x => new { x.SourceHash, x.TargetLanguageId, x.TargetText, x.GlossaryVersion })
                    .ToList();

                // Multiple rows can share a hash (pre-dedup / multiple glossary versions);
                // keep one item per language (highest glossary version).
                var grouped = sqlData
                    .GroupBy(x => x.SourceHash)
                    .ToDictionary(
                        g => g.Key,
                        g => g.GroupBy(i => i.TargetLanguageId)
                              .Select(lg => lg.OrderByDescending(i => i.GlossaryVersion).First())
                              .Select(i => new TranslationItem
                              {
                                  TargetLanguageId = i.TargetLanguageId,
                                  TargetText = i.TargetText
                              })
                              .ToList());

                foreach (var hash in missingHashes)
                {
                    var items = grouped.TryGetValue(hash, out var v) ? v : new List<TranslationItem>();

                    var cacheKey = $"{CACHE_PREFIX}h:{hash}";
                    _memCache.AddOrUpdateCache(
                        cacheKey,
                        items,
                        (int)TimeSpan.FromMinutes(CACHE_DURATION_MINUTES).TotalHours);

                    hashToItems[hash] = items;
                }
            }

            foreach (var kvp in keyToHash)
            {
                result[kvp.Key] = hashToItems.TryGetValue(kvp.Value, out var items)
                    ? items
                    : new List<TranslationItem>();
            }

            return result;
        }

        #endregion
    }
}
