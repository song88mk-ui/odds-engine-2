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
    }
}
