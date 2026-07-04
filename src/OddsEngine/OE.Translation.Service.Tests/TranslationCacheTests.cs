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

        [Fact]
        public void Preload_CachesOnlyRowsWithAtLeastOneTranslatedLanguage()
        {
            var cache = new TranslationCache(
                NullLogger<TranslationCache>.Instance,
                ScopeFactory(nameof(Preload_CachesOnlyRowsWithAtLeastOneTranslatedLanguage),
                    Row("SpId:1", zh: "足球"),
                    Row("SpId:2")));

            cache.Preload();

            Assert.Equal("足球", cache.GetTranslations("SpId:1")!.Single().Tx);
            Assert.Null(cache.GetTranslations("SpId:2"));
        }

        [Fact]
        public void Apply_UpdatesAddsAndRemovesLanguages()
        {
            var cache = new TranslationCache(
                NullLogger<TranslationCache>.Instance,
                ScopeFactory(nameof(Apply_UpdatesAddsAndRemovesLanguages), Row("Te:H1", zh: "旧", es: "Viejo")));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:H1", LaCo = "zh", Tx = "新" },   // update (lowercase normalized)
                new TranslationChangeItem { StId = "Te:H1", LaCo = "ES", Tx = "" },     // removal
                new TranslationChangeItem { StId = "MaTe:16", LaCo = "FR", Tx = "Résultat" }, // new key
                new TranslationChangeItem { StId = "Te:H1", LaCo = "JA", Tx = "x" }     // unsupported — ignored
            });

            var team = cache.GetTranslations("Te:H1");
            Assert.NotNull(team);
            Assert.Equal("新", team!.Single().Tx);

            Assert.Equal("Résultat", cache.GetTranslations("MaTe:16")!.Single().Tx);
        }
    }
}
