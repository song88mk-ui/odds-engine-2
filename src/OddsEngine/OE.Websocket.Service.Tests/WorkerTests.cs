using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Messages;
using OE.WebSocket.Service.Repository;
using OE.WebSocket.Service.Telemetry;
using Microsoft.AspNetCore.SignalR.Client;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Websocket.Service.Tests
{
    // GM-791: the service went silent instead of restarting. The hub connect at
    // startup faulted ExecuteAsync, the host stopped, but the dispatch threads were
    // foreground threads blocked in Take(), so the process never exited and the
    // container stayed Running with nothing consuming. These tests pin both halves
    // of the fix (background dispatch threads; a hub that is down at startup is
    // retried instead of stopping the host) and the 500+ client startup behaviour
    // (bounded parallel loading; readiness only when templates really loaded).
    public class WorkerTests
    {
        // Nothing listens here, so every hub connect attempt fails fast with
        // "connection refused", which is exactly what the GM-791 log showed.
        private const string UnreachableHubUrl = "http://127.0.0.1:1/platform";

        [Fact]
        public void DispatchThreads_AreBackgroundThreads()
        {
            var thread = Worker.CreateDispatchThread(() => { }, "ws-test");

            Assert.True(thread.IsBackground);
            Assert.Equal("ws-test", thread.Name);
        }

        [Fact]
        public async Task HubUnreachableAtStartup_KeepsRetrying_AndStopsCleanly()
        {
            var broker = new FakeMessageBroker();
            var registry = new FakeClientRegistry();
            var worker = BuildWorker(broker, registry: registry);

            await worker.StartAsync(CancellationToken.None);

            // Wait for the actual signal (client data load completing) instead of a
            // fixed delay, which is a flake risk if CI load shifts discovery+load timing.
            await WaitUntilAsync(() => registry.Clients.All(c => c.IsReady), TimeSpan.FromSeconds(5));

            // Still retrying: ExecuteAsync has not faulted (which would stop the host)
            // and RabbitMQ consumption has not started before the hub is reachable.
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.Equal(0, broker.ConnectCalls);
            Assert.All(registry.Clients, c => Assert.True(c.IsReady));

            await worker.StopAsync(CancellationToken.None);

            Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
            Assert.Equal(0, broker.ConnectCalls);
        }

        [Fact]
        public async Task TemplateLoadFailure_LeavesClientOutOfTheFanOut()
        {
            var repository = new FakeSportsbookRepository { TemplatesLoaded = false };
            var registry = new FakeClientRegistry();
            var worker = BuildWorker(new FakeMessageBroker(), repository, registry);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => repository.LoadAttempts >= registry.Clients.Count, TimeSpan.FromSeconds(5));
            await worker.StopAsync(CancellationToken.None);

            Assert.All(registry.Clients, c => Assert.False(c.IsReady));
        }
        [Fact]
        public async Task ClientDataLoad_IsBounded_AndMarksEveryClientReady()
        {
            var repository = new FakeSportsbookRepository { LoadDelay = TimeSpan.FromMilliseconds(5) };
            var registry = new FakeClientRegistry(clientCount: 120);
            var worker = BuildWorker(new FakeMessageBroker(), repository, registry);

            await worker.StartAsync(CancellationToken.None);

            await WaitUntilAsync(() => registry.Clients.All(c => c.IsReady), TimeSpan.FromSeconds(10));

            await worker.StopAsync(CancellationToken.None);

            Assert.All(registry.Clients, c => Assert.True(c.IsReady));
            Assert.InRange(repository.MaxInFlight, 1, Worker.LoadClientConcurrency);
        }

        // GM-851: Back Office saves a client's Oddstable league order and CacheSync relays
        // OnOddstableLeagueSortOrderChange. The order stamped on pushed fixtures (SoOr) is loaded once
        // at startup, so without this the websocket service kept the old order until it restarted.
        [Fact]
        public async Task OddstableLeagueSortOrderChange_ReloadsTheFixtureSortOrderOfTheNamedClient()
        {
            var repository = new FakeSportsbookRepository();
            var registry = new FakeClientRegistry();
            var worker = BuildWorker(new FakeMessageBroker(), repository, registry);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => registry.Clients.All(c => c.IsReady), TimeSpan.FromSeconds(5));
            var loadsAtStartup = repository.FixtureSortOrderLoads.Count;

            Deliver(worker, "OnOddstableLeagueSortOrderChange", "r.oe.sb.engine.onoddstableleaguesortorderchange.c000");

            await WaitUntilAsync(() => repository.FixtureSortOrderLoads.Count > loadsAtStartup, TimeSpan.FromSeconds(5));
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(loadsAtStartup + 1, repository.FixtureSortOrderLoads.Count);
            Assert.Equal("c000", repository.FixtureSortOrderLoads.Last());
        }

        [Fact]
        public async Task OddstableLeagueSortOrderChange_ForAClientThisInstanceDoesNotServe_IsSkipped()
        {
            var repository = new FakeSportsbookRepository();
            var registry = new FakeClientRegistry();
            var worker = BuildWorker(new FakeMessageBroker(), repository, registry);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => registry.Clients.All(c => c.IsReady), TimeSpan.FromSeconds(5));
            var loadsAtStartup = repository.FixtureSortOrderLoads.Count;

            registry.ResolvesClients = false;
            Deliver(worker, "OnOddstableLeagueSortOrderChange", "r.oe.sb.engine.onoddstableleaguesortorderchange.zzz");
            await Task.Delay(300);
            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(loadsAtStartup, repository.FixtureSortOrderLoads.Count);
        }

        // Feeds one message straight into the consumer callback; the broker connection only starts once
        // the hub is reachable, which these tests deliberately never allow.
        private static void Deliver(Worker worker, string messageType, string routingKey)
        {
            var args = new BasicDeliverEventArgs(
                "consumer", 1, false, "e.central", routingKey,
                new RabbitMQ.Client.Framing.BasicProperties { Type = messageType },
                System.Text.Encoding.UTF8.GetBytes("true"));

            typeof(Worker)
                .GetMethod("MessageReceivedEvent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(worker, new object?[] { null, args });
        }

        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(20);
        }

        private static Worker BuildWorker(
            IMessageBroker broker,
            FakeSportsbookRepository? repository = null,
            FakeClientRegistry? registry = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Custom:UseMessagePack", "false" },
                    { "Custom:WebSocketAPI", UnreachableHubUrl },
                    { "Custom:PlatformAPI", "http://localhost:3000" }
                })
                .Build();

            return new Worker(
                NullLogger<Worker>.Instance,
                configuration,
                broker,
                repository ?? new FakeSportsbookRepository(),
                registry ?? new FakeClientRegistry(),
                new FakeClientPresence(),
                new Instrumentation());
        }

        private sealed class FakeClientRegistry : IClientRegistry
        {
            private readonly List<ClientContext> _clients;

            public FakeClientRegistry(int clientCount = 1)
            {
                _clients = Enumerable.Range(0, clientCount)
                    .Select(i => new ClientContext
                    {
                        ClientId = $"00000000-0000-0000-0000-{i:D12}",
                        ClientCode = $"c{i:D3}",
                        HttpClient = new HttpClient()
                    })
                    .ToList();
            }

            // false makes the registry answer "this instance does not serve that client".
            public bool ResolvesClients { get; set; } = true;

            public IReadOnlyList<ClientContext> Clients => _clients;
            public ClientContext ResolveByRoutingKey(string routingKey) => ResolvesClients ? _clients[0] : null!;
            public ClientContext ResolveByClientId(string clientId) => ResolvesClients ? _clients[0] : null!;
            public Task<IReadOnlyList<ClientContext>> RefreshAsync() => Task.FromResult<IReadOnlyList<ClientContext>>(_clients);
        }

        private sealed class FakeClientPresence : IClientPresence
        {
            public bool IsActive(string clientCode) => true;
            public void Update(IReadOnlyCollection<string> activeClientCodes) { }
            public void MarkUnavailable() { }
        }

        private sealed class FakeSportsbookRepository : ISportsbookRepository
        {
            public bool TemplatesLoaded { get; set; } = true;
            public TimeSpan LoadDelay { get; set; } = TimeSpan.Zero;

            private int _inFlight;
            private int _maxInFlight;
            private int _loadAttempts;

            // Highest number of clients whose template load overlapped.
            public int MaxInFlight => Volatile.Read(ref _maxInFlight);

            // How many clients have had a template load attempted so far.
            public int LoadAttempts => Volatile.Read(ref _loadAttempts);

            public async Task<bool> ProcessMainMarketTemplates(ClientContext client)
            {
                var now = Interlocked.Increment(ref _inFlight);

                int seen;
                while ((seen = Volatile.Read(ref _maxInFlight)) < now
                       && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
                {
                }

                try
                {
                    if (LoadDelay > TimeSpan.Zero)
                        await Task.Delay(LoadDelay);

                    return TemplatesLoaded;
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                    Interlocked.Increment(ref _loadAttempts);
                }
            }

            public Task ProcessOddsChangeForMainMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessOddsChangeForAllMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessBetStopGlobal(ClientContext client, OnBetStopGlobalMessage onBetStopGlobal, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessBetStopFixture(ClientContext client, OnBetStopFixtureMessage onBetStopFixtureMessage, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessBetStatus(ClientContext client, IncomingBetStatus onBetStatus, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessCashOutChange(ClientContext client, IncomingCashOutChange onCashOutChange, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessWalletChange(ClientContext client, IncomingWalletChange onWalletChange, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessTopFixtures(ClientContext client, OnMarketChangeMessage message) => Task.CompletedTask;
            public Task ProcessTopLeagues(ClientContext client, OnMarketChangeMessage message) => Task.CompletedTask;
            public Task ProcessMarketCount(ClientContext client, OnMarketChangeMessage message) => Task.CompletedTask;
            public Task ProcessFavorites(ClientContext client, IncomingVisitFavorites message) => Task.CompletedTask;
            public Task ProcessRemoveFavorites(ClientContext client, IncomingVisitFavorites message) => Task.CompletedTask;
            public Task ProcessBoostedFixtures(ClientContext client, IncomingVisitBoostedFixtures message) => Task.CompletedTask;
            public Task ProcessPayableChange(ClientContext client, IncomingPayableChange message, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessWebPropsChange(ClientContext client, HubConnection hubConnection) => Task.CompletedTask;
            public Task ProcessMarketSortOrder(ClientContext client) => Task.CompletedTask;
            // Client codes in the order ProcessFixtureSortOrder was called for them.
            public System.Collections.Concurrent.ConcurrentQueue<string> FixtureSortOrderLoads { get; } = new();

            public Task ProcessFixtureSortOrder(ClientContext client)
            {
                FixtureSortOrderLoads.Enqueue(client.ClientCode);
                return Task.CompletedTask;
            }
            public Task ProcessBetBuilderMarketTemplates(ClientContext client) => Task.CompletedTask;
            public Task ProcessMarketTemplatePlayerProps(ClientContext client) => Task.CompletedTask;
        }
    }
}
