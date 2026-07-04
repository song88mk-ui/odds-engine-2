using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Adapter.Service.Repositories;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Adapter.Service.Tests
{
    // Covers the adapter's in-memory TranslationCache2 snapshot (2026-07 translation-flow
    // redesign): preload keeps only rows with at least one translated language, and
    // ON_TRANSLATION_CHANGE deltas are applied copy-on-write per language.
    public class TranslationCacheTests
    {
        private static IServiceScopeFactory ScopeFactory(string databaseName, params TranslationCache2[] rows)
        {
            var services = new ServiceCollection();
            services.AddDbContext<TranslationContext>(options => options.UseInMemoryDatabase(databaseName));
            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TranslationContext>();
                context.TranslationCache2.AddRange(rows);
                context.SaveChanges();
            }

            return provider.GetRequiredService<IServiceScopeFactory>();
        }

        private static TranslationCache2 Row(string stringId, string zh = "", string es = "", string fr = "")
        {
            return new TranslationCache2
            {
                StringId = stringId,
                SourceLanguage = "EN",
                SourceText = "Source",
                TargetTextZh = zh,
                TargetTextEs = es,
                TargetTextFr = fr
            };
        }

        private static TranslationCache Cache(string databaseName, params TranslationCache2[] rows)
        {
            return new TranslationCache(NullLogger<TranslationCache>.Instance, ScopeFactory(databaseName, rows));
        }

        [Fact]
        public void Preload_CachesOnlyRowsWithAtLeastOneTranslatedLanguage()
        {
            var cache = Cache(nameof(Preload_CachesOnlyRowsWithAtLeastOneTranslatedLanguage),
                Row("SpId:1", zh: "足球", fr: "Football"),
                Row("SpId:2")); // all targets empty — not translated yet

            cache.Preload();

            var translated = cache.GetTranslations("SpId:1");

            Assert.NotNull(translated);
            Assert.Equal(2, translated.Count);
            Assert.Equal("足球", translated.Single(t => t.LaCo == "ZH").Tx);
            Assert.Equal("Football", translated.Single(t => t.LaCo == "FR").Tx);

            Assert.Null(cache.GetTranslations("SpId:2"));
            Assert.Null(cache.GetTranslations("SpId:404"));
            Assert.Null(cache.GetTranslations(null));
        }

        [Fact]
        public void Apply_UpdatesOneLanguageWithoutTouchingOthers()
        {
            var cache = Cache(nameof(Apply_UpdatesOneLanguageWithoutTouchingOthers), Row("Te:H1", zh: "旧", es: "Viejo"));
            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:H1", LaCo = "zh", Tx = "新" } // lowercase — must normalize
            });

            var translated = cache.GetTranslations("Te:H1");

            Assert.NotNull(translated);
            Assert.Equal("新", translated.Single(t => t.LaCo == "ZH").Tx);
            Assert.Equal("Viejo", translated.Single(t => t.LaCo == "ES").Tx);
        }

        [Fact]
        public void Apply_AddsPreviouslyUnknownKey()
        {
            var cache = Cache(nameof(Apply_AddsPreviouslyUnknownKey));
            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "MaTe:16", LaCo = "FR", Tx = "Résultat du match" }
            });

            var translated = cache.GetTranslations("MaTe:16");

            Assert.NotNull(translated);
            Assert.Equal("Résultat du match", translated.Single().Tx);
        }

        [Fact]
        public void Apply_EmptyTextClearsLanguage_AndKeyIsDroppedWhenLastLanguageGoes()
        {
            var cache = Cache(nameof(Apply_EmptyTextClearsLanguage_AndKeyIsDroppedWhenLastLanguageGoes), Row("LeId:3", zh: "英超", es: "Premier"));
            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "LeId:3", LaCo = "ZH", Tx = "" }
            });

            var translated = cache.GetTranslations("LeId:3");
            Assert.NotNull(translated);
            Assert.Single(translated);
            Assert.Equal("ES", translated.Single().LaCo);

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "LeId:3", LaCo = "ES", Tx = "" }
            });

            Assert.Null(cache.GetTranslations("LeId:3")); // null, not an empty list
        }

        [Fact]
        public void Apply_IgnoresUnsupportedLanguagesAndMalformedItems()
        {
            var cache = Cache(nameof(Apply_IgnoresUnsupportedLanguagesAndMalformedItems), Row("SpId:1", zh: "足球"));
            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "SpId:1", LaCo = "JA", Tx = "サッカー" }, // not a TranslationCache2 column
                new TranslationChangeItem { StId = "", LaCo = "ZH", Tx = "x" },
                null!
            });

            var translated = cache.GetTranslations("SpId:1");

            Assert.NotNull(translated);
            Assert.Single(translated);
            Assert.Equal("足球", translated.Single().Tx);
        }
    }
}
