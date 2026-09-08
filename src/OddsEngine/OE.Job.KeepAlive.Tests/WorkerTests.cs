using Microsoft.Extensions.Logging.Abstractions;
using OE.Job.KeepAlive.Messages;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Job.KeepAlive.Tests
{
    // Publishes an OnHeartbeat message every 5 seconds, forever. Like OE.Job.Settlement's Worker,
    // StopAsync has no try/catch around Dispose() — a failure there propagates rather than being
    // swallowed.
    public class WorkerTests
    {
        private static Worker BuildWorker(FakeMessageBroker broker) =>
            new(NullLogger<Worker>.Instance, broker);

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.True(condition(), "condition was not met within the timeout");
        }

        [Fact]
        public async Task StartAsync_ConnectsToBrokerBeforeLoopStarts()
        {
            var broker = new FakeMessageBroker();
            var worker = BuildWorker(broker);

            await worker.StartAsync(CancellationToken.None);

            Assert.Equal(1, broker.ConnectCalls);

            await worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_PublishesHealthyHeartbeat_ThenWaitsUntilStopped()
        {
            var broker = new FakeMessageBroker();
            var worker = BuildWorker(broker);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => broker.Published.Count > 0);

            Assert.False(worker.ExecuteTask!.IsCompleted); // still waiting on the 5s delay

            var (payload, messageType, routingKey, correlationId) = broker.Published[0];
            var heartbeat = Assert.IsType<OnHeartbeatMessage>(payload);
            Assert.True(heartbeat.IsHealthy);
            Assert.True(heartbeat.Timestamp > 0);
            Assert.Equal("OnHeartbeat", messageType);
            Assert.Equal("r.oe.uof.heartbeat", routingKey);
            Assert.Equal(heartbeat.Timestamp.ToString(), correlationId);

            await worker.StopAsync(CancellationToken.None);
            Assert.True(worker.ExecuteTask.IsCompleted);
        }

        [Fact]
        public async Task ExecuteAsync_PublishThrows_IsCaughtAndLogged_LoopSurvives()
        {
            var broker = new FakeMessageBroker { ThrowOnPublish = true };
            var worker = BuildWorker(broker);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => broker.PublishAttempts > 0);

            Assert.False(worker.ExecuteTask!.IsCompleted); // still alive, waiting for the next tick

            await worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task StopAsync_DisposesBroker()
        {
            var broker = new FakeMessageBroker();
            var worker = BuildWorker(broker);
            await worker.StartAsync(CancellationToken.None);

            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(1, broker.DisposeCalls);
        }

        [Fact]
        public async Task StopAsync_BrokerDisposeThrows_PropagatesRatherThanBeingSwallowed()
        {
            var broker = new FakeMessageBroker { ThrowOnDispose = true };
            var worker = BuildWorker(broker);
            await worker.StartAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StopAsync(CancellationToken.None));
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public async Task StartAsync_ConnectsToBrokerBeforeLoopStarts_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var worker = BuildWorker(broker);

            await worker.StartAsync(CancellationToken.None);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(0, broker.ConnectCalls));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(2, broker.ConnectCalls));

            await worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_PublishesHealthyHeartbeat_ThenWaitsUntilStopped_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var worker = BuildWorker(broker);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => broker.Published.Count > 0);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(worker.ExecuteTask!.IsCompleted));

            var (payload, messageType, routingKey, correlationId) = broker.Published[0];
            var heartbeat = Assert.IsType<OnHeartbeatMessage>(payload);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(heartbeat.IsHealthy));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(heartbeat.Timestamp <= 0));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("NotOnHeartbeat", messageType));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("r.oe.uof.wrong-key", routingKey));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("wrong-correlation-id", correlationId));

            await worker.StopAsync(CancellationToken.None);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(worker.ExecuteTask.IsCompleted));
        }

        [Fact]
        public async Task ExecuteAsync_PublishThrows_IsCaughtAndLogged_LoopSurvives_MutationCheck()
        {
            var broker = new FakeMessageBroker { ThrowOnPublish = true };
            var worker = BuildWorker(broker);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => broker.PublishAttempts > 0);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(worker.ExecuteTask!.IsCompleted));

            await worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task StopAsync_DisposesBroker_MutationCheck()
        {
            var broker = new FakeMessageBroker();
            var worker = BuildWorker(broker);
            await worker.StartAsync(CancellationToken.None);

            await worker.StopAsync(CancellationToken.None);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(0, broker.DisposeCalls));
        }

        [Fact]
        public async Task StopAsync_BrokerDisposeThrows_PropagatesRatherThanBeingSwallowed_MutationCheck()
        {
            var broker = new FakeMessageBroker { ThrowOnDispose = true };
            var worker = BuildWorker(broker);
            await worker.StartAsync(CancellationToken.None);

            // The real test proves StopAsync propagates InvalidOperationException. This proves
            // the proof itself discriminates: asserting that NO exception was thrown must fail
            // here, since one really is.
            await Assert.ThrowsAnyAsync<Xunit.Sdk.XunitException>(async () =>
            {
                var exception = await Record.ExceptionAsync(() => worker.StopAsync(CancellationToken.None));
                Assert.Null(exception);
            });
        }

        #endregion

    }
}
