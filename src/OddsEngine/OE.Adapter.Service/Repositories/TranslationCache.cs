using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Shared.Data.Translation;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

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

        // STRING_ID -> the SOURCE_TEXT it was cached with at preload, used to detect a rename
        // (the feed now sends different English text for the same STRING_ID) so GetTranslations
        // can fall back to the content index below instead of serving a stale translation.
        private readonly ConcurrentDictionary<string, string> _sourceTextByStringId = new();

        // SOURCE_TEXT -> translation list, built once at preload from the same rows as
        // _translations. Content-addressed fallback for a STRING_ID miss or rename: reuses an
        // existing translation for identical English text under a different STRING_ID rather than
        // waiting on the external pipeline to re-translate from scratch. Case-insensitive: a
        // case-only variant is the same text — translations don't depend on English casing.
        private readonly ConcurrentDictionary<string, List<TranslationMessage>> _translationsByText = new(StringComparer.OrdinalIgnoreCase);

        // Keys whose STRING_ID hit was rejected by the rename guard and served untranslated —
        // logged once per key per process lifetime so a stale SOURCE_TEXT is visible in logs
        // instead of silently rendering English (see the 2026-07-14 'Odd Or Even Total' incident:
        // a seeded SOURCE_TEXT that mismatched the live feed name was invisible for days).
        private readonly ConcurrentDictionary<string, byte> _rejectedSourceTextLogged = new();

        // Fixed language set of the flat TranslationCache2 schema (TARGET_TEXT_ZH/_ES/_FR).
        // Adding a language is a schema change + code change here.
        private const string LANGUAGE_ZH = "ZH";
        private const string LANGUAGE_ES = "ES";
        private const string LANGUAGE_FR = "FR";

        // Winning Margin family tail shape (e.g. " by 2", " by 2+") — the one letter-bearing
        // volatile tail known to be safe to splice onto a cached name; see
        // docs/ArchitectureRisks.md AR-14. Anchored and narrow enough that a genuine rename could
        // not plausibly produce a matching tail by coincidence.
        private static readonly Regex WinningMarginTailPattern = new(@"^\s+by\s+\d+\+?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        public List<TranslationMessage> GetTranslations(string stringId, string currentText)
        {
            if (string.IsNullOrEmpty(stringId))
                return null;

            string cachedText = null;

            if (_translations.TryGetValue(stringId, out var translations))
            {
                // Only rows seen at preload have a tracked SOURCE_TEXT to compare against; rows
                // seeded purely via ON_TRANSLATION_CHANGE deltas after startup have none and are
                // trusted as-is (unchanged from pre-fallback behavior). A tracked mismatch means
                // the feed renamed this entity — fall through to the content match below instead.
                // The comparison is case-insensitive: a case-only variant ("Odd or Even Total" vs
                // the feed's "Odd Or Even Total") is the same text, not a rename — treating it as
                // one permanently orphaned valid translations (2026-07-14 prod incident).
                // An empty currentText (market/coverage-derived messages don't carry team/league
                // names — see BG.Receiver.Service.OddsFeedRepository.ProcessMarkets/ProcessCoverage)
                // is "no information to compare," not a rename — trust the cached row as-is.
                if (string.IsNullOrEmpty(currentText) || !_sourceTextByStringId.TryGetValue(stringId, out cachedText) || string.Equals(cachedText, currentText, StringComparison.OrdinalIgnoreCase))
                    return translations;

                // Co:{CompId} is shared by every selection tied to one competitor, but a
                // competitor-fused market (e.g. a set/game score prop: "Rogers, Anna 7-5", or a
                // Winning Margin market: "Fluminense FC by 2") appends a volatile, untranslatable
                // tail onto the same stable name another selection already cached here ("Rogers,
                // Anna", "Fluminense FC"). Reuse the translation when the incoming text is
                // exactly that cached name plus a literal tail — guarded to tails with no letters
                // (a pure score/margin digit), or the narrow Winning Margin " by N"/" by N+" shape,
                // so a genuine rename to an unrelated name that coincidentally shares a prefix
                // still falls through to the rename path below instead of being spliced.
                if (!string.IsNullOrEmpty(cachedText) && currentText.StartsWith(cachedText, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = currentText.Substring(cachedText.Length);

                    if (!suffix.Any(char.IsLetter) || WinningMarginTailPattern.IsMatch(suffix))
                        return translations.Select(t => new TranslationMessage { LaCo = t.LaCo, Tx = t.Tx + suffix }).ToList();
                }
            }

            // STRING_ID miss, or the row's cached SOURCE_TEXT no longer matches (a rename) — fall
            // back to a content match so identical English text elsewhere isn't re-translated from
            // scratch.
            if (!string.IsNullOrEmpty(currentText) && _translationsByText.TryGetValue(currentText, out var byText))
                return byText;

            // Market TEMPLATE wording drifts constantly in the feed ("Over/Under" <->
            // "Total Goals Over / Under") and even oscillates between spellings — refusing to serve
            // on mismatch rendered whole market groups in English for hours (2026-07-16 prod).
            // For MaTe: keys, serve the LAST KNOWN translation instead: a slightly stale market
            // name beats English, the mislabel risk that justifies the strict guard is a
            // team/competitor-name concern (Te:/Co:/SeTe: stay strict), and the seeding path has
            // already re-queued the new wording — the retranslation replaces this fallback via
            // ON_TRANSLATION_CHANGE within minutes.
            if (translations != null && stringId.StartsWith("MaTe:", StringComparison.Ordinal))
            {
                if (_rejectedSourceTextLogged.TryAdd(stringId, 0))
                    _logger.LogWarning($"Translation for {stringId}: cached SOURCE_TEXT '{cachedText}' differs from live text '{currentText}' — serving last known translation until the re-translation lands");

                return translations;
            }

            // A translated row exists for this key but was rejected as renamed and nothing else
            // matched — the entity renders untranslated until the pipeline re-translates it.
            if (translations != null && _rejectedSourceTextLogged.TryAdd(stringId, 0))
                _logger.LogWarning($"Translation for {stringId} not served: cached SOURCE_TEXT '{cachedText}' no longer matches live text '{currentText}' — rendering untranslated until re-translation");

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
                        // Copy-on-write: the current list may be referenced by in-flight messages.
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
