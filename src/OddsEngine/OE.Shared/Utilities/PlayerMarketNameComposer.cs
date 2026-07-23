using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OE.Shared.Messages.OutgoingAdapterMessages;

namespace OE.Shared.Utilities
{
    // Composes the translated display name of a per-player prop market (GM-528). Two strategies,
    // best first:
    //
    // 1. INTERPOLATION (preferred): a placeholder template row ("MaTeP:{MaTeId}", SOURCE_TEXT
    //    derived from the vendor's generic template by replacing its player-slot phrase with
    //    {player} — e.g. "1st Innings {player} to Reach 50 Runs Yes/No") is translated with the
    //    marker preserved ("第一局{player}达到50分 是/否"), and the player's Pl:{PlId}
    //    translation is substituted in — natural word order, fully translated. Guard: a language
    //    whose translation lost the marker is skipped (translators/DeepL can mangle it).
    // 2. JOIN (fallback): "{Pl: translation}{Separator}{MaTe: translation}" — token-swapping the
    //    translated MaTe: template is impossible (real TranslationCache2 data shows translators
    //    restructure the sentence and render "Player" as 球员/选手/人员 inconsistently), so the
    //    two halves are joined deterministically.
    //
    // Per language: interpolation wins, join fills languages interpolation couldn't produce, and
    // a language with neither stays absent. No output at all -> consumers fall back to the
    // English MaNa, which is already player-inclusive.
    //
    // Every MaNaTr/MaTeTr producer for a player market must compose through here identically:
    // OE.Adapter.Service (wire enrichment), OE.Translation.Service (Mongo stamp, backfill, and
    // the Pl:/MaTe:/MaTeP: delta recompose paths).
    public static class PlayerMarketNameComposer
    {
        public const string Separator = " - ";
        public const string PlayerToken = "{player}";

        // The vendor's own generic-template convention: the player slot reads "Individual Player"
        // or "Player" (verified against provider samples and prod MarketType names). Exactly ONE
        // slot must match. The negative lookahead excludes POSITIONAL slots that are not roster
        // players — "Player 1"/"Player 2" (tennis sides) and "Player A"/"Player B" (two-player
        // cricket templates) — which keep the join-format fallback.
        private static readonly Regex PlayerSlotPattern = new Regex(
            @"\b(?:Individual\s+)?Player\b(?!\s+(?:[A-Z]|\d+)\b)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Derives the {player} placeholder wording from the vendor's generic template name.
        // Returns null when the template has no single recognizable player slot.
        public static string DerivePlaceholderSource(string marketTemplateName)
        {
            if (string.IsNullOrWhiteSpace(marketTemplateName))
                return null;

            var matches = PlayerSlotPattern.Matches(marketTemplateName);

            if (matches.Count != 1)
                return null;

            return PlayerSlotPattern.Replace(marketTemplateName, PlayerToken, 1);
        }

        public static List<TranslationMessage> Compose(
            List<TranslationMessage> playerTranslations,
            List<TranslationMessage> templateTranslations,
            List<TranslationMessage> placeholderTemplateTranslations = null)
        {
            var playerByLanguage = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var player in playerTranslations ?? Enumerable.Empty<TranslationMessage>())
            {
                if (player != null && !string.IsNullOrEmpty(player.LaCo) && !string.IsNullOrEmpty(player.Tx))
                    playerByLanguage[player.LaCo] = player.Tx;
            }

            if (playerByLanguage.Count == 0)
                return null;

            var composed = new List<TranslationMessage>();
            var coveredLanguages = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            // Interpolation first — only languages whose translated placeholder text kept the marker.
            foreach (var template in placeholderTemplateTranslations ?? Enumerable.Empty<TranslationMessage>())
            {
                if (template == null || string.IsNullOrEmpty(template.LaCo) || string.IsNullOrEmpty(template.Tx)
                    || !template.Tx.Contains(PlayerToken)
                    || !playerByLanguage.TryGetValue(template.LaCo, out var playerName)
                    || !coveredLanguages.Add(template.LaCo))
                {
                    continue;
                }

                composed.Add(new TranslationMessage { LaCo = template.LaCo, Tx = template.Tx.Replace(PlayerToken, playerName) });
            }

            // Join fallback for languages interpolation couldn't produce.
            foreach (var template in templateTranslations ?? Enumerable.Empty<TranslationMessage>())
            {
                if (template == null || string.IsNullOrEmpty(template.LaCo) || string.IsNullOrEmpty(template.Tx)
                    || !playerByLanguage.TryGetValue(template.LaCo, out var playerName)
                    || !coveredLanguages.Add(template.LaCo))
                {
                    continue;
                }

                composed.Add(new TranslationMessage { LaCo = template.LaCo, Tx = $"{playerName}{Separator}{template.Tx}" });
            }

            return composed.Count > 0 ? composed : null;
        }
    }
}
