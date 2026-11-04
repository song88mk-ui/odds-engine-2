using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Adapter.Service.ClientVisibility;
using OE.Adapter.Service.Repositories;
using OE.Adapter.Service.Telemetry;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Test;
using OE.Shared.Utilities;

namespace OE.Adapter.Service.Tests
{
    // The provider sends ClCo empty and nothing downstream fills it, so the adapter stamps the clients allowed to see
    // the league onto the fixture and every market (BO-145). Before the lists are loaded the message is left as it came.
    public class ClientCodesStampingTests
    {
        private readonly FakeMessageBroker _broker = new();
        private readonly FakeClientVisibility _visibility = new();
        private readonly FakeLeagueRefreshRequests _refreshRequests = new();
        private readonly TradingRepository _repository;

        public ClientCodesStampingTests()
        {
            var cache = new TranslationCache(
                NullLogger<TranslationCache>.Instance,
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

            _repository = new TradingRepository(new Helper(), NullLogger<TradingRepository>.Instance, cache, _visibility, _refreshRequests, new Instrumentation());
        }

        private static OnFixtureChangeMessage Fixture(int leagueId = 3) => new()
        {
            PrFiId = "1234567890",
            SpId = 1,
            SegId = 2,
            LeId = leagueId,
            ClCo = string.Empty
        };

        private static OnMarketChangeMessage Markets(int leagueId = 3)
        {
            var message = new OnMarketChangeMessage { PrFiId = "1234567890", SpId = 1, SegId = 2, LeId = leagueId, ClCo = string.Empty };

            foreach (var marketId in new[] { "M1", "M2" })
            {
                message.Ma.Add(new MarketMessage
                {
                    PrFiId = "1234567890",
                    PrMaId = marketId,
                    MaTeId = "16",
                    Se = new List<SelectionMessage> { new() { PrFiId = "1234567890", PrMaId = marketId, PrSeId = "S1", MaTeId = "16", SeTeId = "1" } }
                });
            }

            return message;
        }

        [Fact]
        public void FixtureChange_IsStampedWithTheClientsOfItsLeague_AndPublishedWithThem()
        {
            _visibility.Set(1, 2, 3, "G01|TSO");
            var message = Fixture();

            _repository.ProcessOnFixtureChangeMessage(_broker, message, "cid");

            Assert.Equal("G01|TSO", message.ClCo);
            Assert.Same(message, Assert.Single(_broker.Published).Payload);
        }

        [Fact]
        public void MarketChange_StampsTheFixtureAndEveryMarket()
        {
            _visibility.Set(1, 2, 3, "G01|TSO");
            var message = Markets();

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            Assert.Equal("G01|TSO", message.ClCo);
            Assert.All(message.Ma, market => Assert.Equal("G01|TSO", market.ClCo));
        }

        [Fact]
        public void ARestrictedLeague_GetsItsShorterList_AnotherLeagueTheDefault()
        {
            _visibility.DefaultCodes = "G01|S88|TSO";
            _visibility.Set(1, 2, 3, "G01|S88");

            var restricted = Fixture(leagueId: 3);
            var open = Fixture(leagueId: 4);
            _repository.ProcessOnFixtureChangeMessage(_broker, restricted, "cid");
            _repository.ProcessOnFixtureChangeMessage(_broker, open, "cid");

            Assert.Equal("G01|S88", restricted.ClCo);
            Assert.Equal("G01|S88|TSO", open.ClCo);
        }

        [Fact]
        public void ALeagueNobodyMaySee_IsStampedEmpty_NotLeftWithWhatTheProviderSent()
        {
            _visibility.Set(1, 2, 3, string.Empty);
            var message = Fixture();
            message.ClCo = "OLD";

            _repository.ProcessOnFixtureChangeMessage(_broker, message, "cid");

            Assert.Equal(string.Empty, message.ClCo);
        }

        [Fact]
        public void BeforeTheListsAreLoaded_TheMessageIsLeftAsItCame()
        {
            _visibility.DefaultCodes = null;
            var fixture = Fixture();
            fixture.ClCo = "FROM-PROVIDER";
            var markets = Markets();
            markets.Ma[0].ClCo = "FROM-PROVIDER";

            _repository.ProcessOnFixtureChangeMessage(_broker, fixture, "cid");
            _repository.ProcessOnMarketChangeMessage(_broker, markets, "cid");

            Assert.Equal("FROM-PROVIDER", fixture.ClCo);
            Assert.Equal("FROM-PROVIDER", markets.Ma[0].ClCo);
            Assert.Equal(2, _broker.Published.Count); // still published
        }

        [Fact]
        public void MarketChange_ANullMarketEntry_DoesNotStopTheStamp()
        {
            _visibility.DefaultCodes = "G01";
            var message = Markets();
            message.Ma.Insert(0, null!);

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            Assert.Equal("G01", message.ClCo);
            Assert.Equal("G01", message.Ma[1].ClCo);
        }

        [Fact]
        public void LeagueConfig_AsksForTheLeaguesClientsToBeReloadedAndSent()
        {
            _repository.ProcessOnLeagueConfigMessage(_broker, new OnLeagueConfigMessage { ClientCode = "G01", SportId = 1, SegmentId = 2, LeagueId = 3 }, "cid");

            Assert.Equal(new[] { new LeagueKey(1, 2, 3) }, _refreshRequests.Requests);
            Assert.Empty(_broker.Published); // the adapter itself publishes nothing here: the refresher does, on its own channel
        }

        [Fact]
        public void LeagueConfig_ANullMessage_IsIgnored()
        {
            _repository.ProcessOnLeagueConfigMessage(_broker, null!, "cid");

            Assert.Empty(_refreshRequests.Requests);
        }
    }
}
