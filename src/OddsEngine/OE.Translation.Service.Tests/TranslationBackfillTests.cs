using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Translation.Service.Repositories;

namespace OE.Translation.Service.Tests
{
    // Temporary Custom:Translation:CacheBackfill bridge (ported from the removed
    // BG.Translation.Service, adapted to the flat TranslationCache2): after seeding, targets are
    // copied from the external SOURCE_HASH-keyed TranslationCache into EMPTY TARGET_TEXT_* columns
    // only. The hash must stay byte-identical to SQL Server's
    // CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', SOURCE_TEXT), 2) or the join finds nothing.
    public class TranslationBackfillTests
    {
        #region Setup

        // SELECT CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', N'Arsenal'), 2)
        private const string ArsenalHash = "7A734C018846706B74B508C31897FD6B46856BE7B88E5BC656611689FB9398F4";

        private static (TranslationRepository Repository, ServiceProvider Provider) CreateRepository(
            string databaseName,
            bool cacheBackfill = true)
        {
            var services = new ServiceCollection();
            services.AddDbContext<TranslationContext>(options => options.UseInMemoryDatabase(databaseName));
            var provider = services.BuildServiceProvider();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Custom:Translation:CacheSeed"] = "true",
                    ["Custom:Translation:CacheBackfill"] = cacheBackfill.ToString()
                })
                .Build();

            var repository = new TranslationRepository(
                NullLogger<TranslationRepository>.Instance,
                new FakeMemCache(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                configuration);

            return (repository, provider);
        }

        private static TranslationContext GetContext(ServiceProvider provider)
        {
            return provider.CreateScope().ServiceProvider.GetRequiredService<TranslationContext>();
        }

        private static void AddLegacyTranslation(
            ServiceProvider provider,
            string sourceText,
            string targetLanguage,
            string? targetText)
        {
            using var context = GetContext(provider);

            context.TranslationCache.Add(new LegacyTranslationCache
            {
                SourceHash = TranslationRepository.SourceHash(sourceText),
                SourceLanguage = "EN",
                TargetLanguage = targetLanguage,
                SourceText = sourceText,
                TargetText = targetText
            });

            context.SaveChanges();
        }

        private static OnFixtureChangeMessage FixtureMessage(string homeName = "Arsenal")
        {
            return new OnFixtureChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                SpId = 1,
                SpNa = "Soccer",
                SegId = 2,
                SegNa = "England",
                LeId = 3,
                LeNa = "Premier League",
                FiNa = "Arsenal vs. Chelsea",
                HoId = "H1",
                HoNa = homeName,
                AwId = "A1",
                AwNa = "Chelsea"
            };
        }

        private static OnMarketChangeMessage MarketMessage()
        {
            return new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M1",
                        MaTeId = "16",
                        MaTeNa = "Match Result",
                        MaNa = "Match Result"
                    }
                }
            };
        }

        #endregion

        [Fact]
        public void SourceHash_MatchesSqlServerHashbytesOfNvarcharText()
        {
            // Fixed vector: any drift in encoding (UTF-16LE) or casing breaks the join against
            // the legacy persisted computed column.
            Assert.Equal(ArsenalHash, TranslationRepository.SourceHash("Arsenal"));
        }

        [Fact]
        public async Task Backfill_FillsEmptyTargetsByHash_ForAllSchemaLanguages()
        {
            var (repository, provider) = CreateRepository(nameof(Backfill_FillsEmptyTargetsByHash_ForAllSchemaLanguages));

            AddLegacyTranslation(provider, "Arsenal", "ZH", "阿森纳");
            AddLegacyTranslation(provider, "Arsenal", "ES", "Arsenal ES");
            AddLegacyTranslation(provider, "Arsenal", "FR", "Arsenal FR");

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var context = GetContext(provider);
            var row = context.TranslationCache2.Single(r => r.StringId == "Te:H1");

            Assert.Equal("阿森纳", row.TargetTextZh);
            Assert.Equal("Arsenal ES", row.TargetTextEs);
            Assert.Equal("Arsenal FR", row.TargetTextFr);
            Assert.True(row.Processed); // legacy cache satisfied it — no DeepL pass needed

            // Keys with no legacy translation stay on the external pipeline's worklist.
            var untouched = context.TranslationCache2.Single(r => r.StringId == "Te:A1");
            Assert.Equal(string.Empty, untouched.TargetTextZh);
            Assert.Equal(string.Empty, untouched.TargetTextEs);
            Assert.Equal(string.Empty, untouched.TargetTextFr);
            Assert.False(untouched.Processed);
        }

        [Fact]
        public async Task Backfill_WorksOnTheMarketTemplatePath()
        {
            var (repository, provider) = CreateRepository(nameof(Backfill_WorksOnTheMarketTemplatePath));

            AddLegacyTranslation(provider, "Match Result", "ZH", "赛果");

            await repository.OnProcessMarketSetUpdate(MarketMessage());

            using var context = GetContext(provider);
            var row = context.TranslationCache2.Single(r => r.StringId == "MaTe:16");
            Assert.Equal("赛果", row.TargetTextZh);
            Assert.True(row.Processed);
        }

        [Fact]
        public async Task Backfill_NeverOverwritesAPipelineWrittenTarget()
        {
            var (repository, provider) = CreateRepository(nameof(Backfill_NeverOverwritesAPipelineWrittenTarget));

            using (var context = GetContext(provider))
            {
                // The external pipeline already translated ZH under the new key space.
                context.TranslationCache2.Add(new TranslationCache2
                {
                    StringId = "Te:H1",
                    SourceLanguage = "EN",
                    SourceText = "Arsenal",
                    TargetTextZh = "现有翻译",
                    TargetTextEs = string.Empty,
                    TargetTextFr = string.Empty,
                    CreatedAt = DateTime.UtcNow
                });
                context.SaveChanges();
            }

            AddLegacyTranslation(provider, "Arsenal", "ZH", "旧翻译");
            AddLegacyTranslation(provider, "Arsenal", "ES", "Arsenal ES");

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var verify = GetContext(provider);
            var row = verify.TranslationCache2.Single(r => r.StringId == "Te:H1");

            Assert.Equal("现有翻译", row.TargetTextZh); // pipeline value kept
            Assert.Equal("Arsenal ES", row.TargetTextEs); // empty column still backfilled
            Assert.True(row.Processed); // the backfilled hit takes the row off the worklist
        }

        [Fact]
        public async Task Backfill_FlagOff_LeavesTargetsEmpty()
        {
            var (repository, provider) = CreateRepository(nameof(Backfill_FlagOff_LeavesTargetsEmpty), cacheBackfill: false);

            AddLegacyTranslation(provider, "Arsenal", "ZH", "阿森纳");

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var context = GetContext(provider);
            var row = context.TranslationCache2.Single(r => r.StringId == "Te:H1");
            Assert.Equal(string.Empty, row.TargetTextZh);
            Assert.False(row.Processed);
        }

        [Fact]
        public async Task Backfill_NormalizesLegacyLanguageCasing()
        {
            var (repository, provider) = CreateRepository(nameof(Backfill_NormalizesLegacyLanguageCasing));

            AddLegacyTranslation(provider, "Arsenal", "zh", "阿森纳");

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var context = GetContext(provider);
            Assert.Equal("阿森纳", context.TranslationCache2.Single(r => r.StringId == "Te:H1").TargetTextZh);
        }

        [Fact]
        public async Task Backfill_SkipsUnsupportedLanguagesAndUntranslatedLegacyRows()
        {
            var (repository, provider) = CreateRepository(nameof(Backfill_SkipsUnsupportedLanguagesAndUntranslatedLegacyRows));

            AddLegacyTranslation(provider, "Arsenal", "JA", "アーセナル"); // no column in the flat schema
            AddLegacyTranslation(provider, "Arsenal", "ZH", null);        // legacy worklist row, not translated

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var context = GetContext(provider);
            var row = context.TranslationCache2.Single(r => r.StringId == "Te:H1");

            Assert.Equal(string.Empty, row.TargetTextZh);
            Assert.Equal(string.Empty, row.TargetTextEs);
            Assert.Equal(string.Empty, row.TargetTextFr);
        }

        [Fact]
        public async Task Backfill_TruncatesLegacyTargetTextToColumnLimit()
        {
            var (repository, provider) = CreateRepository(nameof(Backfill_TruncatesLegacyTargetTextToColumnLimit));

            AddLegacyTranslation(provider, "Arsenal", "ZH", new string('翻', 400));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var context = GetContext(provider);
            Assert.Equal(250, context.TranslationCache2.Single(r => r.StringId == "Te:H1").TargetTextZh.Length);
        }
    }
}
