using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;

namespace OE.Shared.Test.Data.Translation
{
    // TranslationCache2 is the single translation store (2026-07 redesign); LegacyTranslationCache
    // is the external, read-only bridge table. These pin that both DbSets round-trip and that
    // LegacyTranslationCache's three-column composite key is actually enforced.
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

        [Fact]
        public void LegacyTranslationCache_RoundTripsThroughDbSet()
        {
            using var context = NewContext(nameof(LegacyTranslationCache_RoundTripsThroughDbSet));

            context.TranslationCache.Add(new LegacyTranslationCache
            {
                SourceHash = "HASH1",
                SourceLanguage = "EN",
                TargetLanguage = "ZH",
                SourceText = "Arsenal",
                TargetText = "阿森纳"
            });
            context.SaveChanges();

            var stored = context.TranslationCache
                .Single(t => t.SourceHash == "HASH1" && t.SourceLanguage == "EN" && t.TargetLanguage == "ZH");
            Assert.Equal("阿森纳", stored.TargetText);
        }

        // (SourceHash, SourceLanguage, TargetLanguage) is the composite key — the same source hash
        // legitimately has one row per target language.
        [Fact]
        public void LegacyTranslationCache_SameHashDifferentTargetLanguage_AreDistinctRows()
        {
            using var context = NewContext(nameof(LegacyTranslationCache_SameHashDifferentTargetLanguage_AreDistinctRows));

            context.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ZH" });
            context.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ES" });
            context.SaveChanges();

            Assert.Equal(2, context.TranslationCache.Count(t => t.SourceHash == "HASH1"));
        }

        [Fact]
        public void LegacyTranslationCache_DuplicateCompositeKey_ThrowsOnSecondContextSave()
        {
            var provider = NewProvider(nameof(LegacyTranslationCache_DuplicateCompositeKey_ThrowsOnSecondContextSave));

            using (var firstScope = provider.CreateScope())
            {
                var first = firstScope.ServiceProvider.GetRequiredService<TranslationContext>();
                first.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ZH" });
                first.SaveChanges();
            }

            using var secondScope = provider.CreateScope();
            var second = secondScope.ServiceProvider.GetRequiredService<TranslationContext>();
            second.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ZH" });

            // EF Core InMemory 3.1.x surfaces a duplicate primary key as a raw ArgumentException
            // from its backing dictionary rather than wrapping it in DbUpdateException.
            Assert.Throws<ArgumentException>(() => second.SaveChanges());
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void TranslationCache2_RoundTripsThroughDbSet_MutationCheck()
        {
            using var context = NewContext(nameof(TranslationCache2_RoundTripsThroughDbSet_MutationCheck));

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
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("Chelsea", stored.TargetTextZh));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(stored.HasBetPlacement));
        }

        [Fact]
        public void LegacyTranslationCache_RoundTripsThroughDbSet_MutationCheck()
        {
            using var context = NewContext(nameof(LegacyTranslationCache_RoundTripsThroughDbSet_MutationCheck));

            context.TranslationCache.Add(new LegacyTranslationCache
            {
                SourceHash = "HASH1",
                SourceLanguage = "EN",
                TargetLanguage = "ZH",
                SourceText = "Arsenal",
                TargetText = "阿森纳"
            });
            context.SaveChanges();

            var stored = context.TranslationCache
                .Single(t => t.SourceHash == "HASH1" && t.SourceLanguage == "EN" && t.TargetLanguage == "ZH");
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("Chelsea", stored.TargetText));
        }

        // (SourceHash, SourceLanguage, TargetLanguage) is the composite key — the same source hash
        // legitimately has one row per target language.
        [Fact]
        public void LegacyTranslationCache_SameHashDifferentTargetLanguage_AreDistinctRows_MutationCheck()
        {
            using var context = NewContext(nameof(LegacyTranslationCache_SameHashDifferentTargetLanguage_AreDistinctRows_MutationCheck));

            context.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ZH" });
            context.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ES" });
            context.SaveChanges();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, context.TranslationCache.Count(t => t.SourceHash == "HASH1")));
        }

        [Fact]
        public void LegacyTranslationCache_DuplicateCompositeKey_ThrowsOnSecondContextSave_MutationCheck()
        {
            var provider = NewProvider(nameof(LegacyTranslationCache_DuplicateCompositeKey_ThrowsOnSecondContextSave_MutationCheck));

            using (var firstScope = provider.CreateScope())
            {
                var first = firstScope.ServiceProvider.GetRequiredService<TranslationContext>();
                first.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ZH" });
                first.SaveChanges();
            }

            using var secondScope = provider.CreateScope();
            var second = secondScope.ServiceProvider.GetRequiredService<TranslationContext>();
            second.TranslationCache.Add(new LegacyTranslationCache { SourceHash = "HASH1", SourceLanguage = "EN", TargetLanguage = "ZH" });

            // The real test proves SaveChanges throws ArgumentException on a duplicate composite
            // key. This proves the proof itself discriminates: asserting that NO exception was
            // thrown must fail here, since one really is.
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            {
                var exception = Record.Exception(() => second.SaveChanges());
                Assert.Null(exception);
            });
        }

        #endregion
    }
}
