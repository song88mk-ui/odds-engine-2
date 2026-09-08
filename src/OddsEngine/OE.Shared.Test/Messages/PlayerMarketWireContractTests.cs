using System.Text.Json;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Shared.Test.Messages
{
    // Pins the 2026-07 player-markets contract additions: the fixture roster
    // (OnFixtureChangeMessage.Pl, entries {PlId, PlNa}) and the market-level competitor id
    // (MarketMessage.CompId). The names are shared verbatim with bet-genius's
    // OnInternalFixtureChangeMessage/InternalMarketMessage — a rename breaks them silently. The
    // roster rides only the producer -> adapter -> sportsbook leg (seeding + persistence); the
    // player label reaches clients through MaNa/MaTeTr (see PlayerMarketNameComposer), so
    // OnInternalOddsChangeMessage carries no roster.
    public class PlayerMarketWireContractTests
    {
        [Fact]
        public void FixtureRosterAndMarketCompId_WireCarryExactNames()
        {
            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                Pl = new List<PlayerMessage>
                {
                    new PlayerMessage { PlId = 150762, PlNa = "Sharma, Rohit" }
                },
                Ma = new List<MarketMessage>
                {
                    new MarketMessage { ChTyId = 0, PrFiId = "1234567890", PrMaId = "M1", MaTeId = "15858", CompId = 150762 }
                }
            };

            var json = JsonSerializer.Serialize(message);

            using var document = JsonDocument.Parse(json);
            var player = document.RootElement.GetProperty("Pl")[0];
            Assert.Equal(150762, player.GetProperty("PlId").GetInt32());
            Assert.Equal("Sharma, Rohit", player.GetProperty("PlNa").GetString());
            Assert.Equal(150762, document.RootElement.GetProperty("Ma")[0].GetProperty("CompId").GetInt32());
        }

        // A payload from the bet-genius producer must deserialize cleanly and keep the
        // roster/CompId join intact.
        [Fact]
        public void BetGeniusShapedPayload_DeserializesRosterAndCompId()
        {
            const string json = "{\"PrFiId\":\"14041033\",\"ChTyId\":6,"
                + "\"Pl\":[{\"PlId\":150762,\"PlNa\":\"Sharma, Rohit\"},{\"PlId\":150763,\"PlNa\":\"Kohli, Virat\"}],"
                + "\"Ma\":[{\"PrMaId\":\"M1\",\"MaTeId\":\"15858\",\"CompId\":150762}]}";

            var message = JsonSerializer.Deserialize<OnMarketChangeMessage>(json);

            Assert.NotNull(message);
            Assert.Equal(2, message!.Pl.Count);
            Assert.Equal("Sharma, Rohit", message.Pl[0].PlNa);
            Assert.Equal(150762, message.Ma.Single().CompId);
            Assert.Equal(message.Ma.Single().CompId, message.Pl[0].PlId);
        }

        // OE.Websocket.Service deserializes ON_ODDS_CHANGE (produced as OnInternalOddsChangeMessage)
        // as OnMarketChangeMessage, so the market-level CompId name must line up across the hop
        // (it rides the translation-only push for recompose routing; downstream ignores it).
        [Fact]
        public void OnInternalOddsChangeMessage_MarketCompIdSurvivesTheWebsocketHop()
        {
            var internalMessage = new OnInternalOddsChangeMessage
            {
                PrFiId = "14041033",
                Ma = new List<InternalMarketMessage>
                {
                    new InternalMarketMessage { PrFiId = "14041033", PrMaId = "M1", MaTeId = "15858", CompId = 150762 }
                }
            };

            var asWebsocketSees = JsonSerializer.Deserialize<OnMarketChangeMessage>(JsonSerializer.Serialize(internalMessage));

            Assert.NotNull(asWebsocketSees);
            Assert.Equal(150762, asWebsocketSees!.Ma.Single().CompId);
        }

        [Fact]
        public void RosterAndCompId_DefaultToNull_SoPayloadsWithoutThemDontWipeState()
        {
            Assert.Null(new OnFixtureChangeMessage { ChTyId = 0, PrFiId = "1" }.Pl);
            Assert.Null(new MarketMessage { ChTyId = 0, PrFiId = "1", PrMaId = "M1" }.CompId);
            Assert.Null(new InternalMarketMessage { PrFiId = "1", PrMaId = "M1" }.CompId);
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void FixtureRosterAndMarketCompId_WireCarryExactNames_MutationCheck()
        {
            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                Pl = new List<PlayerMessage>
                {
                    new PlayerMessage { PlId = 150762, PlNa = "Sharma, Rohit" }
                },
                Ma = new List<MarketMessage>
                {
                    new MarketMessage { ChTyId = 0, PrFiId = "1234567890", PrMaId = "M1", MaTeId = "15858", CompId = 150762 }
                }
            };

            var json = JsonSerializer.Serialize(message);

            using var document = JsonDocument.Parse(json);
            var player = document.RootElement.GetProperty("Pl")[0];

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(999999, player.GetProperty("PlId").GetInt32()));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("Wrong Name", player.GetProperty("PlNa").GetString()));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(999999, document.RootElement.GetProperty("Ma")[0].GetProperty("CompId").GetInt32()));
        }

        [Fact]
        public void BetGeniusShapedPayload_DeserializesRosterAndCompId_MutationCheck()
        {
            const string json = "{\"PrFiId\":\"14041033\",\"ChTyId\":6,"
                + "\"Pl\":[{\"PlId\":150762,\"PlNa\":\"Sharma, Rohit\"},{\"PlId\":150763,\"PlNa\":\"Kohli, Virat\"}],"
                + "\"Ma\":[{\"PrMaId\":\"M1\",\"MaTeId\":\"15858\",\"CompId\":150762}]}";

            var message = JsonSerializer.Deserialize<OnMarketChangeMessage>(json);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Null(message));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, message!.Pl.Count));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("Wrong Name", message!.Pl[0].PlNa));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(999999, message!.Ma.Single().CompId));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEqual(message!.Ma.Single().CompId, message.Pl[0].PlId));
        }

        [Fact]
        public void OnInternalOddsChangeMessage_MarketCompIdSurvivesTheWebsocketHop_MutationCheck()
        {
            var internalMessage = new OnInternalOddsChangeMessage
            {
                PrFiId = "14041033",
                Ma = new List<InternalMarketMessage>
                {
                    new InternalMarketMessage { PrFiId = "14041033", PrMaId = "M1", MaTeId = "15858", CompId = 150762 }
                }
            };

            var asWebsocketSees = JsonSerializer.Deserialize<OnMarketChangeMessage>(JsonSerializer.Serialize(internalMessage));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Null(asWebsocketSees));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(999999, asWebsocketSees!.Ma.Single().CompId));
        }

        [Fact]
        public void RosterAndCompId_DefaultToNull_SoPayloadsWithoutThemDontWipeState_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(new OnFixtureChangeMessage { ChTyId = 0, PrFiId = "1" }.Pl));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(new MarketMessage { ChTyId = 0, PrFiId = "1", PrMaId = "M1" }.CompId));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotNull(new InternalMarketMessage { PrFiId = "1", PrMaId = "M1" }.CompId));
        }

        #endregion
    }
}
