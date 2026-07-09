using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Translation.Service.Repositories;

namespace OE.Translation.Service.Tests
{
    // This service's copy of the TranslationCache (mirrors OE.Adapter.Service's) backs the live
    // Mongo *NaTr writes for the raw BG.Receiver stream — same preload/apply semantics.
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
                Row("SpId:1", zh: "足球"),
                Row("SpId:2"));

            cache.Preload();

            Assert.Equal("足球", cache.GetTranslations("SpId:1", "Source-SpId:1")!.Single().Tx);
            Assert.Null(cache.GetTranslations("SpId:2", "Source-SpId:2"));
        }

        [Fact]
        public void Apply_UpdatesAddsAndRemovesLanguages()
        {
            var cache = Cache(nameof(Apply_UpdatesAddsAndRemovesLanguages), Row("Te:H1", zh: "旧", es: "Viejo"));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:H1", LaCo = "zh", Tx = "新" },   // update (lowercase normalized)
                new TranslationChangeItem { StId = "Te:H1", LaCo = "ES", Tx = "" },     // removal
                new TranslationChangeItem { StId = "MaTe:16", LaCo = "FR", Tx = "Résultat" }, // new key
                new TranslationChangeItem { StId = "Te:H1", LaCo = "JA", Tx = "x" }     // unsupported — ignored
            });

            var team = cache.GetTranslations("Te:H1", "Source-Te:H1");
            Assert.NotNull(team);
            Assert.Equal("新", team!.Single().Tx);

            Assert.Equal("Résultat", cache.GetTranslations("MaTe:16", "anything")!.Single().Tx); // no tracked source text — trusted as-is
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
    }
}
