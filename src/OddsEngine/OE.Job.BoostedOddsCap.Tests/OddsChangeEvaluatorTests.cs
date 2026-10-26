using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Job.BoostedOddsCap.Tests
{
    // End to end through the pure part of the job: a real message, real rules, the real tracker. The base case is
    // a market of two selections at 2.00 (margin 0, the client's margin 0, so the client sees 2.00) and a boost of
    // 2.10 for the first selection, a gap of 2.44% against a 5% cap.
    public class OddsChangeEvaluatorTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

        private static OddsChangeEvaluator NewEvaluator(ISet<string> playerPropMarketTemplateIds = null)
            => new OddsChangeEvaluator(new CapAlertTracker(0.25m), playerPropMarketTemplateIds);

        private static IReadOnlyList<CapAlert> Evaluate(OddsChangeEvaluator evaluator, OnInternalOddsChangeMessage message,
            params BoostRule[] rules) => evaluator.Evaluate(message, rules, Now);

        private static OnInternalOddsChangeMessage Prematch(InternalMarketMessage market)
            => TestMessages.OddsChange(TestMessages.FixtureId, 0, market);

        [Fact]
        public void AlertsWhenTheClientsOddsHaveComeWithinTheCap()
        {
            var alerts = Evaluate(NewEvaluator(), Prematch(TestMessages.EvenMarket(2.00m)), TestMessages.Rule());

            var alert = Assert.Single(alerts);
            Assert.Equal("S88", alert.ClientCode);
            Assert.Equal(2.10m, alert.BoostedOdds);
            Assert.Equal(2.00m, alert.CurrentOdds);
            Assert.Equal(5m, alert.CapPercent);
            Assert.Equal(2.4390m, Math.Round(alert.GapPercent, 4));
        }

        [Fact]
        public void CarriesTheNamesTheToastAndEmailPrint()
        {
            var alert = Assert.Single(Evaluate(NewEvaluator(), Prematch(TestMessages.EvenMarket(2.00m)), TestMessages.Rule()));

            Assert.Equal("Soccer", alert.SportName);
            Assert.Equal("International", alert.SegmentName);
            Assert.Equal("World Cup", alert.LeagueName);
            Assert.Equal("Team A vs. Team B", alert.FixtureName);
            Assert.Equal("Match Winner", alert.MarketName);
            Assert.Equal("Selection 1", alert.SelectionName);
            Assert.Equal(TestMessages.FixtureId, alert.ProviderFixtureId);
            Assert.Equal(Now, alert.DetectedAtUtc);
        }

        [Fact]
        public void DoesNotAlertWhileTheBoostIsStillWellAboveTheMarket()
        {
            // 2.50 against 2.00 is an 11.1% gap
            var alerts = Evaluate(NewEvaluator(), Prematch(TestMessages.EvenMarket(2.00m)),
                    TestMessages.Rule(boostedOdds: 2.50m));

            Assert.Empty(alerts);
        }

        [Fact]
        public void AlertsWhenTheMarketHasPassedTheBoost()
        {
            var alerts = Evaluate(NewEvaluator(), Prematch(TestMessages.EvenMarket(2.20m)), TestMessages.Rule());

            Assert.Single(alerts);
        }

        [Fact]
        public void DoesNotAlertTwiceForTheSameBreach()
        {
            var evaluator = NewEvaluator();
            var message = Prematch(TestMessages.EvenMarket(2.00m));

            Assert.Single(Evaluate(evaluator, message, TestMessages.Rule()));
            Assert.Empty(Evaluate(evaluator, message, TestMessages.Rule()));
        }

        [Fact]
        public void AlertsOnceForEachClientWhoseBoostIsInsideItsCap()
        {
            var alerts = Evaluate(NewEvaluator(), Prematch(TestMessages.EvenMarket(2.00m)),
                    TestMessages.Rule(clientCode: "S88"), TestMessages.Rule(clientCode: "TSO"));

            Assert.Equal(new[] { "S88", "TSO" }, alerts.Select(a => a.ClientCode).OrderBy(c => c));
        }

        [Fact]
        public void IgnoresALiveFixture()
        {
            var message = TestMessages.OddsChange(TestMessages.FixtureId, 1, TestMessages.EvenMarket(2.00m));

            Assert.Empty(Evaluate(NewEvaluator(), message, TestMessages.Rule()));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(-3)]
        public void IgnoresAMarketThatIsNotActive(int marketStatusId)
        {
            var market = TestMessages.Market("1", null, "S88", "S88:0", marketStatusId,
                    TestMessages.Selection("1", null, 2.00m), TestMessages.Selection("2", null, 2.00m));

            Assert.Empty(Evaluate(NewEvaluator(), Prematch(market), TestMessages.Rule()));
        }

        [Fact]
        public void IgnoresAMarketOfAnotherTemplate()
        {
            Assert.Empty(Evaluate(NewEvaluator(), Prematch(TestMessages.EvenMarket(2.00m)),
                    TestMessages.Rule(marketTemplateId: "2")));
        }

        [Fact]
        public void MatchesTheMarketSpecifierSoOneLineOfATotalsMarketIsNotConfusedWithAnother()
        {
            var market = TestMessages.Market("18", "total=2.5", "S88", "S88:0", 1,
                    TestMessages.Selection("1", null, 2.00m), TestMessages.Selection("2", null, 2.00m));

            Assert.Empty(Evaluate(NewEvaluator(), Prematch(market),
                    TestMessages.Rule(marketTemplateId: "18", marketSpecifier: "total=3.5")));
            Assert.Single(Evaluate(NewEvaluator(), Prematch(market),
                    TestMessages.Rule(marketTemplateId: "18", marketSpecifier: "total=2.5")));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void TreatsNullEmptyAndBlankSpecifiersAsTheSame(string marketSpecifier)
        {
            var market = TestMessages.Market("1", marketSpecifier, "S88", "S88:0", 1,
                    TestMessages.Selection("1", null, 2.00m), TestMessages.Selection("2", null, 2.00m));

            Assert.Single(Evaluate(NewEvaluator(), Prematch(market), TestMessages.Rule(marketSpecifier: "")));
        }

        [Fact]
        public void MatchesTheSelectionSpecifier()
        {
            var market = TestMessages.Market("1", null, "S88", "S88:0", 1,
                    TestMessages.Selection("1", "hcp=-1", 2.00m), TestMessages.Selection("1", "hcp=1", 2.00m));

            Assert.Empty(Evaluate(NewEvaluator(), Prematch(market), TestMessages.Rule(selectionSpecifier: "hcp=2")));
            Assert.Single(Evaluate(NewEvaluator(), Prematch(market), TestMessages.Rule(selectionSpecifier: "hcp=1")));
        }

        [Fact]
        public void IgnoresAMarketTheClientIsNotOfferedAndAClientWhoIsNotListed()
        {
            // the market is offered to TSO only
            Assert.Empty(Evaluate(NewEvaluator(), Prematch(TestMessages.EvenMarket(2.00m, clientCodes: "TSO")),
                    TestMessages.Rule(clientCode: "S88")));
        }

        [Fact]
        public void IgnoresASelectionThatIsSuspended()
        {
            var market = TestMessages.Market("1", null, "S88", "S88:0", 1,
                    TestMessages.Selection("1", null, 2.00m, isActive: false), TestMessages.Selection("2", null, 2.00m));

            Assert.Empty(Evaluate(NewEvaluator(), Prematch(market), TestMessages.Rule()));
        }

        [Fact]
        public void JudgesAgainstTheClientsOddsNotTheMasterOdds()
        {
            // master odds 1.90 on both sides. A 2.10 boost is exactly 5.0% above the master odds, which is inside
            // a 5% cap, so a job that read the master odds would alert here.
            var shorterForTheClient = TestMessages.Market("1", null, "S88", "S88:0.10", 1,
                    TestMessages.Selection("1", null, 1.90m), TestMessages.Selection("2", null, 1.90m));

            // the client's margin is higher than the market's, so the client sees 1.81: the gap to 2.10 is 7.4%
            Assert.Empty(Evaluate(NewEvaluator(), Prematch(shorterForTheClient), TestMessages.Rule(boostedOdds: 2.10m)));

            // a client whose margin equals the market's sees the master 1.90, so the same boost is on the line
            var sameAsMaster = TestMessages.Market("1", null, "S88", "S88:0.05", 1,
                    TestMessages.Selection("1", null, 1.90m), TestMessages.Selection("2", null, 1.90m));

            var alert = Assert.Single(Evaluate(NewEvaluator(), Prematch(sameAsMaster), TestMessages.Rule(boostedOdds: 2.10m)));
            Assert.Equal(1.90m, alert.CurrentOdds);
            Assert.Equal(5m, alert.GapPercent);
        }

        [Fact]
        public void PricesAPlayerPropMarketOnlyWhenItsTemplateIsConfigured()
        {
            var market = TestMessages.Market("9", null, "S88", "S88:0.10", 1,
                    TestMessages.Selection("1", null, 1.90m), TestMessages.Selection("2", null, 1.90m));
            var rule = TestMessages.Rule(marketTemplateId: "9", boostedOdds: 1.75m);

            // configured as a player prop: 1.90 - 1.90 * 0.10 = 1.71
            var asPlayerProp = Assert.Single(Evaluate(NewEvaluator(new HashSet<string> { "9" }), Prematch(market), rule));
            Assert.Equal(1.71m, asPlayerProp.CurrentOdds);

            // not configured: the regular repricing applies, which gives 1.81 for the same market
            var asRegular = Assert.Single(Evaluate(NewEvaluator(), Prematch(market), rule));
            Assert.Equal(1.81m, asRegular.CurrentOdds);
        }

        [Fact]
        public void ReturnsNothingForAMessageWithoutMarketsOrRules()
        {
            var evaluator = NewEvaluator();

            Assert.Empty(Evaluate(evaluator, TestMessages.OddsChange(TestMessages.FixtureId, 0), TestMessages.Rule()));
            Assert.Empty(Evaluate(evaluator, Prematch(TestMessages.EvenMarket(2.00m))));
            Assert.Empty(evaluator.Evaluate(null, new[] { TestMessages.Rule() }, Now));
        }
    }
}
