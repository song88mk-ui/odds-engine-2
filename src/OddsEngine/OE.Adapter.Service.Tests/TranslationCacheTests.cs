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

        // Each row gets a distinct SourceText derived from its StringId by default so tests that
        // aren't specifically exercising the content-fallback (GetTranslations' reuse-by-text
        // path) don't accidentally trip it via a shared placeholder string.
        private static TranslationCache2 Row(string stringId, string zh = "", string es = "", string fr = "", string sourceText = null)
        {
            return new TranslationCache2
            {
                StringId = stringId,
                SourceLanguage = "EN",
                SourceText = sourceText ?? $"Source-{stringId}",
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

            var translated = cache.GetTranslations("SpId:1", "Source-SpId:1");

            Assert.NotNull(translated);
            Assert.Equal(2, translated.Count);
            Assert.Equal("足球", translated.Single(t => t.LaCo == "ZH").Tx);
            Assert.Equal("Football", translated.Single(t => t.LaCo == "FR").Tx);

            Assert.Null(cache.GetTranslations("SpId:2", "Source-SpId:2"));
            Assert.Null(cache.GetTranslations("SpId:404", "Source-SpId:404"));
            Assert.Null(cache.GetTranslations(null, null));
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

            var translated = cache.GetTranslations("Te:H1", "Source-Te:H1");

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

            var translated = cache.GetTranslations("MaTe:16", "anything"); // no tracked source text — trusted as-is

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

            var translated = cache.GetTranslations("LeId:3", "Source-LeId:3");
            Assert.NotNull(translated);
            Assert.Single(translated);
            Assert.Equal("ES", translated.Single().LaCo);

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "LeId:3", LaCo = "ES", Tx = "" }
            });

            Assert.Null(cache.GetTranslations("LeId:3", "Source-LeId:3")); // null, not an empty list
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

            var translated = cache.GetTranslations("SpId:1", "Source-SpId:1");

            Assert.NotNull(translated);
            Assert.Single(translated);
            Assert.Equal("足球", translated.Single().Tx);
        }

        // Content-addressed fallback (2026-07-09): a StringId miss or a detected rename (cached
        // SOURCE_TEXT no longer matches what's arriving) reuses an existing translation for
        // identical English text under a different StringId instead of returning null.
        [Fact]
        public void GetTranslations_StringIdMiss_FallsBackToContentMatchByText()
        {
            var cache = Cache(nameof(GetTranslations_StringIdMiss_FallsBackToContentMatchByText),
                Row("Co:100", zh: "球员Y", sourceText: "Player Y"));

            cache.Preload();

            // "Co:200" was never cached, but its incoming English text matches Co:100's.
            var translated = cache.GetTranslations("Co:200", "Player Y");

            Assert.NotNull(translated);
            Assert.Equal("球员Y", translated!.Single().Tx);
        }

        [Fact]
        public void GetTranslations_RenameDetected_FallsBackToContentMatchInsteadOfStaleCache()
        {
            var cache = Cache(nameof(GetTranslations_RenameDetected_FallsBackToContentMatchInsteadOfStaleCache),
                Row("Co:100", zh: "球员X", sourceText: "Player X"),
                Row("Co:200", zh: "球员Y", sourceText: "Player Y"));

            cache.Preload();

            // Co:200's cached SOURCE_TEXT ("Player Y") no longer matches the incoming text
            // ("Player X") — a rename — so it must NOT return its own stale "球员Y" cache entry,
            // and instead reuse Co:100's translation for the now-matching text.
            var translated = cache.GetTranslations("Co:200", "Player X");

            Assert.NotNull(translated);
            Assert.Equal("球员X", translated!.Single().Tx);
        }

        [Fact]
        public void GetTranslations_RenameWithNoContentMatch_ReturnsNull()
        {
            var cache = Cache(nameof(GetTranslations_RenameWithNoContentMatch_ReturnsNull),
                Row("Co:100", zh: "球员X", sourceText: "Player X"));

            cache.Preload();

            Assert.Null(cache.GetTranslations("Co:100", "Player X Renamed"));
        }

        [Fact]
        public void Apply_KeepsContentIndexInSyncForTrackedRows()
        {
            var cache = Cache(nameof(Apply_KeepsContentIndexInSyncForTrackedRows),
                Row("Co:100", zh: "旧", sourceText: "Player X"));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Co:100", LaCo = "ZH", Tx = "新" }
            });

            // A different, unseen key with the same English text should reuse the UPDATED
            // translation, not the one captured at preload time.
            var translated = cache.GetTranslations("Co:200", "Player X");

            Assert.NotNull(translated);
            Assert.Equal("新", translated!.Single().Tx);
        }
    }
}
