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

        // 2026-07-09 fix: Co:{CompId} is shared by every selection tied to one competitor, but a
        // competitor-fused market (e.g. a tennis set-score prop) appends a volatile score onto the
        // same stable name another selection already cached here — the incoming text is a rename
        // by exact-match standards, but it's really "the cached name plus a tail," so the cached
        // translation should be reused with the tail appended, not discarded.
        [Fact]
        public void GetTranslations_RenameIsKnownNamePlusScoreTail_SplicesTranslatedNamePlusTail()
        {
            var cache = Cache(nameof(GetTranslations_RenameIsKnownNamePlusScoreTail_SplicesTranslatedNamePlusTail),
                Row("Co:1372197", zh: "罗杰斯, 安娜", sourceText: "Rogers, Anna"));

            cache.Preload();

            var translated = cache.GetTranslations("Co:1372197", "Rogers, Anna 7-5");

            Assert.NotNull(translated);
            Assert.Equal("罗杰斯, 安娜 7-5", translated!.Single().Tx);
        }

        // Winning Margin family (AR-14): the tail contains letters ("by 2"), which the generic
        // no-letters guard alone would reject — the narrow " by N"/" by N+" allowance covers it
        // without opening the door to arbitrary letter-bearing tails.
        [Theory]
        [InlineData("Fluminense FC by 2", "弗鲁米嫩塞 by 2")]
        [InlineData("Fluminense FC by 2+", "弗鲁米嫩塞 by 2+")]
        [InlineData("Fluminense FC BY 3", "弗鲁米嫩塞 BY 3")]
        public void GetTranslations_RenameIsKnownNamePlusWinningMarginTail_SplicesTranslatedNamePlusTail(string currentText, string expected)
        {
            var cache = Cache(nameof(GetTranslations_RenameIsKnownNamePlusWinningMarginTail_SplicesTranslatedNamePlusTail) + currentText,
                Row("Co:96870", zh: "弗鲁米嫩塞", sourceText: "Fluminense FC"));

            cache.Preload();

            var translated = cache.GetTranslations("Co:96870", currentText);

            Assert.NotNull(translated);
            Assert.Equal(expected, translated!.Single().Tx);
        }

        // 2026-07-14 prod incident: TranslationCache2 held "Odd or Even Total" (lowercase "or",
        // inherited from the staging export) for MaTe:7354 while the live feed sends
        // "Odd Or Even Total" — the case-sensitive rename guard rejected the row and the
        // case-sensitive content fallback missed it, so the market rendered in English forever
        // despite a valid ZH translation sitting in the table. A case-only variant is the same
        // text, not a rename.
        [Fact]
        public void GetTranslations_CaseOnlySourceTextMismatch_IsNotARename_ServesCachedTranslation()
        {
            var cache = Cache(nameof(GetTranslations_CaseOnlySourceTextMismatch_IsNotARename_ServesCachedTranslation),
                Row("MaTe:7354", zh: "总分奇数或偶数", sourceText: "Odd or Even Total"));

            cache.Preload();

            var translated = cache.GetTranslations("MaTe:7354", "Odd Or Even Total");

            Assert.NotNull(translated);
            Assert.Equal("总分奇数或偶数", translated.Single().Tx);
        }

        // The content-addressed fallback must tolerate case drift too: identical English text
        // under a different STRING_ID is reusable regardless of casing.
        [Fact]
        public void GetTranslations_ContentFallbackMatchesCaseInsensitively()
        {
            var cache = Cache(nameof(GetTranslations_ContentFallbackMatchesCaseInsensitively),
                Row("MaTe:7354", zh: "总分奇数或偶数", sourceText: "Odd or Even Total"));

            cache.Preload();

            var translated = cache.GetTranslations("MaTe:7934", "Odd Or Even Total");

            Assert.NotNull(translated);
            Assert.Equal("总分奇数或偶数", translated.Single().Tx);
        }

        // The name-plus-tail splice must also tolerate a case-only difference in the name part —
        // same principle as the rename guard above.
        [Fact]
        public void GetTranslations_TailSpliceToleratesCaseOnlyPrefixDifference()
        {
            var cache = Cache(nameof(GetTranslations_TailSpliceToleratesCaseOnlyPrefixDifference),
                Row("Co:96870", zh: "弗鲁米嫩塞", sourceText: "Fluminense Fc"));

            cache.Preload();

            var translated = cache.GetTranslations("Co:96870", "Fluminense FC by 2");

            Assert.NotNull(translated);
            Assert.Equal("弗鲁米嫩塞 by 2", translated.Single().Tx);
        }

        // A tail with letters that ISN'T the Winning Margin shape must still be treated as a
        // genuine rename, not spliced — otherwise the guard is meaningless.
        [Fact]
        public void GetTranslations_RenameWithNonMarginLetterTail_StillFallsBackInsteadOfSplicing()
        {
            var cache = Cache(nameof(GetTranslations_RenameWithNonMarginLetterTail_StillFallsBackInsteadOfSplicing),
                Row("Co:100", zh: "球员X", sourceText: "Player X"));

            cache.Preload();

            Assert.Null(cache.GetTranslations("Co:100", "Player X United"));
        }

        // 2026-07-09 fix: market/coverage-derived OnMarketChangeMessage/OnFixtureChangeMessage
        // payloads never carry team/league names (see
        // BG.Receiver.Service.OddsFeedRepository.ProcessMarkets/ProcessCoverage — only the
        // fixture-change payload from ProcessFixture does). An empty currentText on those messages
        // is "no information to compare," not a rename, so the cached row must still be trusted —
        // otherwise a team's HoTr/AwTr can never be attached except on the rare genuine
        // fixture-change message.
        [Fact]
        public void GetTranslations_EmptyCurrentText_TrustsCachedRowInsteadOfTreatingAsRename()
        {
            var cache = Cache(nameof(GetTranslations_EmptyCurrentText_TrustsCachedRowInsteadOfTreatingAsRename),
                Row("Te:108358", zh: "翁特哈兴", sourceText: "SpVgg Unterhaching"));

            cache.Preload();

            Assert.Equal("翁特哈兴", cache.GetTranslations("Te:108358", null)!.Single().Tx);
            Assert.Equal("翁特哈兴", cache.GetTranslations("Te:108358", string.Empty)!.Single().Tx);
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
