using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using OE.Shared.Messages.OutgoingAdapterMessages;

namespace OE.Shared.Utilities
{
    // TranslationCache2 STRING_ID + source text to resolve a selection's SeNaTr, shared by
    // OE.Adapter.Service.Repositories.TradingRepository (live enrichment) and
    // OE.Translation.Service.Repositories.MongoTranslationRepository (Mongo persistence) — the two
    // services keep separate ITranslationCache implementations (see each TranslationCache.cs "keep
    // in sync" note), so this only shares the pure "which key" decision, not the cache lookup itself.
    public struct SelectionTranslationKey
    {
        public string StringId { get; }
        public string SourceText { get; }

        public SelectionTranslationKey(string stringId, string sourceText)
        {
            StringId = stringId;
            SourceText = sourceText;
        }
    }

    // Co:{CompId} and SeTe:{MaTeId}:{SeTeId} are mutually exclusive per selection (see
    // docs/BusinessRules.md R-E6 in bet-genius). Three exceptions redirect to the fixture's own
    // Te:{HoId}/Te:{AwId} team-level key instead, because SeNa/SeTeNa fuse a volatile value with an
    // identity Te: already tracks stably:
    //  1) A handicap market (IsHa) whose selection CompId is one of the fixture's own two
    //     participants (e.g. Asian Handicap by team) — SeNa has the handicap line baked in (e.g.
    //     "Karpaty Lviv(-0.25)"), which defeats the Co: cache on every line change. The market's own
    //     Li field carries the line and gets appended downstream by SelectionNameComposer.Compose
    //     on the read side, so the cached source text here is just the team's stable name (hoNa/
    //     awNa) — Compose already supplies the volatile part.
    //  2) A non-handicap market whose selection CompId is one of the fixture's own two participants
    //     (e.g. Correct Score's "this team leads 1-0" outcomes) — SeNa fuses the team name with a
    //     per-selection volatile value that ISN'T a market-level field (unlike a handicap line, so
    //     Compose can't append it; it's a no-op for non-handicap markets). Verified against live
    //     data (2026-07-10): BetGenius attaches the real CompId here, but Correct Score has many
    //     selections sharing one CompId — one per scoreline — and Co:{CompId}'s cached SOURCE_TEXT
    //     can only ever hold ONE of those fused strings at a time, so every sibling scoreline either
    //     misses (falls back to English — safe) or, worse, collides with a DIFFERENT fixture's
    //     unrelated selection that happens to share the same CompId/SeTeId and gets its wrong cached
    //     name spliced on instead (root cause of a real production bug: "Santos Laguna 1-0"
    //     rendering as an unrelated Polish/Belgian/Brazilian club's name). Redirecting to
    //     Te:{HoId}/{AwId} keyed off the FULL fused SeNa lets TranslationCache's existing
    //     "cached name + volatile digit tail" splice (already proven for Co: — see
    //     TranslationCacheTests.GetTranslations_RenameIsKnownNamePlusScoreTail_SplicesTranslatedNamePlusTail)
    //     append the score onto the team's own stable, fixture-level translation instead.
    //  3) A CompId-null selection using the generic two-way positional template ("Player 1"/
    //     "Player 2" — the vendor never attaches a CompId for these individual-sport market
    //     templates, such as tennis/darts/snooker Match Winner, Set/Game Handicap, Frame Winner),
    //     even though SeNa still carries the real per-fixture name as free text. SeTeId 25/26 are
    //     the vendor's fixed encoding of this template's two positions.
    public static class SelectionTranslationKeyResolver
    {
        public const string PositionalPlayer1Template = "Player 1";
        public const string PositionalPlayer2Template = "Player 2";

        public static SelectionTranslationKey Resolve(
            int? compId,
            string seNa,
            string seTeNa,
            string seTeId,
            string maTeId,
            bool isHa,
            string hoId,
            string hoNa,
            string awId,
            string awNa)
        {
            if (compId == null)
            {
                if (seTeNa == PositionalPlayer1Template)
                    return new SelectionTranslationKey($"Te:{hoId}", hoNa);

                if (seTeNa == PositionalPlayer2Template)
                    return new SelectionTranslationKey($"Te:{awId}", awNa);

                // 2026-07-10: a CompId-null selection whose SeTeNa is actually this fixture's own
                // team name fused with a volatile per-selection value (e.g. Correct Score's
                // "Standard de Liège 2-0" when the vendor didn't attach a CompId for this specific
                // outcome) must not be cached under the generic, cross-fixture-shared
                // SeTe:{MaTeId}:{SeTeId} bucket — Correct Score's outcome numbering isn't stable
                // across fixtures (verified against live SQL data: the same SeTeId means "Draw 1-1"
                // for one fixture and a specific team's own scoreline for another), so a different
                // fixture landing on the same MaTeId/SeTeId pair would inherit THIS fixture's team
                // name instead of its own — the same root cause as the CompId-present case above,
                // just without a CompId to key off. Redirect to the stable Te:{HoId}/{AwId} key,
                // keyed off the full SeTeNa so the existing splice reunites the team's translation
                // with its own suffix (mirrors the CompId-present non-handicap case; Correct Score
                // is never a handicap market, so there's no Compose double-append risk here either).
                if (!string.IsNullOrEmpty(hoNa) && seTeNa != null && seTeNa.StartsWith(hoNa, StringComparison.Ordinal))
                    return new SelectionTranslationKey($"Te:{hoId}", seTeNa);

                if (!string.IsNullOrEmpty(awNa) && seTeNa != null && seTeNa.StartsWith(awNa, StringComparison.Ordinal))
                    return new SelectionTranslationKey($"Te:{awId}", seTeNa);

                // A digit-only SeTeNa is only a problem when it's a MEANINGLESS internal outcome
                // code standing in for text the vendor failed to supply — detected by SeTeNa not
                // matching the actual composed SeNa (e.g. Correct Score's SeNa "Evans, L 4-1" vs.
                // SeTeNa "41": verified live, these come from the same market template and the same
                // CompId-null shape, but "41" bears no relation to the real selection). That code
                // round-trips through translation unchanged (digits need no translating), gets
                // cached as if it were a genuine translation, and permanently masks the
                // correctly-composed English SeNa. Return an empty key so the caller's existing "no
                // StringId" fallback (SeNa as-is) applies for every language instead.
                //
                // A digit-only SeTeNa that MATCHES SeNa exactly is a different, legitimate case:
                // markets like Total Corners ("12+"), Total Goals Bands ("2-3"), or Next Goal Time
                // ("81-90") use a numeric range as their actual, complete, fixture-independent
                // selection text — verified live, SeNa == SeTeNa for these, the same way it does
                // for genuine generic templates like "Draw 0-0". There's nothing "meaningless" to
                // guard against here, and blocking it would wrongly prevent these otherwise-stable
                // templates from ever being cached/translated.
                //
                // An EMPTY SeTeNa is a third, already-handled case (no information on this message
                // — GetTranslations trusts the existing cached row per its own "no info to compare"
                // rule) and must fall through to the normal SeTe: key untouched.
                if (!string.IsNullOrEmpty(seTeNa) && !seTeNa.Any(char.IsLetter) && seTeNa != seNa)
                    return new SelectionTranslationKey(string.Empty, string.Empty);

                return new SelectionTranslationKey($"SeTe:{maTeId}:{seTeId}", seTeNa);
            }

            var compIdText = compId.Value.ToString();
            var matchesHome = string.Equals(compIdText, hoId, StringComparison.Ordinal);
            var matchesAway = string.Equals(compIdText, awId, StringComparison.Ordinal);

            if (matchesHome || matchesAway)
            {
                // Handicap: cache just the team's stable name — Compose appends the market's Li
                // downstream, so the splice must NOT fire here (it would double-append the line).
                // Non-handicap: cache the full fused SeNa — there's no market-level field for the
                // volatile part, and Compose is a no-op for non-handicap markets, so the splice is
                // the only thing that can reunite the translated team name with its own suffix.
                return matchesHome
                    ? new SelectionTranslationKey($"Te:{hoId}", isHa ? hoNa : seNa)
                    : new SelectionTranslationKey($"Te:{awId}", isHa ? awNa : seNa);
            }

            return new SelectionTranslationKey($"Co:{compId}", seNa);
        }
    }

    // Companion to SelectionTranslationKeyResolver for the SeTe: bucket it can't personalize.
    //
    // A positional token template ("Home or Draw", "Home or Away", "Draw or Away", the
    // "Home or Draw and Yes/No/Over/Under" combos, ...) stays keyed on the fixture-independent
    // SeTe:{MaTeId}:{SeTeId} row — that sharing is what keeps TranslationCache2 small — but the
    // English SeNa the vendor composes from it carries the fixture's own team names ("Korona
    // Kielce or Draw"), so serving the raw translated template made non-EN read generic
    // (主队胜或平局) where EN read team-specific. This composer swaps the translated positional
    // tokens (ZH 主队/客队) for the fixture's own translated team names (the Te:{HoId}/Te:{AwId}
    // rows) AFTER the template lookup, on the translated text:
    //  - substituting on the ENGLISH side instead would produce a per-fixture source string that
    //    has no TranslationCache2 row — the exact miss this design avoids;
    //  - a token per language is only replaced when the English SeTeNa contains the matching
    //    positional word AND that team's translation exists for that language; anything else
    //    keeps the generic template text (today's behavior is the floor, never worse);
    //  - always composes from the cached template translation, never from previously composed
    //    output, so re-stamping is idempotent;
    //  - returns NEW lists/items — cache lists are shared copy-on-write snapshots handed to
    //    in-flight writes (see each service's TranslationCache) and must never be mutated.
    //
    // Every producer of a selection's SeNaTr must run this or the surfaces diverge (Mongo REST
    // reads vs. the ungated websocket *Tr deltas): OE.Adapter.Service.TradingRepository
    // (enrichment) and OE.Translation.Service.MongoTranslationRepository (live stamp, backfill,
    // team cascade, ON_TRANSLATION_CHANGE persistence + payload).
    public static class SelectionTranslationComposer
    {
        // Whole-word English trigger, mirrored server-side by PositionalWordPattern in Mongo
        // filters — keep the two in sync.
        public const string PositionalWordPattern = @"\b(Home|Away)\b";

        private static readonly Regex PositionalWord = new Regex(PositionalWordPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HomeWord = new Regex(@"\bHome\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AwayWord = new Regex(@"\bAway\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // How the pipeline's translations render the positional words, per TARGET_TEXT_* language.
        // ZH only today — ES/FR are not actively translated (TARGET_TEXT_ES/_FR empty by design),
        // and an unmapped language passes through untouched. Same hardcoded-language precedent as
        // the fixed LANGUAGE_ZH/ES/FR set in each service's TranslationCache.
        private static readonly Dictionary<string, (string HomeToken, string AwayToken)> PositionalTokensByLanguage =
            new Dictionary<string, (string HomeToken, string AwayToken)>(StringComparer.OrdinalIgnoreCase)
            {
                ["ZH"] = ("主队", "客队")
            };

        // One alternation regex per language, single-pass so replacement text is never rescanned
        // (a team translation that happens to contain the other language token must not cascade).
        private static readonly Dictionary<string, Regex> TokenPatternByLanguage =
            new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase)
            {
                ["ZH"] = new Regex("主队|客队", RegexOptions.Compiled)
            };

        // True when this selection's translation came from the shared SeTe: bucket AND its English
        // template carries a positional word worth personalizing. CompId-null is implied: the
        // resolver only ever returns a SeTe: key from its CompId-null branch.
        public static bool RequiresTeamTokenComposition(string? stringId, string? seTeNa)
        {
            return stringId != null
                && stringId.StartsWith("SeTe:", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(seTeNa)
                && PositionalWord.IsMatch(seTeNa);
        }

        // Swaps each language's positional tokens for that language's translated team names.
        // homeTranslations/awayTranslations are the Te:{HoId}/Te:{AwId} lookups from the SAME
        // cache the template came from; pass null when a team has no translations at all.
        public static List<TranslationMessage>? ComposeTeamTokens(
            List<TranslationMessage>? templateTranslations,
            string? seTeNa,
            List<TranslationMessage>? homeTranslations,
            List<TranslationMessage>? awayTranslations)
        {
            if (templateTranslations == null || templateTranslations.Count == 0 || string.IsNullOrEmpty(seTeNa))
                return templateTranslations;

            var hasHomeWord = HomeWord.IsMatch(seTeNa);
            var hasAwayWord = AwayWord.IsMatch(seTeNa);

            if (!hasHomeWord && !hasAwayWord)
                return templateTranslations;

            var composed = new List<TranslationMessage>(templateTranslations.Count);

            foreach (var template in templateTranslations)
                composed.Add(new TranslationMessage { LaCo = template.LaCo, Tx = ComposeText(template, hasHomeWord, hasAwayWord, homeTranslations, awayTranslations) });

            return composed;
        }

        private static string ComposeText(
            TranslationMessage template,
            bool hasHomeWord,
            bool hasAwayWord,
            List<TranslationMessage>? homeTranslations,
            List<TranslationMessage>? awayTranslations)
        {
            if (string.IsNullOrEmpty(template.Tx)
                || template.LaCo == null
                || !PositionalTokensByLanguage.TryGetValue(template.LaCo, out var tokens)
                || !TokenPatternByLanguage.TryGetValue(template.LaCo, out var tokenPattern))
                return template.Tx;

            var homeName = hasHomeWord ? FindText(homeTranslations, template.LaCo) : null;
            var awayName = hasAwayWord ? FindText(awayTranslations, template.LaCo) : null;

            if (homeName == null && awayName == null)
                return template.Tx;

            return tokenPattern.Replace(template.Tx, match =>
                match.Value == tokens.HomeToken && homeName != null ? homeName
                : match.Value == tokens.AwayToken && awayName != null ? awayName
                : match.Value);
        }

        private static string? FindText(List<TranslationMessage>? translations, string languageCode)
        {
            if (translations == null)
                return null;

            var text = translations.FirstOrDefault(t => string.Equals(t.LaCo, languageCode, StringComparison.OrdinalIgnoreCase))?.Tx;

            return string.IsNullOrEmpty(text) ? null : text;
        }
    }
}
