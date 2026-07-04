using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using OE.Sportsbook.Service.Telemetry;

namespace OE.Sportsbook.Service.Tests
{
    // Covers the websocket-path translation change gate (2026-07 translation-flow redesign):
    // OE.Sportsbook.Service strips unchanged *Tr lists from ON_ODDS_CHANGE (BulkWriteResult can't
    // detect this — writes are unacknowledged and odds ride the same documents), and keeps its
    // gate state coherent when ON_TRANSLATION_CHANGE deltas arrive. Mongo writes are never gated.
    public class TranslationGateTests
    {
        private static SportsbookRepository CreateRepository()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Never connected to by these tests — MongoClient/GetCollection are lazy.
                    ["Custom:MongoDB-Sportsbook"] = "mongodb://localhost:27017"
                })
                .Build();

            return new SportsbookRepository(
                configuration,
                new FakeMemCache(),
                new Helper(),
                new Instrumentation(),
                NullLogger<SportsbookRepository>.Instance);
        }

        private static List<TranslationMessage> Translations(params (string LaCo, string Tx)[] items)
        {
            return items.Select(i => new TranslationMessage { LaCo = i.LaCo, Tx = i.Tx }).ToList();
        }

        [Fact]
        public void Gate_FirstSighting_PublishesAndRecords()
        {
            var repository = CreateRepository();
            var incoming = Translations(("ZH", "足球"));

            var result = repository.GateTranslations("SpId:1", incoming);

            Assert.Same(incoming, result);
        }

        [Fact]
        public void Gate_UnchangedValues_ReturnsNull_EvenWhenOrderDiffers()
        {
            var repository = CreateRepository();

            repository.GateTranslations("SpId:1", Translations(("ZH", "足球"), ("FR", "Football")));

            var second = repository.GateTranslations("SpId:1", Translations(("FR", "Football"), ("ZH", "足球")));

            Assert.Null(second);
        }

        [Fact]
        public void Gate_ChangedValue_PublishesAgain()
        {
            var repository = CreateRepository();

            repository.GateTranslations("SpId:1", Translations(("ZH", "足球")));

            var changed = repository.GateTranslations("SpId:1", Translations(("ZH", "足球赛")));

            Assert.NotNull(changed);
            Assert.Equal("足球赛", changed!.Single().Tx);
        }

        [Fact]
        public void Gate_AddedLanguage_PublishesAgain()
        {
            var repository = CreateRepository();

            repository.GateTranslations("SpId:1", Translations(("ZH", "足球")));

            var withNewLanguage = repository.GateTranslations("SpId:1", Translations(("ZH", "足球"), ("ES", "Fútbol")));

            Assert.NotNull(withNewLanguage);
        }

        [Fact]
        public void Gate_NullOrEmpty_NeverPublishes()
        {
            var repository = CreateRepository();

            Assert.Null(repository.GateTranslations("SpId:1", null!));
            Assert.Null(repository.GateTranslations("SpId:1", new List<TranslationMessage>()));
        }

        [Fact]
        public void Gate_KeysAreIndependent()
        {
            var repository = CreateRepository();
            var values = Translations(("ZH", "足球"));

            repository.GateTranslations("SpId:1", values);

            Assert.NotNull(repository.GateTranslations("SpId:2", values));
        }

        [Fact]
        public void TranslationsEqual_ComparesByLanguageAndText()
        {
            Assert.True(SportsbookRepository.TranslationsEqual(
                Translations(("ZH", "足球"), ("FR", "Football")),
                Translations(("FR", "Football"), ("ZH", "足球"))));

            Assert.False(SportsbookRepository.TranslationsEqual(
                Translations(("ZH", "足球")),
                Translations(("ZH", "足球"), ("FR", "Football"))));

            Assert.False(SportsbookRepository.TranslationsEqual(
                Translations(("ZH", "足球")),
                Translations(("ZH", "足球赛"))));
        }

    }
}
