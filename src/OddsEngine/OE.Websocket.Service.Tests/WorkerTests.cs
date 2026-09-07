using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
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

            // Long enough for discovery, client data load and at least one failed connect.
            await Task.Delay(TimeSpan.FromMilliseconds(700));

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
            await Task.Delay(TimeSpan.FromMilliseconds(500));
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

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (registry.Clients.Any(c => !c.IsReady) && DateTime.UtcNow < deadline)
                await Task.Delay(50);

            await worker.StopAsync(CancellationToken.None);

            Assert.All(registry.Clients, c => Assert.True(c.IsReady));
            Assert.InRange(repository.MaxInFlight, 1, Worker.LoadClientConcurrency);
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

        private sealed class FakeMessageBroker : IMessageBroker
        {
            public int ConnectCalls { get; private set; }

            public event EventHandler<BasicDeliverEventArgs>? MessageReceived;

            public void Connect() => ConnectCalls++;
            public void ConnectWithManualAck() => ConnectCalls++;
            public void ExternalConnect() { }
            public void ExternalConnect(MessageBrokerModel model) { }
            public void Acknowledge(BasicDeliverEventArgs e) { }
            public void NAcknowledge(BasicDeliverEventArgs e) { }
            public void ExternalPublish<T>(T payload, string messageType, string routingKey, string correlationId) { }
            public void ExternalPublish(string payload, string messageType, string routingKey, string correlationId) { }
            public void Publish<T>(T payload, string messageType, string routingKey, string correlationId) { }
            public void Publish(string payload, string messageType, string routingKey, string correlationId) { }
            public void CPublish<T>(T payload, string messageType, string routingKey, string correlationId) { }
            public void CPublish(string payload, string messageType, string routingKey, string correlationId) { }
            public IModel BindQueue(string queueName, Dictionary<string, object> options) => throw new NotSupportedException();
            public void Dispose() { }

            // Keeps the compiler quiet about the never-raised event.
            public void Raise(BasicDeliverEventArgs e) => MessageReceived?.Invoke(this, e);
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

            public IReadOnlyList<ClientContext> Clients => _clients;
            public ClientContext ResolveByRoutingKey(string routingKey) => _clients[0];
            public ClientContext ResolveByClientId(string clientId) => _clients[0];
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

            // Highest number of clients whose template load overlapped.
            public int MaxInFlight => Volatile.Read(ref _maxInFlight);

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
            public Task ProcessFixtureSortOrder(ClientContext client) => Task.CompletedTask;
            public Task ProcessBetBuilderMarketTemplates(ClientContext client) => Task.CompletedTask;
            public Task ProcessMarketTemplatePlayerProps(ClientContext client) => Task.CompletedTask;
        }
    }
}
