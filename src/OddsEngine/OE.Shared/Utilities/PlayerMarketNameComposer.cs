using System.Collections.Generic;
using System.Linq;
using OE.Shared.Messages.OutgoingAdapterMessages;

namespace OE.Shared.Utilities
{
    // Composes the translated display name of a per-player prop market (GM-528): the vendor's
    // English market name embeds the player ("1st Innings Sharma, Rohit to Reach 50 Runs"), but
    // its translation cannot be produced by token-swapping the translated MaTe: template — real
    // TranslationCache2 data shows translators restructure the sentence and render "Player"
    // differently per template (球员/选手/人员), so there is no stable token to replace (unlike
    // the selection-level Home/Away tokens in SelectionTranslationComposer). Instead the name is
    // joined deterministically: "{Pl:{PlId} translation}{Separator}{MaTe:{MaTeId} translation}",
    // per language, only for languages where BOTH translations exist. When either side is
    // missing the market has no translation and consumers fall back to the English MaNa, which
    // is already player-inclusive.
    //
    // Every MaNaTr/MaTeTr producer for a player market must compose through here identically:
    // OE.Adapter.Service (wire enrichment), OE.Translation.Service (Mongo stamp, backfill, and
    // the Pl:/MaTe: delta recompose paths).
    public static class PlayerMarketNameComposer
    {
        public const string Separator = " - ";

        public static List<TranslationMessage> Compose(
            List<TranslationMessage> playerTranslations,
            List<TranslationMessage> templateTranslations)
        {
            if (playerTranslations == null || playerTranslations.Count == 0
                || templateTranslations == null || templateTranslations.Count == 0)
            {
                return null;
            }

            var playerByLanguage = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var player in playerTranslations)
            {
                if (player != null && !string.IsNullOrEmpty(player.LaCo) && !string.IsNullOrEmpty(player.Tx))
                    playerByLanguage[player.LaCo] = player.Tx;
            }

            var composed = templateTranslations
                .Where(t => t != null && !string.IsNullOrEmpty(t.LaCo) && !string.IsNullOrEmpty(t.Tx)
                            && playerByLanguage.ContainsKey(t.LaCo))
                .Select(t => new TranslationMessage { LaCo = t.LaCo, Tx = $"{playerByLanguage[t.LaCo]}{Separator}{t.Tx}" })
                .ToList();

            return composed.Count > 0 ? composed : null;
        }
    }
}
