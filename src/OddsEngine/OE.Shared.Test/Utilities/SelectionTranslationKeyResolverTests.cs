using OE.Shared.Utilities;

namespace OE.Shared.Test.Utilities
{
    // Pins the Co:/SeTe:/Te: decision shared by OE.Adapter.Service.TradingRepository (live
    // enrichment) and OE.Translation.Service.MongoTranslationRepository (Mongo persistence) — see
    // SelectionTranslationKeyResolver for the full rationale.
    public class SelectionTranslationKeyResolverTests
    {
        [Fact]
        public void CompetitorSelection_NonHandicap_UsesCoKeyedOffSeNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: 810068, seNa: "Schwartzman, DS", seTeNa: "Winner", seTeId: "1", maTeId: "16", isHa: false,
                hoId: "H1", hoNa: "Arsenal", awId: "A1", awNa: "Chelsea");

            Assert.Equal("Co:810068", key.StringId);
            Assert.Equal("Schwartzman, DS", key.SourceText);
        }

        [Fact]
        public void HandicapSelection_CompIdMatchesHomeParticipant_UsesTeKeyedOffHoNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: 116180, seNa: "Karpaty Lviv(-0.25)", seTeNa: "Team 1", seTeId: "1", maTeId: "82", isHa: true,
                hoId: "116180", hoNa: "Karpaty Lviv", awId: "129527", awNa: "CFR Cluj");

            Assert.Equal("Te:116180", key.StringId);
            Assert.Equal("Karpaty Lviv", key.SourceText);
        }

        [Fact]
        public void HandicapSelection_CompIdMatchesAwayParticipant_UsesTeKeyedOffAwNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: 129527, seNa: "CFR Cluj(+0.25)", seTeNa: "Team 2", seTeId: "2", maTeId: "82", isHa: true,
                hoId: "116180", hoNa: "Karpaty Lviv", awId: "129527", awNa: "CFR Cluj");

            Assert.Equal("Te:129527", key.StringId);
            Assert.Equal("CFR Cluj", key.SourceText);
        }

        // A named individual competitor who isn't one of the fixture's own two participants (e.g.
        // an outright market referencing a third party) is left on Co:, even on a handicap market.
        [Fact]
        public void HandicapSelection_CompIdMatchesNeitherParticipant_FallsBackToCoKey()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: 999999, seNa: "Some Other Entity(-0.25)", seTeNa: "Team 1", seTeId: "1", maTeId: "82", isHa: true,
                hoId: "116180", hoNa: "Karpaty Lviv", awId: "129527", awNa: "CFR Cluj");

            Assert.Equal("Co:999999", key.StringId);
            Assert.Equal("Some Other Entity(-0.25)", key.SourceText);
        }

        // 2026-07-09 fix: the vendor never attaches a CompId to individual-sport head-to-head
        // markets (tennis/darts/snooker Match Winner, Set/Game Handicap, Frame Winner, ...) — SeNa
        // still carries the real per-fixture name, but SeTeNa is the generic positional template.
        [Fact]
        public void CompIdNullPositionalTemplate_Player1_UsesTeKeyedOffHoNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Yamalapalli, S(-1.50)", seTeNa: "Player 1", seTeId: "25", maTeId: "8660", isHa: true,
                hoId: "1291254", hoNa: "Yamalapalli, S", awId: "1372197", awNa: "Rogers, Anna");

            Assert.Equal("Te:1291254", key.StringId);
            Assert.Equal("Yamalapalli, S", key.SourceText);
        }

        [Fact]
        public void CompIdNullPositionalTemplate_Player2_UsesTeKeyedOffAwNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Rogers, Anna", seTeNa: "Player 2", seTeId: "26", maTeId: "6505", isHa: false,
                hoId: "1291254", hoNa: "Yamalapalli, S", awId: "1372197", awNa: "Rogers, Anna");

            Assert.Equal("Te:1372197", key.StringId);
            Assert.Equal("Rogers, Anna", key.SourceText);
        }

        [Fact]
        public void CompIdNullOtherTemplate_FallsBackToSeTeKey()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Over", seTeNa: "Over", seTeId: "30", maTeId: "16", isHa: false,
                hoId: "H1", hoNa: "Arsenal", awId: "A1", awNa: "Chelsea");

            Assert.Equal("SeTe:16:30", key.StringId);
            Assert.Equal("Over", key.SourceText);
        }

        // Exact match only — an unrecognized variant (different casing, "Team 1", etc.) must not
        // be silently treated as the positional template, since only "Player 1"/"Player 2" are
        // vendor-confirmed for this exception.
        [Fact]
        public void CompIdNullPositionalTemplate_CaseMismatch_FallsBackToSeTeKey()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Yamalapalli, S(-1.50)", seTeNa: "player 1", seTeId: "25", maTeId: "8660", isHa: true,
                hoId: "1291254", hoNa: "Yamalapalli, S", awId: "1372197", awNa: "Rogers, Anna");

            Assert.Equal("SeTe:8660:25", key.StringId);
            Assert.Equal("player 1", key.SourceText);
        }

        // A generic-template selection whose SeTeNa is a bare vendor outcome code with no
        // descriptive text at all (e.g. some Correct Score outcomes with no CompId) must not be
        // cached/translated: the digit string round-trips through translation unchanged and would
        // permanently mask the correctly-composed English SeNa fallback ("Stade Rennais 4-0").
        [Fact]
        public void CompIdNullNonDescriptiveNumericSeTeNa_ReturnsEmptyKey()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Stade Rennais 4-0", seTeNa: "29", seTeId: "29", maTeId: "91", isHa: false,
                hoId: "H1", hoNa: "Stade Rennais", awId: "A1", awNa: "SM Caen");

            Assert.Equal(string.Empty, key.StringId);
            Assert.Equal(string.Empty, key.SourceText);
        }

        // An empty/missing SeTeNa is a DIFFERENT case from non-descriptive digits — it means "no
        // name info on this message," which GetTranslations already treats as "no info to compare,
        // trust whatever's cached" (see TranslationCache.GetTranslations remarks). The guard above
        // must not swallow this — it still needs the real SeTe: key so a prior cached translation
        // keeps applying.
        [Fact]
        public void CompIdNullEmptySeTeNa_StillFallsBackToSeTeKey()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Stade Rennais 4-0", seTeNa: "", seTeId: "29", maTeId: "91", isHa: false,
                hoId: "H1", hoNa: "Stade Rennais", awId: "A1", awNa: "SM Caen");

            Assert.Equal("SeTe:91:29", key.StringId);
            Assert.Equal(string.Empty, key.SourceText);
        }

        // 2026-07-10 fix: a CompId-null selection whose SeTeNa is actually the fixture's own team
        // name fused with a volatile score (the vendor didn't attach a CompId for this specific
        // Correct Score outcome) must not be cached under the generic SeTe: bucket — verified
        // against live SQL data that the same SeTeId can mean "Draw 1-1" in one fixture and a
        // specific team's own scoreline in another, so a different fixture landing on the same slot
        // would otherwise inherit THIS fixture's club name.
        [Fact]
        public void CompIdNullSeTeNaStartsWithHomeTeamName_UsesTeKeyedOffFullSeTeNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Standard de Liège 2-0", seTeNa: "Standard de Liège 2-0", seTeId: "173", maTeId: "91", isHa: false,
                hoId: "H1", hoNa: "Standard de Liège", awId: "A1", awNa: "Club Brugge");

            Assert.Equal("Te:H1", key.StringId);
            Assert.Equal("Standard de Liège 2-0", key.SourceText);
        }

        [Fact]
        public void CompIdNullSeTeNaStartsWithAwayTeamName_UsesTeKeyedOffFullSeTeNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Club Brugge 1-0", seTeNa: "Club Brugge 1-0", seTeId: "51", maTeId: "91", isHa: false,
                hoId: "H1", hoNa: "Standard de Liège", awId: "A1", awNa: "Club Brugge");

            Assert.Equal("Te:A1", key.StringId);
            Assert.Equal("Club Brugge 1-0", key.SourceText);
        }

        // A generic template phrase that merely shares a leading word with a team name must not be
        // misdetected — "Draw" never equals a real team name, so this stays on SeTe: as before.
        [Fact]
        public void CompIdNullSeTeNaDoesNotStartWithEitherTeamName_FallsBackToSeTeKey()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: null, seNa: "Draw 1-1", seTeNa: "Draw 1-1", seTeId: "11", maTeId: "91", isHa: false,
                hoId: "H1", hoNa: "Standard de Liège", awId: "A1", awNa: "Club Brugge");

            Assert.Equal("SeTe:91:11", key.StringId);
            Assert.Equal("Draw 1-1", key.SourceText);
        }

        // 2026-07-10 fix: a non-handicap market whose selection CompId matches the fixture's own
        // home participant (e.g. Correct Score's "this team leads 1-0") is redirected to Te:{HoId}
        // the same way the handicap case already was — but keyed off the FULL fused SeNa (not the
        // bare team name), since there's no market-level Li field for Compose to append the score
        // separately (Compose is a no-op for non-handicap markets). This lets TranslationCache's
        // splice reunite the team's stable translation with its own volatile score suffix. Verified
        // against live production data: Co:{CompId}'s cached SOURCE_TEXT can only ever hold one
        // scoreline at a time (one CompId, many sibling selections), so every other scoreline either
        // missed or — worse — collided with an unrelated fixture sharing the same CompId/SeTeId and
        // spliced on THAT fixture's cached name instead.
        [Fact]
        public void NonHandicapSelection_CompIdMatchesHomeParticipant_UsesTeKeyedOffFullSeNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: 10032, seNa: "AS Roma 1-0", seTeNa: "AS Roma 1-0", seTeId: "11", maTeId: "91", isHa: false,
                hoId: "10032", hoNa: "AS Roma", awId: "10170", awNa: "Fiorentina");

            Assert.Equal("Te:10032", key.StringId);
            Assert.Equal("AS Roma 1-0", key.SourceText); // full fused text, NOT bare "AS Roma"
        }

        [Fact]
        public void NonHandicapSelection_CompIdMatchesAwayParticipant_UsesTeKeyedOffFullSeNa()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: 10170, seNa: "Fiorentina 2-1", seTeNa: "Fiorentina 2-1", seTeId: "29", maTeId: "91", isHa: false,
                hoId: "10032", hoNa: "AS Roma", awId: "10170", awNa: "Fiorentina");

            Assert.Equal("Te:10170", key.StringId);
            Assert.Equal("Fiorentina 2-1", key.SourceText);
        }

        // A competitor-specific, non-handicap selection whose CompId matches NEITHER fixture
        // participant (e.g. an outright market referencing a third party) stays on Co: — unchanged
        // from CompetitorSelection_NonHandicap_UsesCoKeyedOffSeNa above; this pins the boundary
        // explicitly against the new Te: redirect so the two don't get confused.
        [Fact]
        public void NonHandicapSelection_CompIdMatchesNeitherParticipant_FallsBackToCoKey()
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                compId: 999999, seNa: "Some Other Entity 1-0", seTeNa: "Some Other Entity 1-0", seTeId: "11", maTeId: "91", isHa: false,
                hoId: "10032", hoNa: "AS Roma", awId: "10170", awNa: "Fiorentina");

            Assert.Equal("Co:999999", key.StringId);
            Assert.Equal("Some Other Entity 1-0", key.SourceText);
        }
    }
}
