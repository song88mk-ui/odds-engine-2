using OE.Job.BoostedOddsCap.Evaluation;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Job.BoostedOddsCap.Tests
{
    // The expected prices are worked out by hand from the steps of OE.Websocket.Service's GetClientOdds (the
    // arithmetic is in the comments), so these pin the port to what clients see. The first two are the cases that
    // matter most: a client whose margin equals the market's is shown the master odds, and one whose margin is
    // higher is shown shorter odds.
    public class ClientOddsCalculatorTests
    {
        private static InternalMarketMessage TwoWay(string clientMarketConfig, decimal home, decimal away,
            bool awayActive = true)
        {
            return TestMessages.Market("1", null, "S88", clientMarketConfig, 1,
                    TestMessages.Selection("1", null, home),
                    TestMessages.Selection("2", null, away, awayActive));
        }

        private static decimal Price(InternalMarketMessage market, string selectionTemplateId, bool playerProp = false)
        {
            var selection = market.Se.First(s => s.SeTeId == selectionTemplateId);

            Assert.True(ClientOddsCalculator.TryGetClientOdds(market, selection, "S88", playerProp, out var odds));
            return odds;
        }

        [Fact]
        public void ClientWhoseMarginMatchesTheMarketGetsTheMasterOdds()
        {
            // 100/1.90 * 2 = 105.26 -> 105, so the market margin is 0.05; the client's is 0.05 too
            Assert.Equal(1.90m, Price(TwoWay("S88:0.05", 1.90m, 1.90m), "1"));
        }

        [Fact]
        public void ClientWithAHigherMarginThanTheMarketGetsShorterOdds()
        {
            // fair = 2 * 1.90 / (2 - 0.05 * 1.90) = 1.99475; final = 1.99475 * 0.10 / 2 + 1 = 1.09974
            // 1.99475 / 1.09974 = 1.8138 -> 1.81
            Assert.Equal(1.81m, Price(TwoWay("S88:0.10", 1.90m, 1.90m), "1"));
        }

        [Fact]
        public void ClientWithALowerMarginIsCappedAtTheMasterOddsWithTheAddedMargin()
        {
            // the repriced 1.96 would be above the master 1.90, so it falls back to 100 / (100 / 1.90 + 2) = 1.83
            Assert.Equal(1.83m, Price(TwoWay("S88:0.02", 1.90m, 1.90m), "1"));
        }

        [Fact]
        public void ThreeWayMarketIsRepricedAcrossAllThreeSelections()
        {
            // 100/2.5 + 100/3.2 + 100/2.9 = 105.73 -> 106, margin 0.06; fair = 3 * 2.5 / (3 - 0.06 * 2.5) = 2.63158
            // final = 2.63158 * 0.10 / 3 + 1 = 1.08772; 2.63158 / 1.08772 = 2.4194 -> 2.42
            var market = TestMessages.Market("1", null, "S88", "S88:0.10", 1,
                    TestMessages.Selection("1", null, 2.50m),
                    TestMessages.Selection("X", null, 3.20m),
                    TestMessages.Selection("2", null, 2.90m));

            Assert.Equal(2.42m, Price(market, "1"));
        }

        [Fact]
        public void LoneActiveSelectionIsRepricedAsIfTheMarketWereWhole()
        {
            // one active selection falls back to every selection that has odds, as the websocket service does;
            // without that the margin would come out at -0.47 and the price would be wrong
            Assert.Equal(1.81m, Price(TwoWay("S88:0.10", 1.90m, 1.90m, awayActive: false), "1"));
        }

        [Fact]
        public void PlayerPropMarketTakesTheMarginOffTheOddsDirectly()
        {
            // 2.00 - 2.00 * 0.05
            var market = TestMessages.Market("9", null, "S88", "S88:0.05", 1,
                    TestMessages.Selection("1", null, 2.00m), TestMessages.Selection("2", null, 2.00m));

            Assert.Equal(1.90m, Price(market, "1", playerProp: true));
        }

        [Fact]
        public void PlayerPropMarketLeavesSelectionTypes12And13OnTheRegularPath()
        {
            var market = TestMessages.Market("9", null, "S88", "S88:0", 1,
                    TestMessages.Selection("12", null, 2.00m), TestMessages.Selection("13", null, 2.00m));

            // margin 0 equals the client's 0, so the master odds
            Assert.Equal(2.00m, Price(market, "12", playerProp: true));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("TSO:0.10")]
        public void ClientWithNoMarginForTheMarketSeesTheSourceOdds(string clientMarketConfig)
        {
            Assert.Equal(1.90m, Price(TwoWay(clientMarketConfig, 1.90m, 1.90m), "1"));
        }

        [Fact]
        public void SuspendedSelectionHasNoPrice()
        {
            var market = TwoWay("S88:0.05", 1.90m, 1.90m, awayActive: false);

            Assert.False(ClientOddsCalculator.TryGetClientOdds(market, market.Se[1], "S88", false, out var odds));
            Assert.Equal(0m, odds);
        }

        [Fact]
        public void SelectionWithoutOddsHasNoPrice()
        {
            var market = TwoWay("S88:0.05", 0m, 1.90m);

            Assert.False(ClientOddsCalculator.TryGetClientOdds(market, market.Se[0], "S88", false, out _));
        }

        [Fact]
        public void TryGetClientMargin_TakesTheLastSegmentOfTheClientsEntry()
        {
            Assert.True(ClientOddsCalculator.TryGetClientMargin("TSO:0.03|S88:x:0.07", "S88", out var margin));
            Assert.Equal(0.07m, margin);
        }

        [Fact]
        public void TryGetClientMargin_SkipsEntriesItCannotRead()
        {
            Assert.False(ClientOddsCalculator.TryGetClientMargin("S88:abc|S88", "S88", out _));
        }

        [Theory]
        [InlineData(1.01, 1.01)]
        [InlineData(1.00, 1.00)]
        [InlineData(1.90, 1.83)]
        [InlineData(2.00, 1.92)]
        public void GetMasterOddsWithAddedMargin_AddsTwoPointsOfMarginExceptAtTheFloor(double odds, double expected)
        {
            Assert.Equal((decimal)expected, ClientOddsCalculator.GetMasterOddsWithAddedMargin((decimal)odds));
        }
    }
}
