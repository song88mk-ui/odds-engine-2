using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Data.Translation;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Translation.Service.Repositories;

namespace OE.Translation.Service.Tests
{
    // Seeding semantics for the flat TranslationCache2 — the single translation store (2026-07
    // translation-flow redesign; the retired SportsbookDynamic/SportsbookGlossary tables are
    // gone). The service owns the SOURCE side and the liveness touch; it must NEVER touch the
    // TARGET_TEXT_* columns, which belong to the external translation pipeline (empty target =
    // the pipeline's worklist).
    public class TranslationRepositoryTests
    {
        #region Setup

        private static (TranslationRepository Repository, ServiceProvider Provider) CreateRepository(
            string databaseName,
            bool cacheSeed = true)
        {
            var services = new ServiceCollection();
            services.AddDbContext<TranslationContext>(options => options.UseInMemoryDatabase(databaseName));
            var provider = services.BuildServiceProvider();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Custom:Translation:CacheSeed"] = cacheSeed.ToString()
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

        private static OnFixtureChangeMessage FixtureMessage(
            string homeName = "Arsenal",
            string leagueName = "Premier League")
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
                LeNa = leagueName,
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
                        MaNa = "Match Result",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M1",
                                PrSeId = "S1",
                                MaTeId = "16",
                                SeTeId = "1",
                                SeTeNa = "Home",
                                SeNa = "Arsenal"
                            }
                        }
                    }
                }
            };
        }

        #endregion

        [Fact]
        public async Task FixtureUpdate_SeedsSourceRowsWithEmptyTargets()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_SeedsSourceRowsWithEmptyTargets));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(new[] { "LeId:3", "SegId:2", "SpId:1", "Te:A1", "Te:H1" }, rows.Keys.OrderBy(k => k).ToArray());
            Assert.Equal("Arsenal", rows["Te:H1"].SourceText);
            Assert.Equal("Premier League", rows["LeId:3"].SourceText);

            foreach (var row in rows.Values)
            {
                Assert.Equal("EN", row.SourceLanguage);
                Assert.Equal(string.Empty, row.TargetTextZh);
                Assert.Equal(string.Empty, row.TargetTextEs);
                Assert.Equal(string.Empty, row.TargetTextFr);
                Assert.False(row.HasBetPlacement);
                Assert.False(row.Processed); // fresh rows are on the pipeline's worklist
                Assert.NotNull(row.CreatedAt);
            }
        }

        [Fact]
        public async Task FixtureUpdate_SourceNameChange_ClearsTargetsAndReopensWorklist()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_SourceNameChange_ClearsTargetsAndReopensWorklist));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(homeName: "Arsenal"));

            using (var context = GetContext(provider))
            {
                var row = context.TranslationCache2.Single(r => r.StringId == "Te:H1");
                row.TargetTextZh = "阿森纳"; // the external pipeline translated it in the meantime
                row.Processed = true;
                context.SaveChanges();
            }

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(homeName: "Arsenal FC"));

            using var verify = GetContext(provider);
            var updated = verify.TranslationCache2.Single(r => r.StringId == "Te:H1");

            Assert.Equal("Arsenal FC", updated.SourceText); // source side updated
            Assert.Equal(string.Empty, updated.TargetTextZh); // stale target invalidated
            Assert.False(updated.Processed); // back on the pipeline's worklist
        }

        [Fact]
        public async Task FixtureUpdate_UnchangedSource_LivenessTouchesUpdatedAtWithoutDuplicating()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_UnchangedSource_LivenessTouchesUpdatedAtWithoutDuplicating));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using (var context = GetContext(provider))
            {
                // Inserts don't set UPDATED_AT; only re-sightings do.
                Assert.All(context.TranslationCache2, r => Assert.Null(r.UpdatedAt));
            }

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var verify = GetContext(provider);

            // Still one row per key, but every row got the liveness touch that shields it from
            // OE.Translation.Job's retention sweep.
            Assert.Equal(5, verify.TranslationCache2.Count());
            Assert.All(verify.TranslationCache2, r => Assert.NotNull(r.UpdatedAt));
        }

        [Fact]
        public async Task MarketUpdate_SeedsTemplateAndNamespacedSelectionKeys()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_SeedsTemplateAndNamespacedSelectionKeys));

            await repository.OnProcessMarketSetUpdate(MarketMessage());

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(2, rows.Count);
            Assert.Equal("Match Result", rows["MaTe:16"].SourceText);
            Assert.Equal("Home", rows["SeTe:16:1"].SourceText);
        }

        [Fact]
        public async Task CacheSeedDisabled_WritesNothing()
        {
            var (repository, provider) = CreateRepository(nameof(CacheSeedDisabled_WritesNothing), cacheSeed: false);

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());
            await repository.OnProcessMarketSetUpdate(MarketMessage());

            using var context = GetContext(provider);
            Assert.Empty(context.TranslationCache2);
        }

        [Fact]
        public async Task Seeding_TruncatesSourceTextToColumnLimit()
        {
            var (repository, provider) = CreateRepository(nameof(Seeding_TruncatesSourceTextToColumnLimit));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(leagueName: new string('x', 400)));

            using var context = GetContext(provider);
            var row = context.TranslationCache2.Single(r => r.StringId == "LeId:3");

            Assert.Equal(250, row.SourceText.Length);
        }
    }
}
