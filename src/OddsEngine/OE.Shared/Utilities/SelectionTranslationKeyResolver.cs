using System;
using System.Linq;

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

                // A generic template label is only worth caching/translating if it's real
                // descriptive text. Some vendor market templates (e.g. Correct Score outcomes
                // with no CompId) fall back to a bare numeric outcome code in SeTeNa when the
                // vendor never supplied a proper name. That code round-trips through translation
                // unchanged (digits need no translating), gets cached as if it were a genuine
                // translation, and permanently masks the correctly-composed English SeNa. Return
                // an empty key so the caller's existing "no StringId" fallback (SeNa as-is)
                // applies for every language instead. An EMPTY SeTeNa is a different, already
                // -handled case (no information on this message — GetTranslations trusts the
                // existing cached row per its own "no info to compare" rule) and must fall
                // through to the normal SeTe: key untouched.
                if (!string.IsNullOrEmpty(seTeNa) && !seTeNa.Any(char.IsLetter))
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
}
