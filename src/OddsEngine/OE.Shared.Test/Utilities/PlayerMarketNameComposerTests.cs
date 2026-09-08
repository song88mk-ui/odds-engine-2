using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;

namespace OE.Shared.Test.Utilities
{
    // The composed player-market display name (GM-528): "{Pl: translation} - {MaTe: translation}"
    // per language, only where BOTH exist. Token-swapping the translated template is impossible —
    // prod TranslationCache2 shows translators restructure the sentence and render "Player"
    // differently per template (球员/选手/人员), unlike the stable Home/Away selection tokens.
    public class PlayerMarketNameComposerTests
    {
        private static List<TranslationMessage> Tr(params (string LaCo, string Tx)[] items) =>
            items.Select(i => new TranslationMessage { LaCo = i.LaCo, Tx = i.Tx }).ToList();

        [Fact]
        public void Compose_BothSidesPresent_JoinsPlayerAndTemplatePerLanguage()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "罗希特·夏尔马")),
                Tr(("ZH", "第一局首位达到50分的球员 是/否")));

            var zh = Assert.Single(composed!);
            Assert.Equal("ZH", zh.LaCo);
            Assert.Equal("罗希特·夏尔马 - 第一局首位达到50分的球员 是/否", zh.Tx);
        }

        [Fact]
        public void Compose_LanguageOnlyOnOneSide_IsDropped()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "罗希特·夏尔马")),
                Tr(("ZH", "模板"), ("ES", "plantilla")));

            var zh = Assert.Single(composed!);
            Assert.Equal("ZH", zh.LaCo); // ES dropped: player has no ES translation
        }

        [Fact]
        public void Compose_EitherSideMissing_ReturnsNull_SoEnglishMaNaWins()
        {
            var template = Tr(("ZH", "模板"));
            var player = Tr(("ZH", "球员名"));

            Assert.Null(PlayerMarketNameComposer.Compose(null, template));
            Assert.Null(PlayerMarketNameComposer.Compose(new List<TranslationMessage>(), template));
            Assert.Null(PlayerMarketNameComposer.Compose(player, null));
            Assert.Null(PlayerMarketNameComposer.Compose(player, new List<TranslationMessage>()));
        }

        [Fact]
        public void Compose_LanguageCodeMatchIsCaseInsensitive()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("zh", "球员名")),
                Tr(("ZH", "模板")));

            Assert.Equal("球员名 - 模板", Assert.Single(composed!).Tx);
        }

        [Fact]
        public void Compose_BlankTexts_AreIgnored()
        {
            Assert.Null(PlayerMarketNameComposer.Compose(
                Tr(("ZH", "")),
                Tr(("ZH", "模板"))));
        }

        // Interpolation (GM-528 phase 2): a MaTeP: placeholder translation that kept the {player}
        // marker produces a naturally-worded name; a language that lost the marker, or has no
        // placeholder row, falls back to the join format.
        [Fact]
        public void Compose_PlaceholderWithMarker_Interpolates()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "费罗扎，海鸥")),
                Tr(("ZH", "第一局首位达到50分的球员 是/否")),
                Tr(("ZH", "第一局{player}达到50分 是/否")));

            Assert.Equal("第一局费罗扎，海鸥达到50分 是/否", Assert.Single(composed!).Tx);
        }

        [Fact]
        public void Compose_PlaceholderLostTheMarker_FallsBackToJoinForThatLanguage()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "费罗扎，海鸥")),
                Tr(("ZH", "第一局首位达到50分的球员 是/否")),
                Tr(("ZH", "第一局球员达到50分 是/否"))); // DeepL mangled/translated the marker away

            Assert.Equal("费罗扎，海鸥 - 第一局首位达到50分的球员 是/否", Assert.Single(composed!).Tx);
        }

        [Fact]
        public void Compose_PlaceholderCoversOneLanguage_JoinFillsTheOthers()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "球员名"), ("ES", "Jugador X")),
                Tr(("ZH", "模板"), ("ES", "plantilla")),
                Tr(("ZH", "带{player}的模板")));

            Assert.Equal(2, composed!.Count);
            Assert.Equal("带球员名的模板", composed.Single(t => t.LaCo == "ZH").Tx);
            Assert.Equal("Jugador X - plantilla", composed.Single(t => t.LaCo == "ES").Tx);
        }

        [Fact]
        public void Compose_PlaceholderOnly_NoPlainTemplate_StillInterpolates()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "球员名")),
                templateTranslations: null,
                placeholderTemplateTranslations: Tr(("ZH", "带{player}的模板")));

            Assert.Equal("带球员名的模板", Assert.Single(composed!).Tx);
        }

        [Fact]
        public void DerivePlaceholderSource_ReplacesTheVendorPlayerSlot()
        {
            Assert.Equal("1st Innings {player} to Reach 50 Runs Yes/No",
                PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Player to Reach 50 Runs Yes/No"));
            Assert.Equal("1st Innings {player} Total Runs Over/Under",
                PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Individual Player Total Runs Over/Under"));
            Assert.Equal("1st Innings Away Team {player} Next Man Out Yes/No",
                PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Away Team Individual Player Next Man Out Yes/No"));
        }

        [Fact]
        public void DerivePlaceholderSource_NoUsableSlot_ReturnsNull()
        {
            // No player slot at all
            Assert.Null(PlayerMarketNameComposer.DerivePlaceholderSource("Match Result"));
            // Two-player template — positional "Player A"/"Player B", not roster slots
            Assert.Null(PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Player A and Player B Both to Reach 50"));
            // Tennis positional sides — "Player 1"/"Player 2" are not roster players
            Assert.Null(PlayerMarketNameComposer.DerivePlaceholderSource("Player 1 To Win A Set"));
            Assert.Null(PlayerMarketNameComposer.DerivePlaceholderSource(null));
            Assert.Null(PlayerMarketNameComposer.DerivePlaceholderSource("  "));
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void Compose_BothSidesPresent_JoinsPlayerAndTemplatePerLanguage_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "罗希特·夏尔马")),
                Tr(("ZH", "第一局首位达到50分的球员 是/否")));

            var zh = Assert.Single(composed!);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("EN", zh.LaCo));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong text", zh.Tx));
        }

        [Fact]
        public void Compose_LanguageOnlyOnOneSide_IsDropped_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "罗希特·夏尔马")),
                Tr(("ZH", "模板"), ("ES", "plantilla")));

            var zh = Assert.Single(composed!);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("ES", zh.LaCo));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(2, composed!.Count));
        }

        [Fact]
        public void Compose_EitherSideMissing_ReturnsNull_SoEnglishMaNaWins_MutationCheck()
        {
            var template = Tr(("ZH", "模板"));
            var player = Tr(("ZH", "球员名"));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.Compose(null, template)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.Compose(new List<TranslationMessage>(), template)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.Compose(player, null)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.Compose(player, new List<TranslationMessage>())));
        }

        [Fact]
        public void Compose_LanguageCodeMatchIsCaseInsensitive_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("zh", "球员名")),
                Tr(("ZH", "模板")));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", Assert.Single(composed!).Tx));
        }

        [Fact]
        public void Compose_BlankTexts_AreIgnored_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "")),
                Tr(("ZH", "模板")));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(composed));
        }

        [Fact]
        public void Compose_PlaceholderWithMarker_Interpolates_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "费罗扎，海鸥")),
                Tr(("ZH", "第一局首位达到50分的球员 是/否")),
                Tr(("ZH", "第一局{player}达到50分 是/否")));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong text", Assert.Single(composed!).Tx));
        }

        [Fact]
        public void Compose_PlaceholderLostTheMarker_FallsBackToJoinForThatLanguage_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "费罗扎，海鸥")),
                Tr(("ZH", "第一局首位达到50分的球员 是/否")),
                Tr(("ZH", "第一局球员达到50分 是/否")));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong text", Assert.Single(composed!).Tx));
        }

        [Fact]
        public void Compose_PlaceholderCoversOneLanguage_JoinFillsTheOthers_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "球员名"), ("ES", "Jugador X")),
                Tr(("ZH", "模板"), ("ES", "plantilla")),
                Tr(("ZH", "带{player}的模板")));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, composed!.Count));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", composed!.Single(t => t.LaCo == "ZH").Tx));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong", composed!.Single(t => t.LaCo == "ES").Tx));
        }

        [Fact]
        public void Compose_PlaceholderOnly_NoPlainTemplate_StillInterpolates_MutationCheck()
        {
            var composed = PlayerMarketNameComposer.Compose(
                Tr(("ZH", "球员名")),
                templateTranslations: null,
                placeholderTemplateTranslations: Tr(("ZH", "带{player}的模板")));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong text", Assert.Single(composed!).Tx));
        }

        [Fact]
        public void DerivePlaceholderSource_ReplacesTheVendorPlayerSlot_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong",
                PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Player to Reach 50 Runs Yes/No")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong",
                PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Individual Player Total Runs Over/Under")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong",
                PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Away Team Individual Player Next Man Out Yes/No")));
        }

        [Fact]
        public void DerivePlaceholderSource_NoUsableSlot_ReturnsNull_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.DerivePlaceholderSource("Match Result")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.DerivePlaceholderSource("1st Innings Player A and Player B Both to Reach 50")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.DerivePlaceholderSource("Player 1 To Win A Set")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.DerivePlaceholderSource(null)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(PlayerMarketNameComposer.DerivePlaceholderSource("  ")));
        }

        #endregion
    }
}
