using System.Text.Json;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingAdapterMessages.Base;

namespace OE.Shared.Test.Messages
{
    // The abbreviated property names ARE the wire contract (no serialization attributes) — these
    // tests pin OnMarketCancellationMessage for the cross-repo consumers (bet-genius, the UOF
    // egress, the sportsbook writer).
    //
    // They also guard a specific regression (DEVOPS-358): TiSe/TiRe used to be REDECLARED on
    // OnMarketCancellationMessage even though OutgoingAdapterMessageBase already declares them
    // with the same name and type. That shadowing compiled (CS0108) and serialized fine, because
    // every read and every serialization call binds T to the concrete type — so no wire value was
    // ever wrong and no shipped code path was broken.
    //
    // What it did leave behind is a trap: the base-declared slots stayed permanently 0, so the
    // FIRST piece of code to read these timestamps through a base-typed view would have silently
    // got zeros. Such a view already exists — TradingRepository.PublishMessage<T>(...) is
    // constrained `where T : OutgoingAdapterMessageBase`, and inside it every base member binds to
    // the base slot — it just happens to read only PrFiId today.
    //
    // BaseTypedTimestampsMatchDerived below is the test that fails if the shadowing is ever
    // reintroduced.
    public class MarketCancellationWireContractTests
    {
        private const long TimestampSent = 1717171717171;
        private const long TimestampReceived = 1717171717999;

        private static OnMarketCancellationMessage NewMessage() => new()
        {
            ChTyId = 7,
            PrFiId = "1234567890",
            FiId = 42,
            FiSp = "Football",
            SpId = 9,
            TiSe = TimestampSent,
            TiRe = TimestampReceived,
            Raw = "{\"raw\":true}",
            Ma = new List<MarketBetCancellationMessage>
            {
                new MarketBetCancellationMessage
                {
                    MaId = "M1", MaTeId = "16", MaSp = "1", VoRe = "MarketCancelled", StTi = 100, EnTi = 200
                }
            }
        };

        // Mirrors TradingRepository.PublishMessage<T>(...): a generic constrained to the base, so
        // every member access inside binds to the BASE-declared slot, not the derived one.
        private static (long TiSe, long TiRe) ReadTimestampsAsBase<T>(T payload)
            where T : OutgoingAdapterMessageBase
            => (payload.TiSe, payload.TiRe);

        [Fact]
        public void OnMarketCancellationMessage_WireCarriesExactNames()
        {
            var json = JsonSerializer.Serialize(NewMessage());

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            Assert.Equal(7, root.GetProperty("ChTyId").GetInt32());
            Assert.Equal("1234567890", root.GetProperty("PrFiId").GetString());
            Assert.Equal(42, root.GetProperty("FiId").GetInt64());
            Assert.Equal("Football", root.GetProperty("FiSp").GetString());
            Assert.Equal(9, root.GetProperty("SpId").GetInt32());
            Assert.Equal(TimestampSent, root.GetProperty("TiSe").GetInt64());
            Assert.Equal(TimestampReceived, root.GetProperty("TiRe").GetInt64());
            Assert.Equal("{\"raw\":true}", root.GetProperty("Raw").GetString());
            Assert.Equal("7:1234567890", root.GetProperty("Id").GetString());

            var market = root.GetProperty("Ma")[0];
            Assert.Equal("M1", market.GetProperty("MaId").GetString());
            Assert.Equal("16", market.GetProperty("MaTeId").GetString());
            Assert.Equal("1", market.GetProperty("MaSp").GetString());
            Assert.Equal("MarketCancelled", market.GetProperty("VoRe").GetString());
            Assert.Equal(100, market.GetProperty("StTi").GetInt64());
            Assert.Equal(200, market.GetProperty("EnTi").GetInt64());
        }

        // The shadowing regression guard. Before DEVOPS-358 the base-typed reads returned 0 here
        // while the derived reads returned the real timestamps.
        [Fact]
        public void BaseTypedTimestampsMatchDerived()
        {
            var message = NewMessage();

            var viaGeneric = ReadTimestampsAsBase(message);
            var viaCast = (OutgoingAdapterMessageBase)message;

            Assert.Equal(TimestampSent, viaGeneric.TiSe);
            Assert.Equal(TimestampReceived, viaGeneric.TiRe);
            Assert.Equal(TimestampSent, viaCast.TiSe);
            Assert.Equal(TimestampReceived, viaCast.TiRe);
        }

        // How the Adapter and UOF workers actually receive this message: deserialized from an
        // inbound payload, then read back — including through the base-typed publish path.
        [Fact]
        public void InboundPayload_RoundTripsIntoBothDerivedAndBaseViews()
        {
            var inbound = JsonSerializer.Serialize(NewMessage());

            var roundTripped = JsonSerializer.Deserialize<OnMarketCancellationMessage>(inbound);

            Assert.NotNull(roundTripped);
            Assert.Equal(TimestampSent, roundTripped!.TiSe);
            Assert.Equal(TimestampReceived, roundTripped.TiRe);

            var asBase = ReadTimestampsAsBase(roundTripped);
            Assert.Equal(TimestampSent, asBase.TiSe);
            Assert.Equal(TimestampReceived, asBase.TiRe);

            Assert.Equal("M1", roundTripped.Ma.Single().MaId);
        }
    }
}
