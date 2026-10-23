using OE.Job.BoostedOddsCap.Boosts;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Job.BoostedOddsCap.Tests
{
    // Builders for the odds change message and the boost rules the tests judge. The message classes are the real
    // ones from OE.Shared, so a rename of a wire property breaks these tests as well as the scanner contract test.
    internal static class TestMessages
    {
        public const string FixtureId = "sr:match:1234500";

        public static OnInternalOddsChangeMessage OddsChange(string providerFixtureId, int fixtureStatusId,
            params InternalMarketMessage[] markets)
        {
            return new OnInternalOddsChangeMessage
            {
                SpId = 1,
                SpNa = "Soccer",
                SegId = 2,
                SegNa = "International",
                LeId = 3,
                LeNa = "World Cup",
                FiId = 99,
                PrFiId = providerFixtureId,
                FiNa = "Team A vs. Team B",
                FiStId = fixtureStatusId,
                Ma = markets.ToList()
            };
        }

        public static InternalMarketMessage Market(string marketTemplateId, string marketSpecifier, string clientCodes,
            string clientMarketConfig, int marketStatusId, params InternalSelectionMessage[] selections)
        {
            return new InternalMarketMessage
            {
                MaTeId = marketTemplateId,
                MaSp = marketSpecifier,
                MaNa = "Match Winner",
                MaStId = marketStatusId,
                ClCo = clientCodes,
                ClCoMa = clientMarketConfig,
                Se = selections.ToList()
            };
        }

        public static InternalSelectionMessage Selection(string selectionTemplateId, string selectionSpecifier, decimal odds,
            bool isActive = true)
        {
            return new InternalSelectionMessage
            {
                SeTeId = selectionTemplateId,
                SeSp = selectionSpecifier,
                Od = odds,
                IsAc = isActive,
                SeNa = $"Selection {selectionTemplateId}"
            };
        }

        // A market with two selections at the same odds and a client margin equal to the market's own, so the
        // client's odds are the master odds and the numbers in a test are easy to follow.
        public static InternalMarketMessage EvenMarket(decimal odds, string clientCodes = "S88|TSO")
        {
            // two selections at odds 2.00 sum to exactly 100% so the market margin is 0
            return Market("1", null, clientCodes, "S88:0|TSO:0", 1,
                    Selection("1", null, odds), Selection("2", null, odds));
        }

        public static BoostRule Rule(string clientCode = "S88", string providerFixtureId = FixtureId,
            string marketTemplateId = "1", string marketSpecifier = "", string selectionTemplateId = "1",
            string selectionSpecifier = "", decimal boostedOdds = 2.10m, decimal capPercent = 5m)
        {
            return new BoostRule
            {
                Key = BoostRule.BuildKey(clientCode, providerFixtureId, marketTemplateId, marketSpecifier,
                        selectionTemplateId, selectionSpecifier),
                ClientCode = clientCode,
                ProviderFixtureId = providerFixtureId,
                MarketTemplateId = marketTemplateId,
                MarketSpecifier = marketSpecifier,
                SelectionTemplateId = selectionTemplateId,
                SelectionSpecifier = selectionSpecifier,
                BoostedOdds = boostedOdds,
                CapPercent = capPercent
            };
        }
    }
}
