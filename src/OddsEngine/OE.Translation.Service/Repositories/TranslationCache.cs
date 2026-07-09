using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Shared.Data.Translation;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using System.Collections.Concurrent;

namespace OE.Translation.Service.Repositories
{
    // Mirror of OE.Adapter.Service's TranslationCache (keep in sync): preloads the whole flat
    // TranslationCache2 table at startup and applies ON_TRANSLATION_CHANGE deltas copy-on-write.
    // Here it backs the Mongo *NaTr writes for the raw (unenriched) BG.Receiver stream.
    public class TranslationCache : ITranslationCache
    {
        #region Fields

        private readonly ILogger<TranslationCache> _logger;
        private readonly IServiceScopeFactory _scopeFactory;

        // STRING_ID -> immutable translation list. Lists are replaced wholesale (never mutated in
        // place) so readers on the message path can hand them to outgoing writes without locking.
        private readonly ConcurrentDictionary<string, List<TranslationMessage>> _translations = new();

        // STRING_ID -> the SOURCE_TEXT it was cached with at preload, used to detect a rename
        // (the feed now sends different English text for the same STRING_ID) so GetTranslations
        // can fall back to the content index below instead of serving a stale translation.
        private readonly ConcurrentDictionary<string, string> _sourceTextByStringId = new();

        // SOURCE_TEXT -> translation list, built once at preload from the same rows as
        // _translations. Content-addressed fallback for a STRING_ID miss or rename: reuses an
        // existing translation for identical English text under a different STRING_ID rather than
        // waiting on the external pipeline to re-translate from scratch.
        private readonly ConcurrentDictionary<string, List<TranslationMessage>> _translationsByText = new();

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
                    x.SourceText,
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
                _sourceTextByStringId[row.StringId] = row.SourceText;

                if (!string.IsNullOrEmpty(row.SourceText))
                    _translationsByText[row.SourceText] = translations;

                loaded++;
            }

            _logger.LogInformation($"Translation cache preloaded: {loaded} translated entries");
        }

        public List<TranslationMessage>? GetTranslations(string stringId, string currentText)
        {
            if (string.IsNullOrEmpty(stringId))
                return null;

            if (_translations.TryGetValue(stringId, out var translations))
            {
                // Only rows seen at preload have a tracked SOURCE_TEXT to compare against; rows
                // seeded purely via ON_TRANSLATION_CHANGE deltas after startup have none and are
                // trusted as-is (unchanged from pre-fallback behavior). A tracked mismatch means
                // the feed renamed this entity — fall through to the content match below instead.
                if (!_sourceTextByStringId.TryGetValue(stringId, out var cachedText) || cachedText == currentText)
                    return translations;
            }

            // STRING_ID miss, or the row's cached SOURCE_TEXT no longer matches (a rename) — fall
            // back to a content match so identical English text elsewhere isn't re-translated from
            // scratch.
            if (!string.IsNullOrEmpty(currentText) && _translationsByText.TryGetValue(currentText, out var byText))
                return byText;

            return null;
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
                        // Copy-on-write: the current list may be referenced by in-flight writes.
                        var updated = current.Where(t => t.LaCo != languageCode).ToList();

                        if (!string.IsNullOrEmpty(item.Tx))
                            updated.Add(new TranslationMessage { LaCo = languageCode, Tx = item.Tx });

                        return updated;
                    });

                // Drop keys whose last language entry was cleared so lookups return null, not [].
                if (_translations.TryGetValue(item.StId, out var result) && result.Count == 0)
                    _translations.TryRemove(item.StId, out _);

                // Keep the content index in sync for rows whose SOURCE_TEXT we know (preloaded
                // rows) so a subsequent content-fallback lookup doesn't serve a stale list.
                if (_sourceTextByStringId.TryGetValue(item.StId, out var sourceText) && !string.IsNullOrEmpty(sourceText))
                {
                    if (_translations.TryGetValue(item.StId, out var refreshed))
                        _translationsByText[sourceText] = refreshed;
                    else
                        _translationsByText.TryRemove(sourceText, out _);
                }
            }
        }

        #endregion

        #region Utilities

        private static List<TranslationMessage>? BuildTranslations(string zh, string es, string fr)
        {
            List<TranslationMessage>? translations = null;

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
