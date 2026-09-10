using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using OE.Translation.Service.Repositories;
using OE.Translation.Service.Telemetry;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;
using System.Text;
using System.Text.Json;

namespace OE.Translation.Service.Tests
{
    // Startup order: preload the translation cache (retried; blocks consuming), then two optional
    // non-fatal migration steps (SeedActiveSnapshot / BackfillActiveSnapshot — Backfill defaults ON
    // unless explicitly disabled), then connect and dispatch. A handler exception publishes to
    // r.oe.translation.error, mirroring OE.Adapter.Service's Worker.
    public class WorkerTests
    {
        private static BasicDeliverEventArgs Delivery(string messageType, object payload, string correlationId = "cid", string routingKey = "r.oe.translation.test")
        {
            var properties = new BasicProperties { Type = messageType, CorrelationId = correlationId };
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            return new BasicDeliverEventArgs("consumer", 1, false, "e.central", routingKey, properties, body);
        }

        private static Worker BuildWorker(
            FakeMessageBroker broker,
            FakeTranslationCache? translationCache = null,
            FakeTranslationRepository? translationRepository = null,
            FakeMongoTranslationRepository? mongoRepository = null,
            Dictionary<string, string?>? configOverrides = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configOverrides ?? new Dictionary<string, string?>())
                .Build();

            return new Worker(
                NullLogger<Worker>.Instance,
                broker,
                translationCache ?? new FakeTranslationCache(),
                translationRepository ?? new FakeTranslationRepository(),
                mongoRepository ?? new FakeMongoTranslationRepository(),
                configuration,
                new Instrumentation());
        }

        private static async Task<Worker> StartAndWaitAsync(Worker worker)
        {
            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!;
            return worker;
        }

        // ---- Startup sequencing ----

        [Fact]
        public async Task ExecuteAsync_Default_PreloadsThenBackfillsThenConnects()
        {
            var broker = new FakeMessageBroker();
            var cache = new FakeTranslationCache();
            var mongo = new FakeMongoTranslationRepository();
            await StartAndWaitAsync(BuildWorker(broker, cache, mongoRepository: mongo));

            Assert.Equal(1, cache.PreloadCalls);
            Assert.Equal(1, mongo.BackfillCalls); // defaults to true
            Assert.Equal(0, mongo.ReadActiveSnapshotCalls); // SeedActiveSnapshot defaults to false
            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_BackfillDisabled_SkipsBackfill()
        {
            var broker = new FakeMessageBroker();
            var mongo = new FakeMongoTranslationRepository();
            await StartAndWaitAsync(BuildWorker(broker, mongoRepository: mongo,
                configOverrides: new() { ["Custom:Translation:BackfillActiveSnapshot"] = "false" }));

            Assert.Equal(0, mongo.BackfillCalls);
            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_BackfillThrows_IsNonFatal_StillConnects()
        {
            var broker = new FakeMessageBroker();
            var mongo = new FakeMongoTranslationRepository { ThrowOnBackfill = true };
            await StartAndWaitAsync(BuildWorker(broker, mongoRepository: mongo));

            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_SeedActiveSnapshotEnabled_ReadsMongoThenSeeds()
        {
            var broker = new FakeMessageBroker();
            var mongo = new FakeMongoTranslationRepository { Snapshot = new() { ("Te:1", "Arsenal") } };
            var translationRepository = new FakeTranslationRepository();
            await StartAndWaitAsync(BuildWorker(broker, translationRepository: translationRepository, mongoRepository: mongo,
                configOverrides: new() { ["Custom:Translation:SeedActiveSnapshot"] = "true" }));

            Assert.Equal(1, mongo.ReadActiveSnapshotCalls);
            var seeded = Assert.Single(translationRepository.SeededSnapshots);
            Assert.Equal(("Te:1", "Arsenal"), Assert.Single(seeded));
        }

        [Fact]
        public async Task ExecuteAsync_SeedActiveSnapshotThrows_IsNonFatal_StillConnects()
        {
            var broker = new FakeMessageBroker();
            var mongo = new FakeMongoTranslationRepository { ThrowOnReadActiveSnapshot = true };
            await StartAndWaitAsync(BuildWorker(broker, mongoRepository: mongo,
                configOverrides: new() { ["Custom:Translation:SeedActiveSnapshot"] = "true" }));

            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task ExecuteAsync_PreloadFails_KeepsRetryingUntilStopped_WithoutEverConnecting()
        {
            var broker = new FakeMessageBroker();
            var cache = new FakeTranslationCache { ThrowOnPreload = true };
            var worker = BuildWorker(broker, cache);

            await worker.StartAsync(CancellationToken.None);
            await TestWait.UntilAsync(() => cache.PreloadCalls > 0, TimeSpan.FromSeconds(5)); // let the first failed attempt enter its retry delay

            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.Equal(1, cache.PreloadCalls);
            Assert.Equal(0, broker.ConnectCalls);

            await worker.StopAsync(CancellationToken.None); // cancels the pending retry delay

            Assert.True(worker.ExecuteTask.IsCompleted);
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
        }

        // ---- Message dispatch ----

        [Fact]
        public async Task MessageReceived_OnFixtureChange_UpdatesSqlThenMongo()
        {
            var broker = new FakeMessageBroker();
            var translationRepository = new FakeTranslationRepository();
            var mongo = new FakeMongoTranslationRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, translationRepository: translationRepository, mongoRepository: mongo));

            broker.Raise(Delivery(MessageType.ON_FIXTURE_CHANGE, new OnFixtureChangeMessage { PrFiId = "F1" }));

            Assert.Single(translationRepository.FixtureSetUpdateCalls);
            Assert.Single(mongo.FixtureTranslationWrites);
        }

        [Fact]
        public async Task MessageReceived_OnMarketChange_UpdatesSqlThenMongo()
        {
            var broker = new FakeMessageBroker();
            var translationRepository = new FakeTranslationRepository();
            var mongo = new FakeMongoTranslationRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, translationRepository: translationRepository, mongoRepository: mongo));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            Assert.Single(translationRepository.MarketSetUpdateCalls);
            Assert.Single(mongo.MarketTranslationWrites);
        }

        [Fact]
        public async Task MessageReceived_OnTranslationChange_AppliesToCache_AndUpdatesMongo()
        {
            var broker = new FakeMessageBroker();
            var cache = new FakeTranslationCache();
            var mongo = new FakeMongoTranslationRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, cache, mongoRepository: mongo));

            var message = new OnTranslationChangeMessage
            {
                Tr = new List<TranslationChangeItem> { new() { StId = "Te:1", LaCo = "ZH", Tx = "阿森纳" } }
            };
            broker.Raise(Delivery(MessageType.ON_TRANSLATION_CHANGE, message, correlationId: "cid-tr"));

            var applied = Assert.Single(cache.AppliedItems);
            Assert.Equal("Te:1", applied.StId);
            var mongoCall = Assert.Single(mongo.TranslationChangeCalls);
            Assert.Equal("cid-tr", mongoCall.CorrelationId);
        }

        [Fact]
        public async Task MessageReceived_UnknownMessageType_IsIgnoredWithoutError()
        {
            var broker = new FakeMessageBroker();
            var translationRepository = new FakeTranslationRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, translationRepository: translationRepository));

            broker.Raise(Delivery("SomeUnhandledType", new { }));

            Assert.Empty(translationRepository.FixtureSetUpdateCalls);
            Assert.Empty(broker.Published);
        }

        [Fact]
        public async Task MessageReceived_EmptyPayload_SkipsProcessing()
        {
            var broker = new FakeMessageBroker();
            var translationRepository = new FakeTranslationRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, translationRepository: translationRepository));

            var properties = new BasicProperties { Type = MessageType.ON_FIXTURE_CHANGE, CorrelationId = "cid" };
            broker.Raise(new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.translation.test", properties, Array.Empty<byte>()));

            Assert.Empty(translationRepository.FixtureSetUpdateCalls);
        }

        [Fact]
        public async Task MessageReceived_HandlerThrows_PublishesToErrorRoutingKeyAndDoesNotRethrow()
        {
            var broker = new FakeMessageBroker();
            var translationRepository = new FakeTranslationRepository { ThrowOnFixtureSetUpdate = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker, translationRepository: translationRepository));

            broker.Raise(Delivery(MessageType.ON_FIXTURE_CHANGE, new OnFixtureChangeMessage { PrFiId = "F1" }, correlationId: "cid-err"));

            var published = Assert.Single(broker.Published);
            Assert.Equal("r.oe.translation.error", published.RoutingKey);
            Assert.Equal(MessageType.ON_FIXTURE_CHANGE, published.MessageType);
            Assert.Equal("cid-err", published.CorrelationId);
        }
        private sealed class FakeTranslationCache : ITranslationCache
        {
            public int PreloadCalls { get; private set; }
            public bool ThrowOnPreload { get; set; }
            public List<TranslationChangeItem> AppliedItems { get; } = new();

            public void Preload()
            {
                PreloadCalls++;
                if (ThrowOnPreload)
                    throw new InvalidOperationException("preload failed");
            }

            public List<TranslationMessage>? GetTranslations(string stringId, string currentText) => null;

            public void Apply(List<TranslationChangeItem> items) => AppliedItems.AddRange(items);
        }

        private sealed class FakeTranslationRepository : ITranslationRepository
        {
            public bool ThrowOnFixtureSetUpdate { get; set; }

            public List<OnFixtureChangeMessage> FixtureSetUpdateCalls { get; } = new();
            public List<OnMarketChangeMessage> MarketSetUpdateCalls { get; } = new();
            public List<List<(string id, string value)>> SeededSnapshots { get; } = new();

            public Task OnProcessFixtureSetUpdate(OnFixtureChangeMessage message)
            {
                FixtureSetUpdateCalls.Add(message);
                if (ThrowOnFixtureSetUpdate)
                    throw new InvalidOperationException("handler failed");
                return Task.CompletedTask;
            }

            public Task OnProcessMarketSetUpdate(OnMarketChangeMessage message)
            {
                MarketSetUpdateCalls.Add(message);
                return Task.CompletedTask;
            }

            public Task SeedActiveSnapshotAsync(List<(string id, string value)> items)
            {
                SeededSnapshots.Add(items);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeMongoTranslationRepository : IMongoTranslationRepository
        {
            public bool ThrowOnBackfill { get; set; }
            public bool ThrowOnReadActiveSnapshot { get; set; }
            public List<(string Id, string Text)> Snapshot { get; set; } = new();

            public int BackfillCalls { get; private set; }
            public int ReadActiveSnapshotCalls { get; private set; }
            public List<OnFixtureChangeMessage> FixtureTranslationWrites { get; } = new();
            public List<OnMarketChangeMessage> MarketTranslationWrites { get; } = new();
            public List<(IMessageBroker Broker, OnTranslationChangeMessage Message, string CorrelationId)> TranslationChangeCalls { get; } = new();

            public void WriteFixtureTranslations(OnFixtureChangeMessage message) => FixtureTranslationWrites.Add(message);

            public void WriteMarketTranslations(OnMarketChangeMessage message) => MarketTranslationWrites.Add(message);

            public void ProcessTranslationChange(IMessageBroker messageBroker, OnTranslationChangeMessage message, string correlationId)
                => TranslationChangeCalls.Add((messageBroker, message, correlationId));

            public List<(string Id, string Text)> ReadActiveSnapshot()
            {
                ReadActiveSnapshotCalls++;
                if (ThrowOnReadActiveSnapshot)
                    throw new InvalidOperationException("read snapshot failed");
                return Snapshot;
            }

            public void BackfillActiveSnapshot()
            {
                BackfillCalls++;
                if (ThrowOnBackfill)
                    throw new InvalidOperationException("backfill failed");
            }
        }
    }
}
