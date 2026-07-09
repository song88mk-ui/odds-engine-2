using System;

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
    // docs/BusinessRules.md R-E6 in bet-genius). Two exceptions redirect to the fixture's own
    // Te:{HoId}/Te:{AwId} team-level key instead, because SeNa/SeTeNa fuse a volatile value with an
    // identity Te: already tracks stably:
    //  1) A handicap market (IsHa) whose selection CompId is one of the fixture's own two
    //     participants (e.g. Asian Handicap by team) — SeNa has the handicap line baked in (e.g.
    //     "Karpaty Lviv(-0.25)"), which defeats the Co: cache on every line change.
    //  2) A CompId-null selection using the generic two-way positional template ("Player 1"/
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

                return new SelectionTranslationKey($"SeTe:{maTeId}:{seTeId}", seTeNa);
            }

            if (isHa)
            {
                var compIdText = compId.Value.ToString();

                if (string.Equals(compIdText, hoId, StringComparison.Ordinal))
                    return new SelectionTranslationKey($"Te:{hoId}", hoNa);

                if (string.Equals(compIdText, awId, StringComparison.Ordinal))
                    return new SelectionTranslationKey($"Te:{awId}", awNa);
            }

            return new SelectionTranslationKey($"Co:{compId}", seNa);
        }
    }
}
