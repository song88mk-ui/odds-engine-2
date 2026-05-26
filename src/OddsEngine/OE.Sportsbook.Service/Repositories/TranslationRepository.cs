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

        #endregion
    }
}
