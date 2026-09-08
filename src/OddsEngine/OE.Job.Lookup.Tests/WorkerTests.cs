using Microsoft.Extensions.Logging.Abstractions;
using OE.Job.Lookup.Repository;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;
using System.Text;
using System.Text.Json;

namespace OE.Job.Lookup.Tests
{
    // SportsbookRepository itself constructs a real MongoClient in its constructor and writes via
    // a live IMongoCollection<T> with no seam to intercept — not unit-testable without a live
    // MongoDB (this repo has no such test infrastructure). What IS testable, and covered here, is
    // Worker's dispatch against the ISportsbookRepository interface.
    public class WorkerTests
    {
        private static BasicDeliverEventArgs Delivery(string messageType, object payload, string correlationId = "cid")
        {
            var properties = new BasicProperties { Type = messageType, CorrelationId = correlationId };
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            return new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.adapter.onmarketchange.00", properties, body);
        }

        private static Worker BuildWorker(FakeMessageBroker broker, FakeSportsbookRepository? repository = null) =>
            new(NullLogger<Worker>.Instance, broker, repository ?? new FakeSportsbookRepository());

        private static async Task<Worker> StartAndWaitAsync(Worker worker)
        {
            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!;
            return worker;
        }

        [Fact]
        public async Task ExecuteAsync_ConnectsToBroker()
        {
            var broker = new FakeMessageBroker();
            await StartAndWaitAsync(BuildWorker(broker));

            Assert.Equal(1, broker.ConnectCalls);
        }

        [Fact]
        public async Task MessageReceived_OnMarketChange_DeserializesAndDispatches()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            var message = Assert.Single(repository.ProcessedMessages);
            Assert.Equal("F1", message.PrFiId);
        }

        [Fact]
        public async Task MessageReceived_UnknownMessageType_IsIgnored()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("SomeUnhandledType", new { }));

            Assert.Empty(repository.ProcessedMessages);
        }

        // No IsNullOrWhiteSpace guard exists here (unlike OE.Adapter.Service's Worker) — an empty
        // payload fails JSON deserialization, caught by the empty catch block, and is silently
        // swallowed (not even logged). Pinning that as the current, if surprising, behavior.
        [Fact]
        public async Task MessageReceived_MalformedPayload_IsSilentlySwallowed()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            var properties = new BasicProperties { Type = MessageType.ON_MARKET_CHANGE, CorrelationId = "cid" };
            broker.Raise(new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.test", properties, Array.Empty<byte>()));

            Assert.Empty(repository.ProcessedMessages);
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

        [Fact]
        public async Task MessageReceived_RepositoryThrows_IsCaughtAndDoesNotCrashDispatch()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnProcess = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            // No assertion beyond "didn't throw" — this Worker has no error-publish path.
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public async Task ExecuteAsync_ConnectsToBroker_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            await StartAndWaitAsync(BuildWorker(broker));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(0, broker.ConnectCalls));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(2, broker.ConnectCalls));
        }

        [Fact]
        public async Task MessageReceived_OnMarketChange_DeserializesAndDispatches_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Empty(repository.ProcessedMessages));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(2, repository.ProcessedMessages.Count));

            var message = Assert.Single(repository.ProcessedMessages);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("WrongFixtureId", message.PrFiId));
        }

        [Fact]
        public async Task MessageReceived_UnknownMessageType_IsIgnored_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            broker.Raise(Delivery("SomeUnhandledType", new { }));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(repository.ProcessedMessages));
        }

        [Fact]
        public async Task MessageReceived_MalformedPayload_IsSilentlySwallowed_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            var properties = new BasicProperties { Type = MessageType.ON_MARKET_CHANGE, CorrelationId = "cid" };
            broker.Raise(new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.test", properties, Array.Empty<byte>()));

            // The real test pins "no side effect at all" (not even a log line) on malformed
            // payloads. This proves that pin discriminates: if a mutation ever let a message
            // through here, asserting the collection stayed empty would fail.
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(repository.ProcessedMessages));
        }

        [Fact]
        public async Task StopAsync_DisposesBroker_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var worker = await StartAndWaitAsync(BuildWorker(broker));

            await worker.StopAsync(CancellationToken.None);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(0, broker.DisposeCalls));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(2, broker.DisposeCalls));
        }

        [Fact]
        public async Task StopAsync_BrokerDisposeThrows_IsCaughtAndDoesNotPropagate_MutationCheck()
        {
            var broker = new FakeMessageBroker { ThrowOnDispose = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker));

            // The real test proves StopAsync swallows the broker's dispose exception (it only
            // has to not throw to pass). This proves that proof discriminates: asserting that an
            // exception WAS thrown must fail here, since none actually propagates.
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(async () =>
            {
                var exception = await Record.ExceptionAsync(() => worker.StopAsync(CancellationToken.None));
                Assert.NotNull(exception);
            });
        }

        [Fact]
        public async Task MessageReceived_RepositoryThrows_IsCaughtAndDoesNotCrashDispatch_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnProcess = true };
            var worker = await StartAndWaitAsync(BuildWorker(broker, repository));

            // The real test has no assertion beyond "didn't throw" — if dispatch let the
            // repository's exception propagate, broker.Raise (and the whole test method) would
            // throw. This proves that implicit proof discriminates: asserting that an exception
            // WAS thrown must fail here, since dispatch really swallows it.
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            {
                var exception = Record.Exception(() =>
                    broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" })));
                Assert.NotNull(exception);
            });
        }

        #endregion

        private sealed class FakeSportsbookRepository : ISportsbookRepository
        {
            public bool ThrowOnProcess { get; set; }
            public List<OnMarketChangeMessage> ProcessedMessages { get; } = new();

            public void ProcessOnOddsChangeMessage(OnMarketChangeMessage message)
            {
                ProcessedMessages.Add(message);
                if (ThrowOnProcess)
                    throw new InvalidOperationException("process failed");
            }
        }
    }
}
