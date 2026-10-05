using System.Globalization;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.WebSocket.Service.Repository;
using static OE.Websocket.Service.Tests.SportsbookRepositoryInternals;

namespace OE.Websocket.Service.Tests
{
    // The odds a client is shown are computed here and nowhere else: GetClientOdds applies the
    // per-client margin, and the three format converters turn the decimal price into the
    // Malaysian/Hongkong/Indonesian prices that ride on OutgoingSelection. Every mutant in this
    // block used to survive because no test executed a single line of it.
    public class SportsbookRepositoryOddsTests
    {
        private const string MarketTemplateId = "1";

        private static readonly List<MarketTemplatePlayerPropsResponseDTO> NoPlayerProps = new();

        private static SelectionMessage Selection(long seId, decimal odds, bool isActive = true, string seTeId = "1") =>
            new() { SeId = seId, Od = odds, IsAc = isActive, SeTeId = seTeId };

        // Theory data is written as strings: a double literal such as 2.78 does not survive the
        // cast to decimal exactly, and these assertions are about exact decimal prices.
        private static decimal Decimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

        // ---- GetMasterOddsWithAddedMargin ----

        // The fallback price when the margin maths cannot produce a sane number. 100/((100/odds)+2)
        // is the decimal price with two points of margin added.
        [Theory]
        [InlineData("2.00", "1.92")]
        [InlineData("3.00", "2.83")]
        [InlineData("1.50", "1.46")]
        public void GetMasterOddsWithAddedMargin_AddsTwoPointsOfMargin(string odds, string expected)
        {
            Assert.Equal(Decimal(expected), InvokeStatic<decimal>("GetMasterOddsWithAddedMargin", Decimal(odds)));
        }

        // At or below 1.01 the formula would push the price below evens, so the input is returned
        // untouched. 1.011 rounds to 1.01 and is therefore still on the untouched side.
        [Theory]
        [InlineData("1.01")]
        [InlineData("1.00")]
        [InlineData("1.011")]
        public void GetMasterOddsWithAddedMargin_AtOrBelowRoundedFloor_ReturnsInputUnchanged(string odds)
        {
            Assert.Equal(Decimal(odds), InvokeStatic<decimal>("GetMasterOddsWithAddedMargin", Decimal(odds)));
        }

        // ---- GetMalayOdds / GetHongkongOdds / GetIndoOdds ----

        // isBinary is false for markets with more than two selections; those keep the decimal
        // price in every format.
        [Theory]
        [InlineData("GetMalayOdds")]
        [InlineData("GetHongkongOdds")]
        [InlineData("GetIndoOdds")]
        public void FormatConverters_NonBinaryMarket_ReturnDecimalOddsUnchanged(string method)
        {
            Assert.Equal(3.25m, InvokeStatic<decimal>(method, 3.25m, false));
        }

        [Theory]
        [InlineData("GetMalayOdds")]
        [InlineData("GetHongkongOdds")]
        [InlineData("GetIndoOdds")]
        public void FormatConverters_OddsOfExactlyOne_ReturnZero(string method)
        {
            Assert.Equal(0m, InvokeStatic<decimal>(method, 1m, true));
        }

        // Malay: positive below evens (odds - 1), negative above it (-1/(odds-1), truncated).
        [Theory]
        [InlineData("1.90", "0.90")]
        [InlineData("2.00", "1.00")]
        [InlineData("2.50", "-0.66")]
        [InlineData("3.00", "-0.50")]
        public void GetMalayOdds_BinaryMarket_FlipsSignAboveEvens(string odds, string expected)
        {
            Assert.Equal(Decimal(expected), InvokeStatic<decimal>("GetMalayOdds", Decimal(odds), true));
        }

        [Theory]
        [InlineData("1.90", "0.90")]
        [InlineData("2.50", "1.50")]
        public void GetHongkongOdds_BinaryMarket_IsDecimalOddsMinusOne(string odds, string expected)
        {
            Assert.Equal(Decimal(expected), InvokeStatic<decimal>("GetHongkongOdds", Decimal(odds), true));
        }

        // Indo is Malay mirrored: positive at or above evens, negative below it.
        [Theory]
        [InlineData("2.00", "1.00")]
        [InlineData("2.50", "1.50")]
        [InlineData("1.90", "-1.11")]
        [InlineData("1.50", "-2.00")]
        public void GetIndoOdds_BinaryMarket_FlipsSignBelowEvens(string odds, string expected)
        {
            Assert.Equal(Decimal(expected), InvokeStatic<decimal>("GetIndoOdds", Decimal(odds), true));
        }

        // ---- GetClientOdds: which clients get a price at all ----

        [Fact]
        public void GetClientOdds_EmptyMarketConfig_ReturnsNothing()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.05m, new List<SelectionMessage> { Selection(1, 2.00m) }, string.Empty, "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Empty(result);
        }

        [Fact]
        public void GetClientOdds_ClientNotInMarketConfig_ReturnsNothing()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.05m, new List<SelectionMessage> { Selection(1, 2.00m) }, "S88:0.10", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Empty(result);
        }

        [Fact]
        public void GetClientOdds_NamedClient_PricesOnlyThatClient()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.10m, new List<SelectionMessage> { Selection(1, 2.00m) }, "TSO:0.10|S88:0.20", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Equal("TSO", Assert.Single(result).ClientCode);
        }

        // "OE" is the engine's own pseudo-client: it is not in any market config, and it gets a
        // price for every client in the config rather than for itself.
        [Fact]
        public void GetClientOdds_OEClientCode_PricesEveryClientInTheConfig()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.10m, new List<SelectionMessage> { Selection(1, 2.00m) }, "TSO:0.10|S88:0.20", "OE", MarketTemplateId, NoPlayerProps);

            Assert.Equal(new[] { "TSO", "S88" }, result.Select(o => o.ClientCode).ToArray());
        }

        // ---- GetClientOdds: pricing ----

        // The client's margin equals the market's own margin, so the price passes through and the
        // format conversions are applied to the untouched decimal odds.
        [Fact]
        public void GetClientOdds_ClientMarginMatchesMarketMargin_PublishesSourceOdds()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.10m, new List<SelectionMessage> { Selection(7, 2.50m) }, "TSO:0.10", "TSO", MarketTemplateId, NoPlayerProps);

            var odds = Assert.Single(result);
            Assert.Equal(2.50m, odds.DecimalOdds);
            Assert.Equal(-0.66m, odds.MalaysianOdds);
            Assert.Equal(1.50m, odds.HongkongOdds);
            Assert.Equal(1.50m, odds.IndonesianOdds);
            Assert.Equal(7L, odds.SelectionId);
            Assert.Equal("1", odds.SelectionTemplateId);
        }

        // The client wants more margin than the market carries, so the price is re-derived:
        // fair odds first (margin removed), then the client's margin applied.
        [Fact]
        public void GetClientOdds_ClientWantsMoreMargin_ShortensThePrice()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.05m, new List<SelectionMessage> { Selection(1, 2.00m) }, "TSO:0.10", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Equal(1.90m, Assert.Single(result).DecimalOdds);
        }

        // GM-811 removed the asian-odds passthrough that published source odds with no margin for
        // clients listed in Custom:AsianClientCodes. Its trigger (sport 1, market templates
        // 16/18/66/68) was inherited from the Betradar-era engine; in the Genius Sports id space the
        // platform uses, soccer is sport 10 and its handicap template is 82, so the branch could
        // never fire. Whether the legacy trigger ids or the real soccer handicap ids arrive, an Asian
        // client is priced like any other client: 2.00 at 5% market margin re-priced to a 10% client
        // margin is 1.90.
        [Theory]
        [InlineData(10, "82")]
        [InlineData(1, "16")]
        public void GetClientOdds_AsianClient_GetsMarginLikeAnyOtherClient(int sportId, string marketTemplateId)
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                sportId, 2, 0.05m, new List<SelectionMessage> { Selection(1, 2.00m) }, "S88:0.10", "S88", marketTemplateId, NoPlayerProps);

            Assert.Equal(1.90m, Assert.Single(result).DecimalOdds);
        }

        // An inactive selection, or one with no price, is published verbatim - the margin maths
        // is skipped entirely.
        [Fact]
        public void GetClientOdds_InactiveSelection_PublishesSourceOddsUntouched()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.05m, new List<SelectionMessage> { Selection(1, 2.00m, isActive: false) }, "TSO:0.10", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Equal(2.00m, Assert.Single(result).DecimalOdds);
        }

        [Fact]
        public void GetClientOdds_ZeroOdds_PublishesSourceOddsUntouched()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.05m, new List<SelectionMessage> { Selection(1, 0m) }, "TSO:0.10", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Equal(0m, Assert.Single(result).DecimalOdds);
        }

        // Three selections make the market non-binary, so every alternative format keeps the
        // decimal price.
        [Fact]
        public void GetClientOdds_MoreThanTwoSelections_KeepsDecimalOddsInEveryFormat()
        {
            var selections = new List<SelectionMessage> { Selection(1, 2.50m), Selection(2, 3.00m), Selection(3, 4.00m) };

            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 3, 0.10m, selections, "TSO:0.10", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.All(result, o =>
            {
                Assert.Equal(o.DecimalOdds, o.MalaysianOdds);
                Assert.Equal(o.DecimalOdds, o.HongkongOdds);
                Assert.Equal(o.DecimalOdds, o.IndonesianOdds);
            });
        }

        // Player-prop markets bypass the fair-odds maths: the client margin is taken straight off
        // the price. 2.50 - (2.50 * 0.10) = 2.25.
        [Fact]
        public void GetClientOdds_PlayerPropMarket_SubtractsTheMarginDirectly()
        {
            var playerProps = new List<MarketTemplatePlayerPropsResponseDTO>
            {
                new() { SportId = 1, MarketTemplates = MarketTemplateId }
            };

            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.05m, new List<SelectionMessage> { Selection(1, 2.50m) }, "TSO:0.10", "TSO", MarketTemplateId, playerProps);

            Assert.Equal(2.25m, Assert.Single(result).DecimalOdds);
        }

        // Selection templates 12 and 13 are excluded from the player-prop shortcut and fall
        // through to the normal fair-odds maths.
        [Theory]
        [InlineData("12")]
        [InlineData("13")]
        public void GetClientOdds_PlayerPropMarket_ExcludedSelectionTemplates_UseTheNormalMaths(string seTeId)
        {
            var playerProps = new List<MarketTemplatePlayerPropsResponseDTO>
            {
                new() { SportId = 1, MarketTemplates = MarketTemplateId }
            };

            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.10m, new List<SelectionMessage> { Selection(1, 2.50m, seTeId: seTeId) }, "TSO:0.10", "TSO", MarketTemplateId, playerProps);

            Assert.Equal(2.50m, Assert.Single(result).DecimalOdds);
        }

        // A market whose margin is high enough that the fair-odds divisor collapses cannot be
        // re-priced, so the fallback (source price plus two points) is published instead.
        [Fact]
        public void GetClientOdds_MarginTooHighToRePrice_FallsBackToTheSourcePricePlusMargin()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 1.50m, new List<SelectionMessage> { Selection(1, 2.00m) }, "TSO:0.10", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Equal(1.92m, Assert.Single(result).DecimalOdds);
        }

        // The client asks for less margin than the market carries, which would lengthen the
        // price. Clients never publish longer odds than the source, so the fallback applies.
        [Fact]
        public void GetClientOdds_RePricedLongerThanSource_FallsBackToTheSourcePricePlusMargin()
        {
            var result = InvokeStatic<List<ClientOdds>>("GetClientOdds",
                1, 2, 0.10m, new List<SelectionMessage> { Selection(1, 2.00m) }, "TSO:0.01", "TSO", MarketTemplateId, NoPlayerProps);

            Assert.Equal(1.92m, Assert.Single(result).DecimalOdds);
        }

        // ---- BuildSourceOddsFallback (GM-810) ----

        // The mappers look a price up by ClientCode and SelectionId, so the fallback must carry
        // both, plus the template id, for every selection it is given. Through the mappers a wrong
        // ClientCode or SelectionId is masked by the per-selection fallback, so it is pinned here.
        [Fact]
        public void BuildSourceOddsFallback_CarriesTheLookupKeysForEverySelection()
        {
            var selections = new List<SelectionMessage> { Selection(11, 2.50m, seTeId: "1"), Selection(12, 1.60m, isActive: false, seTeId: "2") };

            var result = InvokeStatic<List<ClientOdds>>("BuildSourceOddsFallback", selections, "TSO");

            Assert.Equal(2, result.Count);
            Assert.All(result, odds => Assert.Equal("TSO", odds.ClientCode));
            Assert.Equal(new long[] { 11, 12 }, result.Select(o => o.SelectionId).ToArray());
            Assert.Equal(new[] { "1", "2" }, result.Select(o => o.SelectionTemplateId).ToArray());
        }

        // Source odds pass through unchanged, inactive selections included: unlike GetClientOdds
        // there is no margin to apply, so IsAc makes no difference.
        [Fact]
        public void BuildSourceOddsFallback_PublishesTheSourcePriceInEveryFormat()
        {
            var selections = new List<SelectionMessage> { Selection(11, 2.50m), Selection(12, 1.60m, isActive: false) };

            var result = InvokeStatic<List<ClientOdds>>("BuildSourceOddsFallback", selections, "TSO");

            Assert.Equal(new[] { 2.50m, 1.60m }, result.Select(o => o.DecimalOdds).ToArray());
            Assert.Equal(new[] { -0.66m, 0.60m }, result.Select(o => o.MalaysianOdds).ToArray());
            Assert.Equal(new[] { 1.50m, 0.60m }, result.Select(o => o.HongkongOdds).ToArray());
            Assert.Equal(new[] { 1.50m, -1.66m }, result.Select(o => o.IndonesianOdds).ToArray());
        }

        [Fact]
        public void BuildSourceOddsFallback_ThreeSelections_IsNotBinary()
        {
            var selections = new List<SelectionMessage> { Selection(1, 2.50m), Selection(2, 3.00m), Selection(3, 4.00m) };

            var result = InvokeStatic<List<ClientOdds>>("BuildSourceOddsFallback", selections, "TSO");

            Assert.All(result, o =>
            {
                Assert.Equal(o.DecimalOdds, o.MalaysianOdds);
                Assert.Equal(o.DecimalOdds, o.HongkongOdds);
                Assert.Equal(o.DecimalOdds, o.IndonesianOdds);
            });
        }

        [Fact]
        public void BuildSourceOddsFallback_NoSelections_ReturnsNothing()
        {
            Assert.Empty(InvokeStatic<List<ClientOdds>>("BuildSourceOddsFallback", new List<SelectionMessage>(), "TSO"));
        }
    }
}
