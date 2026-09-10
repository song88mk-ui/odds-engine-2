using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;

namespace OE.Shared.Test.Utilities
{
    // Pins the positional token composition shared by OE.Adapter.Service.TradingRepository (live
    // enrichment) and OE.Translation.Service.MongoTranslationRepository (Mongo stamp/backfill/
    // cascade/change path) — see SelectionTranslationComposer for the full rationale. Reference
    // data is the 2026-07-14 production case: fixture "Korona Kielce vs. Stal Rzeszów", Double
    // Chance (MaTe:7202), whose ZH selections rendered the generic 主队胜或平局 instead of the
    // fixture's own team names.
    public class SelectionTranslationComposerTests
    {
        private static List<TranslationMessage> Zh(string text) =>
            new List<TranslationMessage> { new TranslationMessage { LaCo = "ZH", Tx = text } };

        #region RequiresTeamTokenComposition — the trigger

        [Theory]
        [InlineData("Home or Draw")]
        [InlineData("Home or Away")]
        [InlineData("Draw or Away")]
        [InlineData("HOME OR DRAW")] // title-case drift exists in live SeTeNa — trigger is case-insensitive
        [InlineData("Home or Draw and Yes")]
        [InlineData("Draw and Away to Score First")]
        public void RequiresTeamTokenComposition_SeTeKeyWithPositionalWord_IsTrue(string seTeNa)
        {
            Assert.True(SelectionTranslationComposer.RequiresTeamTokenComposition("SeTe:7202:7", seTeNa));
        }

        [Theory]
        [InlineData("Draw")]
        [InlineData("Draw 1-1")]
        [InlineData("Over")]
        [InlineData("Homer Simpson")] // whole words only — "Homer" must not trigger
        [InlineData("")]
        [InlineData(null)]
        public void RequiresTeamTokenComposition_SeTeKeyWithoutPositionalWord_IsFalse(string seTeNa)
        {
            Assert.False(SelectionTranslationComposer.RequiresTeamTokenComposition("SeTe:7202:2", seTeNa));
        }

        // Only the shared SeTe: bucket needs personalizing — Te:/Co: results already ARE the
        // team/competitor's own translation, and the empty key means "don't translate at all".
        [Theory]
        [InlineData("Te:117198")]
        [InlineData("Co:810068")]
        [InlineData("")]
        [InlineData(null)]
        public void RequiresTeamTokenComposition_NonSeTeKey_IsFalse(string stringId)
        {
            Assert.False(SelectionTranslationComposer.RequiresTeamTokenComposition(stringId, "Home or Draw"));
        }

        #endregion

        #region ComposeTeamTokens — the production Double Chance triple

        [Fact]
        public void Compose_HomeOrDraw_SwapsHomeTokenForTranslatedTeamName()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("主队胜或平局"), "Home or Draw", Zh("凯尔采"), Zh("斯塔尔热舒夫"));

            Assert.Equal("凯尔采胜或平局", composed!.Single().Tx);
        }

        [Fact]
        public void Compose_HomeOrAway_SwapsBothTokens()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("主队或客队"), "Home or Away", Zh("凯尔采"), Zh("斯塔尔热舒夫"));

            Assert.Equal("凯尔采或斯塔尔热舒夫", composed!.Single().Tx);
        }

        [Fact]
        public void Compose_DrawOrAway_SwapsAwayTokenOnly()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("平局或客队"), "Draw or Away", homeTranslations: null, awayTranslations: Zh("斯塔尔热舒夫"));

            Assert.Equal("平局或斯塔尔热舒夫", composed!.Single().Tx);
        }

        #endregion

        #region ComposeTeamTokens — graceful floors (never worse than the generic template)

        [Fact]
        public void Compose_TeamNotTranslated_KeepsGenericToken()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("主队胜或平局"), "Home or Draw", homeTranslations: null, awayTranslations: null);

            Assert.Equal("主队胜或平局", composed!.Single().Tx);
        }

        [Fact]
        public void Compose_TeamTranslatedInDifferentLanguageOnly_KeepsGenericToken()
        {
            var homeEsOnly = new List<TranslationMessage> { new TranslationMessage { LaCo = "ES", Tx = "Korona" } };

            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("主队胜或平局"), "Home or Draw", homeEsOnly, null);

            Assert.Equal("主队胜或平局", composed!.Single().Tx);
        }

        // English trigger word present but the translated text never rendered it as a positional
        // token (e.g. "Home Run" family) — nothing to swap, text passes through.
        [Fact]
        public void Compose_TokenAbsentFromTranslatedText_LeavesTextUntouched()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("本垒打总数"), "Home Run Total", Zh("凯尔采"), null);

            Assert.Equal("本垒打总数", composed!.Single().Tx);
        }

        // A language with no token map (ES/FR — not actively translated today) passes through.
        [Fact]
        public void Compose_UnmappedLanguage_PassesThrough()
        {
            var template = new List<TranslationMessage> { new TranslationMessage { LaCo = "ES", Tx = "Local o Empate" } };
            var homeEs = new List<TranslationMessage> { new TranslationMessage { LaCo = "ES", Tx = "Korona Kielce" } };

            var composed = SelectionTranslationComposer.ComposeTeamTokens(template, "Home or Draw", homeEs, null);

            Assert.Equal("Local o Empate", composed!.Single().Tx);
        }

        // Each token only swaps when its English word is in the template: a ZH text containing
        // 客队 while the English is "Home or Draw" keeps 客队 generic.
        [Fact]
        public void Compose_TokenWithoutMatchingEnglishWord_IsNotSwapped()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("主队或客队"), "Home or Draw", Zh("凯尔采"), Zh("斯塔尔热舒夫"));

            Assert.Equal("凯尔采或客队", composed!.Single().Tx);
        }

        // An empty Tx entry is the ON_TRANSLATION_CHANGE removal marker — it must survive
        // composition untouched so the language entry is still pulled downstream.
        [Fact]
        public void Compose_EmptyTxRemovalMarker_SurvivesUntouched()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh(""), "Home or Draw", Zh("凯尔采"), null);

            Assert.Equal("", composed!.Single().Tx);
        }

        #endregion

        #region ComposeTeamTokens — mechanics

        // Cache lists are shared copy-on-write snapshots — composition must return new instances
        // and leave every input untouched.
        [Fact]
        public void Compose_DoesNotMutateInputs()
        {
            var template = Zh("主队或客队");
            var home = Zh("凯尔采");
            var away = Zh("斯塔尔热舒夫");

            var composed = SelectionTranslationComposer.ComposeTeamTokens(template, "Home or Away", home, away);

            Assert.NotSame(template, composed);
            Assert.NotSame(template[0], composed![0]);
            Assert.Equal("主队或客队", template[0].Tx);
            Assert.Equal("凯尔采", home[0].Tx);
        }

        // Single-pass replacement: a team name that itself contains the other positional token
        // must not be re-scanned and re-replaced.
        [Fact]
        public void Compose_ReplacementTextIsNeverRescanned()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("主队或客队"), "Home or Away", Zh("X客队X"), Zh("YY"));

            Assert.Equal("X客队X或YY", composed!.Single().Tx);
        }

        // Team names apply to every occurrence of their token (e.g. "Home to Win and Home to
        // Score" shapes render the token twice).
        [Fact]
        public void Compose_ReplacesEveryOccurrenceOfAToken()
        {
            var composed = SelectionTranslationComposer.ComposeTeamTokens(
                Zh("主队进球且主队胜"), "Home to Score and Home to Win", Zh("凯尔采"), null);

            Assert.Equal("凯尔采进球且凯尔采胜", composed!.Single().Tx);
        }

        [Fact]
        public void Compose_NullOrEmptyTemplate_ReturnedAsIs()
        {
            Assert.Null(SelectionTranslationComposer.ComposeTeamTokens(null, "Home or Draw", Zh("凯尔采"), null));

            var empty = new List<TranslationMessage>();
            Assert.Same(empty, SelectionTranslationComposer.ComposeTeamTokens(empty, "Home or Draw", Zh("凯尔采"), null));
        }

        // No positional word in the English template — the exact cached list is passed through
        // (no pointless copy on the hot path).
        [Fact]
        public void Compose_NoPositionalWordInTemplate_ReturnsSameListInstance()
        {
            var template = Zh("平局");

            Assert.Same(template, SelectionTranslationComposer.ComposeTeamTokens(template, "Draw", Zh("凯尔采"), Zh("斯塔尔热舒夫")));
        }

        #endregion
    }
}
