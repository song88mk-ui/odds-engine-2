using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Shared.Data.Translation;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using System.Collections.Concurrent;

namespace OE.Adapter.Service.Repositories
{
    public class TranslationCache : ITranslationCache
    {
        #region Fields

        private readonly ILogger<TranslationCache> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        // STRING_ID -> immutable translation list. Lists are replaced wholesale (never mutated in
        // place) so readers on the message path can hand them to outgoing messages without locking.
        private readonly ConcurrentDictionary<string, List<TranslationMessage>> _translations = new();

        // Fixed language set of the flat TranslationCache2 schema (TARGET_TEXT_ZH/_ES/_FR).
        // Adding a language is a schema change + code change here.
        private const string LANGUAGE_ZH = "ZH";
        private const string LANGUAGE_ES = "ES";
        private const string LANGUAGE_FR = "FR";

        #endregion

        #region Ctor

        public TranslationCache(ILogger<TranslationCache> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        #endregion

        #region Methods

        public void Preload()
        {
            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider.GetRequiredService<TranslationContext>();

            var rows = context.TranslationCache2
                .AsNoTracking()
                .Select(x => new
                {
                    x.StringId,
                    x.TargetTextZh,
                    x.TargetTextEs,
                    x.TargetTextFr
                });

            var loaded = 0;

            foreach (var row in rows)
            {
                var translations = BuildTranslations(row.TargetTextZh, row.TargetTextEs, row.TargetTextFr);

                if (translations == null)
                    continue; // nothing translated yet — cache only rows that add value

                _translations[row.StringId] = translations;
                loaded++;
            }

            _logger.LogInformation($"Translation cache preloaded: {loaded} translated entries");
        }

        public List<TranslationMessage> GetTranslations(string stringId)
        {
            if (string.IsNullOrEmpty(stringId))
                return null;

            return _translations.TryGetValue(stringId, out var translations)
                ? translations
                : null;
        }

        public void Apply(List<TranslationChangeItem> items)
        {
            if (items == null || items.Count == 0)
                return;

            foreach (var item in items)
            {
                if (item == null || string.IsNullOrEmpty(item.StId) || string.IsNullOrEmpty(item.LaCo))
                    continue;

                var languageCode = item.LaCo.ToUpperInvariant();

                if (languageCode != LANGUAGE_ZH && languageCode != LANGUAGE_ES && languageCode != LANGUAGE_FR)
                {
                    _logger.LogWarning($"Translation change ignored for unsupported language {item.LaCo} ({item.StId})");
                    continue;
                }

                _translations.AddOrUpdate(
                    item.StId,
                    _ => string.IsNullOrEmpty(item.Tx)
                        ? new List<TranslationMessage>()
                        : new List<TranslationMessage> { new TranslationMessage { LaCo = languageCode, Tx = item.Tx } },
                    (_, current) =>
                    {
                        // Copy-on-write: the current list may be referenced by in-flight messages.
                        var updated = current.Where(t => t.LaCo != languageCode).ToList();

                        if (!string.IsNullOrEmpty(item.Tx))
                            updated.Add(new TranslationMessage { LaCo = languageCode, Tx = item.Tx });

                        return updated;
                    });

                // Drop keys whose last language entry was cleared so lookups return null, not [].
                if (_translations.TryGetValue(item.StId, out var result) && result.Count == 0)
                    _translations.TryRemove(item.StId, out _);
            }
        }

        #endregion

        #region Utilities

        private static List<TranslationMessage> BuildTranslations(string zh, string es, string fr)
        {
            List<TranslationMessage> translations = null;

            if (!string.IsNullOrEmpty(zh))
                (translations ??= new List<TranslationMessage>()).Add(new TranslationMessage { LaCo = LANGUAGE_ZH, Tx = zh });

            if (!string.IsNullOrEmpty(es))
                (translations ??= new List<TranslationMessage>()).Add(new TranslationMessage { LaCo = LANGUAGE_ES, Tx = es });

            if (!string.IsNullOrEmpty(fr))
                (translations ??= new List<TranslationMessage>()).Add(new TranslationMessage { LaCo = LANGUAGE_FR, Tx = fr });

            return translations;
        }

        #endregion
    }
}
