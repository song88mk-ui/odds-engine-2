using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using OE.Sportsbook.Service.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;
using System.Text;
using System.Text.Json;

namespace OE.Sportsbook.Service.Tests
{
    // Worker dispatches on BasicProperties.Type to the matching ISportsbookRepository method.
    // Unlike OE.Adapter.Service's Worker, a handler exception here is only logged — nothing is
    // published to an error routing key — and SetInitialData failing during startup is NOT
    // swallowed: it rethrows so the host fails fast rather than serving stale/no data.
    public class WorkerTests
    {
        private static BasicDeliverEventArgs Delivery(string messageType, object payload, string correlationId = "cid", string routingKey = "r.oe.sb.test")
        {
            var properties = new BasicProperties { Type = messageType, CorrelationId = correlationId };
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            return new BasicDeliverEventArgs("consumer", 1, false, "e.central", routingKey, properties, body);
        }

        private static Worker BuildWorker(FakeMessageBroker broker, FakeSportsbookRepository? repository = null) =>
            new(NullLogger<Worker>.Instance, broker, repository ?? new FakeSportsbookRepository(), new Instrumentation());

        // BackgroundService schedules ExecuteAsync's work rather than running its synchronous
        // prefix inline (confirmed via OE.Adapter.Service.Tests) — awaiting StartAsync alone does
        // NOT guarantee SetInitialData()/Connect() have run yet. ExecuteTask (public on the base
        // class since .NET 8) is the reliable signal.
        private static async Task<Worker> StartAndWaitAsync(Worker worker)
        {
            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!;
            return worker;
        }

        [Fact]
        public async Task ExecuteAsync_Success_PrefetchesDataThenConnects()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            Assert.Equal(1, repository.SetInitialDataCalls);
            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_SetInitialDataThrows_PropagatesAndNeverConnects()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnSetInitialData = true };
            var worker = BuildWorker(broker, repository);

            await worker.StartAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!);
            Assert.Equal(0, broker.ConnectCalls);
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

            Assert.Equal(1, broker.DisposeCalls);
        }

        [Theory]
        [InlineData(MessageType.ON_MARKET_SETTLEMENT)]
        [InlineData(MessageType.ON_BET_SETTLEMENT)]
        [InlineData(MessageType.ON_BET_STOP_GLOBAL)]
        [InlineData(MessageType.ON_BET_STOP_FIXTURE)]
        public async Task MessageReceived_DispatchesEachKnownTypeToTheMatchingHandler(string messageType)
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(messageType, new { }));

            Assert.Equal(new[] { messageType }, repository.Invocations);
        }

        [Fact]
        public async Task MessageReceived_OnFixtureChange_DeserializesAndDispatches()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
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
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            Assert.Single(repository.MarketChangeCalls);
        }

        [Fact]
        public async Task MessageReceived_OnDynamicTranslationChange_DispatchesToCacheInvalidation()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_DYNAMIC_TRANSLATION_CHANGE, new OnDeleteCacheMessage { CacheKey = "K1" }));

            var call = Assert.Single(repository.CacheInvalidationCalls);
            Assert.Equal("K1", call.CacheKey);
        }

        // GM/repo gap, not covered here by design: ISportsbookRepository declares
        // ProcessOnMarketCancellationMessage, but Worker's switch has no
        // MessageType.ON_MARKET_CANCEL case — such messages silently fall through, identically to
        // an unrecognized type. Pinning the CURRENT behavior, not asserting it's correct.
        [Fact]
        public async Task MessageReceived_OnMarketCancel_FallsThroughWithNoDispatch()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CANCEL, new OnMarketCancellationMessage()));

            Assert.Empty(repository.Invocations);
        }

        // GM-959 tripwire. ON_MARKET_CANCEL is dormant (case removed in DEVOPS-251), so
        // ProcessOnMarketCancellationMessage never runs. Its OnBetCancellation on
        // r.oe.bet.settlement carries MarketId as a string, but platform-final Settlement reads
        // it as OnUOFBetCancellation.MarketId (long) and throws JsonException. Green while the
        // case is dormant; fails if the case returns without the payload fix; green again once
        // both ship together. If Settlement is changed to accept a string MarketId instead,
        // update this test. bet-genius docs: AR-17, AD-12.
        [Fact]
        public async Task MessageReceived_OnMarketCancel_IfDispatched_OnBetCancellationMarketIdMustBeNumeric()
        {
            var workerBroker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(workerBroker, repository));
            var cancellation = new OnMarketCancellationMessage
            {
                FiId = 1,
                Ma = { new MarketBetCancellationMessage { MaId = "2", VoRe = "void reason" } }
            };

            workerBroker.Raise(Delivery(MessageType.ON_MARKET_CANCEL, cancellation));
            var dispatched = repository.Invocations.Contains(MessageType.ON_MARKET_CANCEL);

            var broker = new FakeMessageBroker();
            SportsbookRepositoryTests.CreateRepository().ProcessOnMarketCancellationMessage(broker, cancellation, "cid");
            var published = Assert.Single(broker.Published, p => p.RoutingKey == "r.oe.bet.settlement");
            // Serialized as MessageBroker.Publish<T> does: System.Text.Json, default options.
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(published.Payload));
            var marketIdKind = json.RootElement.GetProperty("MarketId").ValueKind;

            Assert.False(dispatched && marketIdKind != JsonValueKind.Number,
                $"Worker now dispatches {MessageType.ON_MARKET_CANCEL}, but OnBetCancellation.MarketId is a JSON {marketIdKind}. "
                + "platform-final Settlement and CashOutStake cannot read it. Fix the payload with the case (GM-959).");
        }

        [Fact]
        public async Task MessageReceived_UnknownMessageType_IsIgnoredWithoutError()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("SomeUnhandledType", new { }));

            Assert.Empty(repository.Invocations);
        }

        [Fact]
        public async Task MessageReceived_EmptyPayload_SkipsProcessing()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            var properties = new BasicProperties { Type = MessageType.ON_FIXTURE_CHANGE, CorrelationId = "cid" };
            broker.Raise(new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.sb.test", properties, Array.Empty<byte>()));

            Assert.Empty(repository.Invocations);
        }

        // Contrast with OE.Adapter.Service.Worker: no r.error.oe.adapter publish here — a handler
        // failure is logged and swallowed, full stop.
        [Fact]
        public async Task MessageReceived_HandlerThrows_IsSwallowedWithoutPublishing()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnFixtureChange = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_FIXTURE_CHANGE, new OnFixtureChangeMessage { PrFiId = "F1" }));

            Assert.Single(repository.FixtureChangeCalls); // it was attempted
            Assert.Empty(broker.Published); // but nothing published on failure
        }
        private sealed class FakeSportsbookRepository : ISportsbookRepository
        {
            public bool ThrowOnSetInitialData { get; set; }
            public bool ThrowOnFixtureChange { get; set; }
            public int SetInitialDataCalls { get; private set; }

            public List<string> Invocations { get; } = new();
            public List<(IMessageBroker Broker, OnFixtureChangeMessage Message, string CorrelationId)> FixtureChangeCalls { get; } = new();
            public List<(IMessageBroker Broker, OnMarketChangeMessage Message, string CorrelationId)> MarketChangeCalls { get; } = new();
            public List<OnDeleteCacheMessage> CacheInvalidationCalls { get; } = new();

            public Task SetInitialData()
            {
                SetInitialDataCalls++;
                if (ThrowOnSetInitialData)
                    throw new InvalidOperationException("prefetch failed");
                return Task.CompletedTask;
            }

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

            public void ProcessOnBetStopFixtureMessage(IMessageBroker messageBroker, OnBetStopFixtureMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_BET_STOP_FIXTURE);

            public void ProcessOnMarketCancellationMessage(IMessageBroker messageBroker, OnMarketCancellationMessage message, string correlationId)
                => Invocations.Add(MessageType.ON_MARKET_CANCEL);

            public void ProcessCacheInvalidationMessage(OnDeleteCacheMessage message)
            {
                Invocations.Add(MessageType.ON_DYNAMIC_TRANSLATION_CHANGE);
                CacheInvalidationCalls.Add(message);
            }
        }
    }
}
