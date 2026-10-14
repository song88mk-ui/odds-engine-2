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

        // Same "malformed item" guard as the empty-StId case above, but for a missing/empty LaCo.
        [Fact]
        public void Apply_MissingOrEmptyLaCo_SkipsItemWithoutThrowing()
        {
            var cache = Cache(nameof(Apply_MissingOrEmptyLaCo_SkipsItemWithoutThrowing), Row("SpId:1", zh: "足球"));
            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "SpId:1", LaCo = "", Tx = "x" },
                new TranslationChangeItem { StId = "SpId:1", LaCo = null!, Tx = "y" }
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

        // 2026-07-16 prod: market-template wording oscillates in the feed ("Over/Under" <->
        // "Total Goals Over / Under"), leaving whole market groups in English while a perfectly
        // good translation sat preloaded. MaTe: rows serve the LAST KNOWN translation on a
        // wording mismatch — the seeder has already re-queued the new wording, and the
        // retranslation replaces this fallback via ON_TRANSLATION_CHANGE.
        [Fact]
        public void GetTranslations_MaTeWordingMismatch_ServesLastKnownTranslationAsFallback()
        {
            var cache = Cache(nameof(GetTranslations_MaTeWordingMismatch_ServesLastKnownTranslationAsFallback),
                Row("MaTe:259", zh: "总进球数 大/小于", sourceText: "Total Goals Over / Under"));

            cache.Preload();

            var translated = cache.GetTranslations("MaTe:259", "Over/Under");

            Assert.NotNull(translated);
            Assert.Equal("总进球数 大/小于", translated.Single().Tx);
        }

        // The stale-serve fallback is deliberately MaTe:-only — a team-name wording change (e.g.
        // the vendor's "Only {Team}" variant fixtures) must still refuse rather than mislabel.
        [Fact]
        public void GetTranslations_TeWordingMismatch_IsStillRefused()
        {
            var cache = Cache(nameof(GetTranslations_TeWordingMismatch_IsStillRefused),
                Row("Te:10033", zh: "国际米兰", sourceText: "Internazionale"));

            cache.Preload();

            Assert.Null(cache.GetTranslations("Te:10033", "Only Internazionale"));
        }

        // 2026-07-16: a wording-variant row (MarketTemplateVariantId) announced after startup
        // must be content-addressable immediately — the announce carries SrcTx and Apply indexes
        // it. Without this, the stale-serve MaTe fallback (not the fresh, wording-exact variant
        // translation) kept answering until the next restart's preload.
        [Fact]
        public void Apply_WithSourceText_MakesTheNewWordingContentAddressable()
        {
            var cache = Cache(nameof(Apply_WithSourceText_MakesTheNewWordingContentAddressable),
                Row("MaTe:259", zh: "总进球数 大/小于", sourceText: "Total Goals Over / Under"));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "MaTe:259~a1b2c3d4", LaCo = "ZH", Tx = "大/小", SrcTx = "Over/Under" }
            });

            var translated = cache.GetTranslations("MaTe:259", "Over/Under");

            Assert.NotNull(translated);
            Assert.Equal("大/小", translated.Single().Tx); // the variant's own translation, not the stale fallback
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

        // GM-875: a SOURCE_TEXT rename (a Backoffice edit on a language tab sends the new SrcTx for
        // an existing STRING_ID) used to leave the content-index entry under the OLD text holding a
        // frozen pre-rename copy of the row's translations. Any entity still using the old wording
        // was served it, and later edits to the row never reached it.
        [Fact]
        public void Apply_SourceTextRename_StopsServingTheFrozenTranslationUnderTheOldText()
        {
            var cache = Cache(nameof(Apply_SourceTextRename_StopsServingTheFrozenTranslationUnderTheOldText),
                Row("Te:1", zh: "旧", sourceText: "Old Name"));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "新", SrcTx = "New Name" }
            });

            // A different, unseen key still using the old wording must not get the renamed row's
            // pre-rename translation.
            Assert.Null(cache.GetTranslations("Te:99", "Old Name"));
        }

        [Fact]
        public void Apply_SourceTextRename_MakesTheNewTextContentAddressableWithEveryLanguage()
        {
            var cache = Cache(nameof(Apply_SourceTextRename_MakesTheNewTextContentAddressableWithEveryLanguage),
                Row("Te:1", zh: "旧", es: "Viejo", sourceText: "Old Name"));

            cache.Preload();

            // Backoffice publishes one item per edited language, each carrying the new SrcTx.
            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "新", SrcTx = "New Name" },
                new TranslationChangeItem { StId = "Te:1", LaCo = "ES", Tx = "Nuevo", SrcTx = "New Name" }
            });

            var translated = cache.GetTranslations("Te:99", "New Name");

            Assert.NotNull(translated);
            Assert.Equal("新", translated!.Single(t => t.LaCo == "ZH").Tx);
            Assert.Equal("Nuevo", translated.Single(t => t.LaCo == "ES").Tx);
            Assert.Null(cache.GetTranslations("Te:99", "Old Name"));
        }

        // The index entry is only removed while it is still this row's own list: another row with
        // the same old text may have written it since, and its entry must survive the rename.
        [Fact]
        public void Apply_SourceTextRename_KeepsAnotherRowsContentIndexEntryForTheSameOldText()
        {
            var cache = Cache(nameof(Apply_SourceTextRename_KeepsAnotherRowsContentIndexEntryForTheSameOldText),
                Row("Te:1", zh: "旧A", sourceText: "Old Name"));

            cache.Preload();

            // Te:2 shares the old text and, announced last, owns the content-index entry for it.
            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:2", LaCo = "ZH", Tx = "旧B", SrcTx = "Old Name" }
            });

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "新A", SrcTx = "New Name" }
            });

            Assert.Equal("旧B", cache.GetTranslations("Te:99", "Old Name")!.Single().Tx);
            Assert.Equal("新A", cache.GetTranslations("Te:99", "New Name")!.Single().Tx);
        }

        // A case-only change is the same text, not a rename (see the 2026-07-14 incident above).
        [Fact]
        public void Apply_CaseOnlySourceTextChange_IsNotARename_KeepsTheContentIndexEntry()
        {
            var cache = Cache(nameof(Apply_CaseOnlySourceTextChange_IsNotARename_KeepsTheContentIndexEntry),
                Row("Te:1", zh: "旧", sourceText: "Old Name"));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "新", SrcTx = "OLD NAME" }
            });

            Assert.Equal("新", cache.GetTranslations("Te:99", "Old Name")!.Single().Tx);
            Assert.Equal("新", cache.GetTranslations("Te:99", "OLD NAME")!.Single().Tx);
        }

        // An earlier delta replaces the row's list and refreshes the index entry with the new
        // instance, so the rename must compare against the row's list as it was just before this
        // change, not the one loaded at preload.
        [Fact]
        public void Apply_SourceTextRename_AfterAnEarlierDelta_StillRemovesTheOldTextEntry()
        {
            var cache = Cache(nameof(Apply_SourceTextRename_AfterAnEarlierDelta_StillRemovesTheOldTextEntry),
                Row("Te:1", zh: "旧", sourceText: "Old Name"));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "旧2" }
            });

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "新", SrcTx = "New Name" }
            });

            Assert.Null(cache.GetTranslations("Te:99", "Old Name"));
        }

        // GM-875 follow-up: clearing a row's last language used to remove the content-index entry
        // for its SOURCE_TEXT unconditionally, dropping another row's entry for the same text.
        [Fact]
        public void Apply_ClearingLastLanguage_KeepsAnotherRowsContentIndexEntryForTheSameText()
        {
            var cache = Cache(nameof(Apply_ClearingLastLanguage_KeepsAnotherRowsContentIndexEntryForTheSameText),
                Row("Te:1", zh: "甲", sourceText: "Shared Name"));

            cache.Preload();

            // Te:2 shares the text and, announced last, owns the content-index entry for it.
            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:2", LaCo = "ZH", Tx = "乙", SrcTx = "Shared Name" }
            });

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "" }
            });

            Assert.Equal("乙", cache.GetTranslations("Te:99", "Shared Name")!.Single().Tx);

            // The owner clearing its last language still removes the entry.
            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:2", LaCo = "ZH", Tx = "" }
            });

            Assert.Null(cache.GetTranslations("Te:99", "Shared Name"));
        }

        // A rename that also clears the last language: the row never wrote an entry under the new
        // text, so another row's entry for it must survive; the old-text entry still goes.
        [Fact]
        public void Apply_SourceTextRenameClearingLastLanguage_KeepsAnotherRowsEntryForTheNewText()
        {
            var cache = Cache(nameof(Apply_SourceTextRenameClearingLastLanguage_KeepsAnotherRowsEntryForTheNewText),
                Row("Te:1", zh: "旧", sourceText: "Old Name"));

            cache.Preload();

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:2", LaCo = "ZH", Tx = "新", SrcTx = "New Name" }
            });

            cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:1", LaCo = "ZH", Tx = "", SrcTx = "New Name" }
            });

            Assert.Equal("新", cache.GetTranslations("Te:99", "New Name")!.Single().Tx);
            Assert.Null(cache.GetTranslations("Te:99", "Old Name"));
        }
    }
}
