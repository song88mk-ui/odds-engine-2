using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Adapter.Service.Repositories;
using OE.Adapter.Service.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;
using System.Text;
using System.Text.Json;

namespace OE.Adapter.Service.Tests
{
    // Worker dispatches on BasicProperties.Type to the matching ITradingRepository method; a
    // failure in any handler must be caught, published to r.oe.adapter.error, and never crash the
    // consumer loop. Translation preload is skipped when no connection string is configured
    // (deployments ship it empty until argocd injects the real one) and otherwise gates consuming.
    public class WorkerTests
    {
        private static BasicDeliverEventArgs Delivery(string messageType, object payload, string correlationId = "cid", string routingKey = "r.oe.adapter.test")
        {
            var properties = new BasicProperties
            {
                Type = messageType,
                CorrelationId = correlationId
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            return new BasicDeliverEventArgs("consumer", 1, false, "e.central", routingKey, properties, body);
        }

        private static Worker BuildWorker(
            FakeMessageBroker broker,
            FakeTradingRepository? tradingRepository = null,
            FakeTranslationCache? translationCache = null,
            string? translationConnectionString = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Translation"] = translationConnectionString
                })
                .Build();

            return new Worker(
                NullLogger<Worker>.Instance,
                broker,
                tradingRepository ?? new FakeTradingRepository(),
                translationCache ?? new FakeTranslationCache(),
                configuration,
                new Instrumentation());
        }

        // BackgroundService schedules ExecuteAsync's work rather than running its synchronous
        // prefix inline — awaiting StartAsync alone does NOT guarantee Connect()/Preload() have run
        // yet. The base class exposes ExecuteTask (public since .NET 8) precisely for this; await
        // it whenever a test needs ExecuteAsync to have actually finished.
        private static async Task<Worker> StartAndWaitAsync(Worker worker)
        {
            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!;
            return worker;
        }

        [Fact]
        public async Task ExecuteAsync_NoTranslationConnectionString_SkipsPreloadAndConnects()
        {
            var broker = new FakeMessageBroker();
            var translationCache = new FakeTranslationCache();
            var worker = BuildWorker(broker, translationCache: translationCache, translationConnectionString: null);

            await StartAndWaitAsync(worker);

            Assert.Equal(0, translationCache.PreloadCalls);
            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_TranslationConnectionStringConfigured_PreloadsBeforeConnecting()
        {
            var broker = new FakeMessageBroker();
            var translationCache = new FakeTranslationCache();
            var worker = BuildWorker(broker, translationCache: translationCache, translationConnectionString: "Server=.;Database=Translation;");

            await StartAndWaitAsync(worker);

            Assert.Equal(1, translationCache.PreloadCalls);
            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_PreloadFails_KeepsRetryingUntilStopped_WithoutEverConnecting()
        {
            var broker = new FakeMessageBroker();
            var translationCache = new FakeTranslationCache { ThrowOnPreload = true };
            var worker = BuildWorker(broker, translationCache: translationCache, translationConnectionString: "Server=.;Database=Translation;");

            await worker.StartAsync(CancellationToken.None);
            await TestWait.UntilAsync(() => translationCache.PreloadCalls > 0, TimeSpan.FromSeconds(5)); // let the first failed attempt enter its retry delay

            // Still retrying (real 10s delay pending) — never reached Connect().
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.Equal(1, translationCache.PreloadCalls);
            Assert.Equal(0, broker.ConnectCalls);

            await worker.StopAsync(CancellationToken.None); // cancels the pending retry delay

            Assert.True(worker.ExecuteTask.IsCompleted);
            Assert.Equal(0, broker.ConnectCalls); // still never got past the failed preload
        }

        [Fact]
        public async Task StopAsync_DisposesBroker()
        {
            var broker = new FakeMessageBroker();
            var worker = await StartAndWaitAsync(BuildWorker(broker));

            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(1, broker.DisposeCalls);
        }

        [Fact]
        public async Task StopAsync_BrokerDisposeThrows_IsCaughtAndDoesNotPropagate()
        {
            var broker = new FakeMessageBroker { ThrowOnDispose = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker));

            await worker.StopAsync(CancellationToken.None); // must not throw
        }

        [Theory]
        [InlineData(MessageType.ON_MARKET_SETTLEMENT)]
        [InlineData(MessageType.ON_BET_SETTLEMENT)]
        [InlineData(MessageType.ON_BET_STOP_GLOBAL)]
        [InlineData(MessageType.ON_MARKET_CANCEL)]
        [InlineData(MessageType.ON_BET_STOP_FIXTURE)]
        [InlineData(MessageType.ON_TRANSLATION_CHANGE)]
        public async Task MessageReceived_DispatchesEachKnownTypeToTheMatchingHandler(string messageType)
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeTradingRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(messageType, new { }));

            Assert.Equal(new[] { messageType }, repository.Invocations);
        }

        [Fact]
        public async Task MessageReceived_OnFixtureChange_DeserializesAndDispatches()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeTradingRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_FIXTURE_CHANGE, new OnFixtureChangeMessage { PrFiId = "F1" }, correlationId: "cid-1"));

            var call = Assert.Single(repository.FixtureChangeCalls);
            Assert.Equal("F1", call.Message.PrFiId);
            Assert.Equal("cid-1", call.CorrelationId);
            Assert.Same(broker, call.Broker);
        }

        [Fact]
        public async Task MessageReceived_OnMarketChange_DeserializesAndDispatches()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeTradingRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            Assert.Single(repository.MarketChangeCalls);
        }

        [Fact]
        public async Task MessageReceived_UnknownMessageType_IsIgnoredWithoutError()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeTradingRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("SomeUnhandledType", new { }));

            Assert.Empty(repository.Invocations);
            Assert.Empty(broker.Published);
        }

        [Fact]
        public async Task MessageReceived_EmptyPayload_SkipsProcessing()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeTradingRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            var properties = new BasicProperties { Type = MessageType.ON_FIXTURE_CHANGE, CorrelationId = "cid" };
            broker.Raise(new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.adapter.test", properties, Array.Empty<byte>()));

            Assert.Empty(repository.Invocations);
        }

        [Fact]
        public async Task MessageReceived_HandlerThrows_PublishesToErrorRoutingKeyAndDoesNotRethrow()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeTradingRepository { ThrowOnFixtureChange = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_FIXTURE_CHANGE, new OnFixtureChangeMessage { PrFiId = "F1" }, correlationId: "cid-err"));

            var published = Assert.Single(broker.Published);
            Assert.Equal("r.oe.adapter.error", published.RoutingKey);
            Assert.Equal(MessageType.ON_FIXTURE_CHANGE, published.MessageType);
            Assert.Equal("cid-err", published.CorrelationId);
        }
        private sealed class FakeTranslationCache : ITranslationCache
        {
            public int PreloadCalls { get; private set; }
            public bool ThrowOnPreload { get; set; }

            public void Preload()
            {
                PreloadCalls++;
                if (ThrowOnPreload)
                    throw new InvalidOperationException("preload failed");
            }

            public List<TranslationMessage> GetTranslations(string stringId, string currentText) => null!;
            public void Apply(List<TranslationChangeItem> items) { }
        }

        private sealed class FakeTradingRepository : ITradingRepository
        {
            public bool ThrowOnFixtureChange { get; set; }

            public List<string> Invocations { get; } = new();
            public List<(IMessageBroker Broker, OnFixtureChangeMessage Message, string CorrelationId)> FixtureChangeCalls { get; } = new();
            public List<(IMessageBroker Broker, OnMarketChangeMessage Message, string CorrelationId)> MarketChangeCalls { get; } = new();

            public void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
            {
                Invocations.Add(MessageType.ON_FIXTURE_CHANGE);
                FixtureChangeCalls.Add((messageBroker, message, correlationId));
                if (ThrowOnFixtureChange)
                    throw new InvalidOperationException("handler failed");
            }

            public void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
            {
                Invocations.Add(MessageType.ON_MARKET_CHANGE);
                MarketChangeCalls.Add((messageBroker, message, correlationId));
            }

            public void ProcessOnMarketSettlementMessage(IMessageBroker messageBroker, OnMarketSettlementMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_MARKET_SETTLEMENT);

            public void ProcessOnBetSettlementMessage(IMessageBroker messageBroker, OnBetSettlementMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_BET_SETTLEMENT);

            public void ProcessOnBetStopGlobalMessage(IMessageBroker messageBroker, OnBetStopGlobalMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_BET_STOP_GLOBAL);

            public void ProcessMarketCancellation(IMessageBroker messageBroker, OnMarketCancellationMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_MARKET_CANCEL);

            public void ProcessOnBetStopFixture(IMessageBroker messageBroker, OnBetStopFixtureMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_BET_STOP_FIXTURE);

            public void ProcessOnTranslationChangeMessage(IMessageBroker messageBroker, OnTranslationChangeMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_TRANSLATION_CHANGE);
        }
    }
}
