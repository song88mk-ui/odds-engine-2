using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using OE.Translation.Job.Repositories;
using OE.Translation.Job.Workers;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;
using System.Text;
using System.Text.Json;

namespace OE.Translation.Job.Tests.Workers
{
    // Manual-ack consumer: every message is acknowledged regardless of outcome (a missed flag is
    // recoverable — the row just stays eligible for cleanup a bit longer — so this deliberately
    // never requeues, to avoid a poison-message loop).
    public class BetPlacementWorkerTests
    {
        private static BasicDeliverEventArgs Delivery(string messageType, string jsonPayload, string? correlationId = "cid")
        {
            var properties = new BasicProperties { Type = messageType, CorrelationId = correlationId };
            var body = Encoding.UTF8.GetBytes(jsonPayload);
            return new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.bet.placement", properties, body);
        }

        private static string PlacementJson(params (long fixtureId, string marketTemplateId, string selectionTemplateId)[] selections)
        {
            var items = selections.Select(s => new
            {
                BetSelection = new
                {
                    FixtureId = s.fixtureId,
                    MarketId = 1,
                    SelectionId = 1,
                    MarketTemplateId = s.marketTemplateId,
                    SelectionTemplateId = s.selectionTemplateId
                }
            });
            return JsonSerializer.Serialize(new { BetSelections = items });
        }

        private static BetPlacementWorker BuildWorker(FakeMessageBroker broker, FakeCleanupRepository? repository = null)
        {
            var scopeFactory = new ServiceCollection()
                .AddScoped<ITranslationCleanupRepository>(_ => repository ?? new FakeCleanupRepository())
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

            return new BetPlacementWorker(NullLogger<BetPlacementWorker>.Instance, broker, scopeFactory);
        }

        private static async Task<BetPlacementWorker> StartAndWaitAsync(BetPlacementWorker worker)
        {
            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!;
            return worker;
        }

        [Fact]
        public async Task ExecuteAsync_ConnectsWithManualAck()
        {
            var broker = new FakeMessageBroker();
            await StartAndWaitAsync(BuildWorker(broker));

            Assert.Equal(1, broker.ConnectWithManualAckCalls);
        }

        [Fact]
        public async Task MessageReceived_OnBetPlacement_FlagsExtractedKeys_AndAcknowledges()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeCleanupRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("OnBetPlacement", PlacementJson((1, "16", "1"))));

            var call = Assert.Single(repository.FlagCalls);
            Assert.Contains("MaTe:16", call);
            Assert.Contains("SeTe:16:1", call);
            Assert.Equal(1, broker.AcknowledgeCalls);
            Assert.Empty(broker.Published);
        }

        [Fact]
        public async Task MessageReceived_CaseInsensitiveType_StillDispatches()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeCleanupRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("onbetplacement", PlacementJson((1, "16", "1"))));

            Assert.Single(repository.FlagCalls);
        }

        [Fact]
        public async Task MessageReceived_NoExtractableKeys_SkipsRepository_StillAcknowledges()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeCleanupRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("OnBetPlacement", PlacementJson())); // empty BetSelections

            Assert.Empty(repository.FlagCalls);
            Assert.Equal(1, broker.AcknowledgeCalls);
        }

        [Fact]
        public async Task MessageReceived_UnknownMessageType_SkipsProcessing_StillAcknowledges()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeCleanupRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("SomethingElse", "{}"));

            Assert.Empty(repository.FlagCalls);
            Assert.Equal(1, broker.AcknowledgeCalls);
        }

        [Fact]
        public async Task MessageReceived_RepositoryThrows_PublishesErrorButStillAcknowledges()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeCleanupRepository { ThrowOnFlag = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("OnBetPlacement", PlacementJson((1, "16", "1")), correlationId: "cid-err"));

            var published = Assert.Single(broker.Published);
            Assert.Equal("r.oe.translation.job.error", published.RoutingKey);
            Assert.Equal("cid-err", published.CorrelationId);
            Assert.Equal(1, broker.AcknowledgeCalls); // best-effort: never requeued
        }

        [Fact]
        public async Task MessageReceived_MalformedJson_PublishesErrorButStillAcknowledges()
        {
            var broker = new FakeMessageBroker();
            var worker = await StartAndWaitAsync(BuildWorker(broker));

            broker.Raise(Delivery("OnBetPlacement", "not-json"));

            Assert.Single(broker.Published);
            Assert.Equal(1, broker.AcknowledgeCalls);
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
        private sealed class FakeCleanupRepository : ITranslationCleanupRepository
        {
            public bool ThrowOnFlag { get; set; }
            public List<IReadOnlyCollection<string>> FlagCalls { get; } = new();

            public Task<int> FlagBetPlacementAsync(IReadOnlyCollection<string> stringIds, CancellationToken cancellationToken)
            {
                FlagCalls.Add(stringIds);
                if (ThrowOnFlag)
                    throw new InvalidOperationException("flag failed");
                return Task.FromResult(stringIds.Count);
            }

            public Task<int> CleanupAsync(IReadOnlyCollection<string> prefixes, int retentionDays, int batchSize, CancellationToken cancellationToken) =>
                Task.FromResult(0);
        }
    }
}
