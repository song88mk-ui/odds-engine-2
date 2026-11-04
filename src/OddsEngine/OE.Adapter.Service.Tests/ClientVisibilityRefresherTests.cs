using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Adapter.Service.ClientVisibility;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Test;

namespace OE.Adapter.Service.Tests
{
    // Keeps what is already stored current: after a reload, each league whose clients changed or that Backoffice asked
    // about is sent, with its whole list, to the shard that does league-wide writes.
    public class ClientVisibilityRefresherTests
    {
        private static readonly LeagueKey League = new(1, 2, 3);

        private readonly FakeClientVisibility _visibility = new() { DefaultCodes = "G01|TSO" };
        private readonly FakeMessageBroker _broker = new();

        private ClientVisibilityRefresher Build(Dictionary<string, string?>? settings = null)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();

            return new ClientVisibilityRefresher(_visibility, _broker, configuration, NullLogger<ClientVisibilityRefresher>.Instance);
        }

        [Fact]
        public async Task RunOnce_SendsAChangedLeagueToTheGlobalShard_WithItsWholeList()
        {
            _visibility.Set(1, 2, 3, "G01");
            _visibility.Changed.Add(League);

            await Build().RunOnceAsync(CancellationToken.None);

            var sent = Assert.Single(_broker.CPublished);
            Assert.Equal(MessageType.ON_LEAGUE_CLIENTS_CHANGE, sent.MessageType);
            Assert.Equal("r.oe.adapter.onleagueclientschange.10", sent.RoutingKey);

            var message = Assert.IsType<OnLeagueClientsChangeMessage>(sent.Payload);
            Assert.Equal((1, 2, 3), (message.SportId, message.SegmentId, message.LeagueId));
            Assert.Equal("G01", message.ClientCodes);

            // not on the shared channel, which belongs to the consumer thread
            Assert.Empty(_broker.Published);
        }

        [Fact]
        public async Task RunOnce_SendsARequestedLeagueEvenWhenNothingChanged()
        {
            var refresher = Build();
            refresher.Request(1, 2, 3);

            await refresher.RunOnceAsync(CancellationToken.None);

            var message = Assert.IsType<OnLeagueClientsChangeMessage>(Assert.Single(_broker.CPublished).Payload);
            Assert.Equal("G01|TSO", message.ClientCodes);
            Assert.Equal(1, _visibility.RefreshCalls);
        }

        [Fact]
        public async Task RunOnce_ALeagueThatIsRequestedAndChanged_IsSentOnce()
        {
            _visibility.Changed.Add(League);
            var refresher = Build();
            refresher.Request(1, 2, 3);

            await refresher.RunOnceAsync(CancellationToken.None);

            Assert.Single(_broker.CPublished);
        }

        [Fact]
        public async Task RunOnce_NothingChangedAndNothingRequested_SendsNothing()
        {
            await Build().RunOnceAsync(CancellationToken.None);

            Assert.Empty(_broker.CPublished);
        }

        [Fact]
        public async Task RunOnce_ARequestIsServedOnlyOnce()
        {
            var refresher = Build();
            refresher.Request(1, 2, 3);

            await refresher.RunOnceAsync(CancellationToken.None);
            await refresher.RunOnceAsync(CancellationToken.None);

            Assert.Single(_broker.CPublished);
        }

        [Fact]
        public async Task Request_WhenTheListsAreNotConfigured_IsIgnored()
        {
            _visibility.IsConfigured = false;
            var refresher = Build();
            refresher.Request(1, 2, 3);

            await refresher.RunOnceAsync(CancellationToken.None);

            Assert.Empty(_broker.CPublished);
        }

        [Fact]
        public async Task RunOnce_WhenTheReloadFails_SendsNothing_AndKeepsTheRequestForTheNextRun()
        {
            var refresher = Build();
            refresher.Request(1, 2, 3);

            _visibility.ThrowOnRefresh = true;
            await refresher.RunOnceAsync(CancellationToken.None);
            Assert.Empty(_broker.CPublished);

            _visibility.ThrowOnRefresh = false;
            await refresher.RunOnceAsync(CancellationToken.None);
            Assert.Single(_broker.CPublished);
        }

        [Fact]
        public async Task RunOnce_WhenTheListsAreNotLoaded_SendsNothing()
        {
            _visibility.DefaultCodes = null;
            var refresher = Build();
            refresher.Request(1, 2, 3);

            await refresher.RunOnceAsync(CancellationToken.None);

            Assert.Empty(_broker.CPublished);
        }

        [Fact]
        public async Task RunOnce_WhenTheSendFails_TriesTheLeagueAgainAtTheNextRun()
        {
            _visibility.Changed.Add(League);
            var refresher = Build();

            _broker.ThrowOnCPublish = true;
            await refresher.RunOnceAsync(CancellationToken.None); // must not throw
            Assert.Empty(_broker.CPublished);

            _broker.ThrowOnCPublish = false;
            _visibility.Changed.Clear(); // the reload no longer reports it: only the kept request remains
            await refresher.RunOnceAsync(CancellationToken.None);
            Assert.Single(_broker.CPublished);
        }

        [Fact]
        public async Task Execute_WhenTheListsAreNotConfigured_EndsAtOnce()
        {
            _visibility.IsConfigured = false;
            var refresher = Build();

            await refresher.StartAsync(CancellationToken.None);
            await refresher.ExecuteTask!;

            Assert.Equal(0, _visibility.RefreshCalls);
        }

        [Fact]
        public async Task Execute_ARequestedLeagueIsSentWithoutWaitingForTheTimer()
        {
            var refresher = Build(new Dictionary<string, string?>
            {
                ["Custom:ClientVisibilityRefreshSeconds"] = "3600",
                ["Custom:ClientVisibilityDebounceMilliseconds"] = "0"
            });

            await refresher.StartAsync(CancellationToken.None);
            refresher.Request(1, 2, 3);

            await TestWait.UntilAsync(() => _broker.CPublished.Count == 1, TimeSpan.FromSeconds(5));
            await refresher.StopAsync(CancellationToken.None);

            Assert.Single(_broker.CPublished);
        }
    }
}
