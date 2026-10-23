using Microsoft.EntityFrameworkCore;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Tests
{
    public class BoostSnapshotTests
    {
        private static readonly DateTime Loaded = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

        private static PriceBoostRow Boost(string clientCode = "S88", string fixtureId = TestMessages.FixtureId,
            string marketSpecifier = null, string selectionSpecifier = null, decimal boostedOdds = 2.10m)
        {
            return new PriceBoostRow
            {
                ClientCode = clientCode,
                ProviderFixtureId = fixtureId,
                MarketTemplateId = "1",
                MarketSpecifier = marketSpecifier,
                SelectionTemplateId = "1",
                SelectionSpecifier = selectionSpecifier,
                BoostedOdds = boostedOdds
            };
        }

        private static BoostedSettingsRow Cap(string clientCode = "S88", decimal cap = 0.05m)
            => new BoostedSettingsRow { ClientCode = clientCode, Cap = cap };

        private static BoostSnapshot Build(PriceBoostRow[] boosts, BoostedSettingsRow[] settings, params ClientRow[] clients)
            => BoostSnapshot.Build(boosts, settings, clients, Loaded);

        [Fact]
        public void JoinsEachBoostWithItsClientsCapAsAPercentage()
        {
            var snapshot = Build(new[] { Boost() }, new[] { Cap(cap: 0.05m) });

            Assert.True(snapshot.TryGetRules(TestMessages.FixtureId, out var rules));
            var rule = Assert.Single(rules);
            Assert.Equal(5m, rule.CapPercent);
            Assert.Equal("S88", rule.ClientCode);
            Assert.Equal(2.10m, rule.BoostedOdds);
            Assert.Equal(1, snapshot.RuleCount);
            Assert.Equal(1, snapshot.FixtureCount);
            Assert.Equal(Loaded, snapshot.LoadedAtUtc);
        }

        [Fact]
        public void SkipsABoostWhoseClientHasNoCapAndCountsIt()
        {
            var snapshot = Build(new[] { Boost(clientCode: "S88"), Boost(clientCode: "TSO") }, new[] { Cap("S88") });

            Assert.Equal(1, snapshot.RuleCount);
            Assert.Equal(1, snapshot.RowsWithoutCap);
        }

        [Fact]
        public void MatchesClientCodesAcrossPaddingAndCase()
        {
            // BoostedSettings.ClientCode comes back padded from SQL
            var snapshot = Build(new[] { Boost(clientCode: "s88") }, new[] { Cap("S88   ") });

            Assert.True(snapshot.TryGetRules(TestMessages.FixtureId, out var rules));
            Assert.Equal("S88", Assert.Single(rules).ClientCode);
        }

        [Fact]
        public void TurnsANullSpecifierIntoAnEmptyOne()
        {
            var snapshot = Build(new[] { Boost(marketSpecifier: null, selectionSpecifier: "  ") }, new[] { Cap() });

            snapshot.TryGetRules(TestMessages.FixtureId, out var rules);
            Assert.Equal(string.Empty, rules[0].MarketSpecifier);
            Assert.Equal(string.Empty, rules[0].SelectionSpecifier);
        }

        [Fact]
        public void DropsExactDuplicatesBecauseTheTableHasNoPrimaryKey()
        {
            var snapshot = Build(new[] { Boost(), Boost(), Boost(selectionSpecifier: "other") }, new[] { Cap() });

            Assert.Equal(2, snapshot.RuleCount);
        }

        [Fact]
        public void SkipsRowsWithNullColumnsRatherThanFailingTheWholeLoad()
        {
            // every column of the three tables is NULL-able; one such row must not cost the job its whole index
            var snapshot = Build(
                new[]
                {
                    Boost(),
                    new PriceBoostRow { ClientCode = "S88", ProviderFixtureId = "other", MarketTemplateId = "1", SelectionTemplateId = "1", BoostedOdds = null },
                    new PriceBoostRow { ClientCode = null, ProviderFixtureId = "another", MarketTemplateId = "1", SelectionTemplateId = "1", BoostedOdds = 2m }
                },
                new[] { Cap(), new BoostedSettingsRow { ClientCode = null, Cap = 0.1m } });

            Assert.Equal(1, snapshot.RuleCount);
        }

        [Fact]
        public void TreatsAClientWhoseCapIsNullAsHavingNoCap()
        {
            var snapshot = Build(new[] { Boost(clientCode: "TSO") },
                    new[] { new BoostedSettingsRow { ClientCode = "TSO", Cap = null } });

            Assert.Equal(0, snapshot.RuleCount);
            Assert.Equal(1, snapshot.RowsWithoutCap);
        }

        [Fact]
        public void IgnoresARowThatCannotBeEvaluated()
        {
            var snapshot = Build(new[] { Boost(boostedOdds: 0m), Boost(fixtureId: " ") }, new[] { Cap() });

            Assert.Equal(0, snapshot.RuleCount);
        }

        [Fact]
        public void GroupsRulesByFixture()
        {
            var snapshot = Build(new[] { Boost(fixtureId: "a"), Boost(fixtureId: "a", clientCode: "TSO"), Boost(fixtureId: "b") },
                    new[] { Cap("S88"), Cap("TSO") });

            Assert.Equal(2, snapshot.FixtureCount);
            snapshot.TryGetRules("a", out var rulesOfA);
            Assert.Equal(2, rulesOfA.Length);
            Assert.Equal(3, snapshot.RuleKeys.Count());
        }

        [Fact]
        public void ReportsAFixtureWithNoBoostsAsAMiss()
        {
            var snapshot = Build(new[] { Boost() }, new[] { Cap() });

            Assert.False(snapshot.TryGetRules("sr:match:other", out var rules));
            Assert.Empty(rules);
            Assert.False(snapshot.TryGetRules(null, out _));
            Assert.False(BoostSnapshot.Empty.TryGetRules(TestMessages.FixtureId, out _));
        }

        [Fact]
        public void ReadsTheRecipientsOfAClientCaseInsensitively()
        {
            var snapshot = Build(new[] { Boost() }, new[] { Cap() },
                    new ClientRow { ClientCode = "S88", Email = "ops@example.com" });

            Assert.Equal(new[] { "ops@example.com" }, snapshot.RecipientsFor("s88"));
            Assert.Empty(snapshot.RecipientsFor("TSO"));
        }

        [Fact]
        public void ParseRecipients_SplitsOnSemicolonsAndCommasAndDropsWhatIsNotAnAddress()
        {
            var recipients = BoostSnapshot.ParseRecipients("a@example.com; b@example.com, not-an-address,A@EXAMPLE.COM ,");

            Assert.Equal(new[] { "a@example.com", "b@example.com" }, recipients);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseRecipients_ReturnsNothingForAnEmptyField(string email)
        {
            Assert.Empty(BoostSnapshot.ParseRecipients(email));
        }

        // The job reads three tables it does not own. If a name here drifts from backoffice-final's mapping
        // (Backoffice.Shared/Data/Trading), the job loads nothing and the only symptom is silence.
        [Fact]
        public void TheContextReadsTheTradingTablesAndColumnsBackofficeWrites()
        {
            var options = new DbContextOptionsBuilder<BoostContext>()
                    .UseSqlServer("Server=localhost;Database=Trading;Integrated Security=true")
                    .Options;

            using var context = new BoostContext(options);

            AssertMapped(context, typeof(PriceBoostRow), "PriceBoostConfig",
                    "ClientCode", "ProviderFixtureId", "MarketTemplateId", "MarketSpecifier",
                    "SelectionTemplateId", "SelectionSpecifier", "BoostedOdds");
            AssertMapped(context, typeof(BoostedSettingsRow), "BoostedSettings", "ClientCode", "Cap");
            AssertMapped(context, typeof(ClientRow), "Client", "ClientCode", "Email");
        }

        private static void AssertMapped(BoostContext context, Type entity, string table, params string[] columns)
        {
            var entityType = context.Model.FindEntityType(entity);

            Assert.Equal(table, entityType.GetTableName());
            Assert.Null(entityType.FindPrimaryKey());
            Assert.Equal(columns.OrderBy(c => c), entityType.GetProperties().Select(p => p.GetColumnName()).OrderBy(c => c));
        }
    }
}
