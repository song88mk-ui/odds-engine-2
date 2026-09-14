using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;

namespace OE.Shared.Test.Data.Translation
{
    // TranslationCache2 is the single translation store (2026-07 redesign) and, since GM-784
    // retired the legacy TranslationCache bridge, the context's only DbSet. This pins that it
    // round-trips through EF with its column mapping intact.
    public class TranslationContextTests
    {
        private static IServiceProvider NewProvider(string databaseName)
        {
            var services = new ServiceCollection();
            services.AddDbContext<TranslationContext>(options => options.UseInMemoryDatabase(databaseName));
            return services.BuildServiceProvider();
        }

        private static TranslationContext NewContext(string databaseName) =>
            NewProvider(databaseName).CreateScope().ServiceProvider.GetRequiredService<TranslationContext>();

        [Fact]
        public void TranslationCache2_RoundTripsThroughDbSet()
        {
            using var context = NewContext(nameof(TranslationCache2_RoundTripsThroughDbSet));

            context.TranslationCache2.Add(new TranslationCache2
            {
                StringId = "Te:H1",
                SourceLanguage = "EN",
                SourceText = "Arsenal",
                TargetTextZh = "阿森纳",
                TargetTextEs = "",
                TargetTextFr = "",
                HasBetPlacement = true
            });
            context.SaveChanges();

            var stored = context.TranslationCache2.Single(t => t.StringId == "Te:H1");
            Assert.Equal("阿森纳", stored.TargetTextZh);
            Assert.True(stored.HasBetPlacement);
        }
    }
}
