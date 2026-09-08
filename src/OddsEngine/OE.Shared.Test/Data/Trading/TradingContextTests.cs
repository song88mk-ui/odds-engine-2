using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OE.Shared.Data.Trading;
using OE.Shared.Data.Trading.Entities;

namespace OE.Shared.Test.Data.Trading
{
    // OnModelCreating's only real logic is the composite keys below — everything else is column
    // mapping the InMemory provider ignores. These tests exist to pin those composite keys: a
    // mistake there (e.g. dropping SportId) would silently let one client's rows for different
    // sports overwrite each other.
    //
    // Each scenario is seeded exactly once (static field initializer below its Seed* method),
    // not once per Fact: a real test and its MutationCheck companion read the same already-saved
    // result instead of each rebuilding a DI container + InMemory database to re-derive it.
    public class TradingContextTests
    {
        private static IServiceProvider NewProvider(string databaseName)
        {
            var services = new ServiceCollection();
            services.AddDbContext<TradingContext>(options => options.UseInMemoryDatabase(databaseName));
            return services.BuildServiceProvider();
        }

        private static TradingContext NewContext(string databaseName) =>
            NewProvider(databaseName).CreateScope().ServiceProvider.GetRequiredService<TradingContext>();

        private static readonly Guid RoundTripClientId = Guid.NewGuid();
        private static readonly ClientOddsFeedConfiguration RoundTripStored = SeedRoundTrip(RoundTripClientId);

        private static ClientOddsFeedConfiguration SeedRoundTrip(Guid clientId)
        {
            using var context = NewContext(nameof(RoundTripStored));
            context.ClientOddsFeedConfiguration.Add(new ClientOddsFeedConfiguration
            {
                ClientId = clientId,
                SportId = 1,
                PrematchOdds = true,
                LiveOdds = false,
                MaxFixtures = 10
            });
            context.SaveChanges();

            return context.ClientOddsFeedConfiguration.Single(c => c.ClientId == clientId && c.SportId == 1);
        }

        [Fact]
        public void ClientOddsFeedConfiguration_RoundTripsThroughDbSet()
        {
            Assert.True(RoundTripStored.PrematchOdds);
            Assert.Equal(10, RoundTripStored.MaxFixtures);
        }

        // (ClientId, SportId) is the composite key: the same client can have one row per sport.
        private static readonly Guid DistinctRowsClientId = Guid.NewGuid();
        private static readonly int DistinctRowsCount = SeedDistinctRows(DistinctRowsClientId);

        private static int SeedDistinctRows(Guid clientId)
        {
            using var context = NewContext(nameof(DistinctRowsCount));
            context.ClientOddsFeedConfiguration.Add(new ClientOddsFeedConfiguration { ClientId = clientId, SportId = 1, PrematchOdds = true });
            context.ClientOddsFeedConfiguration.Add(new ClientOddsFeedConfiguration { ClientId = clientId, SportId = 2, PrematchOdds = false });
            context.SaveChanges();

            return context.ClientOddsFeedConfiguration.Count(c => c.ClientId == clientId);
        }

        [Fact]
        public void ClientOddsFeedConfiguration_SameClientDifferentSport_AreDistinctRows()
        {
            Assert.Equal(2, DistinctRowsCount);
        }

        private static readonly Exception? DuplicateKeyException = SeedDuplicateKey();

        private static Exception? SeedDuplicateKey()
        {
            var clientId = Guid.NewGuid();
            var provider = NewProvider(nameof(DuplicateKeyException));

            using (var firstScope = provider.CreateScope())
            {
                var first = firstScope.ServiceProvider.GetRequiredService<TradingContext>();
                first.ClientOddsFeedConfiguration.Add(new ClientOddsFeedConfiguration { ClientId = clientId, SportId = 1 });
                first.SaveChanges();
            }

            using var secondScope = provider.CreateScope();
            var second = secondScope.ServiceProvider.GetRequiredService<TradingContext>();
            second.ClientOddsFeedConfiguration.Add(new ClientOddsFeedConfiguration { ClientId = clientId, SportId = 1 });

            // EF Core InMemory 3.1.x surfaces a duplicate primary key as a raw ArgumentException
            // from its backing dictionary rather than wrapping it in DbUpdateException.
            return Record.Exception(() => second.SaveChanges());
        }

        [Fact]
        public void ClientOddsFeedConfiguration_DuplicateCompositeKey_ThrowsOnSecondContextSave()
        {
            Assert.IsType<ArgumentException>(DuplicateKeyException);
        }

        private static readonly Guid LiveFixtureClientId = Guid.NewGuid();
        private static readonly ClientOddsFeedLiveFixture LiveFixtureStored = SeedLiveFixture(LiveFixtureClientId);

        private static ClientOddsFeedLiveFixture SeedLiveFixture(Guid clientId)
        {
            using var context = NewContext(nameof(LiveFixtureStored));
            context.ClientOddsFeedLiveFixture.Add(new ClientOddsFeedLiveFixture
            {
                ClientId = clientId,
                SportId = 1,
                FixtureSpecifier = "F1",
                FixtureStartDate = new DateTime(2026, 1, 1)
            });
            context.SaveChanges();

            return context.ClientOddsFeedLiveFixture
                .Single(f => f.ClientId == clientId && f.SportId == 1 && f.FixtureSpecifier == "F1");
        }

        [Fact]
        public void ClientOddsFeedLiveFixture_RoundTripsThroughDbSet()
        {
            Assert.Equal(new DateTime(2026, 1, 1), LiveFixtureStored.FixtureStartDate);
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check reads the same seeded result as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void ClientOddsFeedConfiguration_RoundTripsThroughDbSet_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(RoundTripStored.PrematchOdds));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(11, RoundTripStored.MaxFixtures));
        }

        [Fact]
        public void ClientOddsFeedConfiguration_SameClientDifferentSport_AreDistinctRows_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, DistinctRowsCount));
        }

        [Fact]
        public void ClientOddsFeedConfiguration_DuplicateCompositeKey_ThrowsOnSecondContextSave_MutationCheck()
        {
            // The real test proves SaveChanges throws ArgumentException on a duplicate composite
            // key. This proves the proof itself discriminates: asserting that NO exception was
            // thrown must fail here, since one really is.
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Null(DuplicateKeyException));
        }

        [Fact]
        public void ClientOddsFeedLiveFixture_RoundTripsThroughDbSet_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new DateTime(2026, 1, 2), LiveFixtureStored.FixtureStartDate));
        }

        #endregion
    }
}
