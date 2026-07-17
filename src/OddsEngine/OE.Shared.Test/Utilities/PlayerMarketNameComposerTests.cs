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
    }
}
