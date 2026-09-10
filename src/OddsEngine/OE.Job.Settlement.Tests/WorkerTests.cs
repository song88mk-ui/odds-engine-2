using Microsoft.Extensions.Logging.Abstractions;
using OE.Job.Settlement.Repository;
using OE.Job.Settlement.Telemetry;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Job.Settlement.Tests
{
    // A periodic (5-minute) loop: run ProcessSettlement once, then delay, forever. Unlike the
    // message-dispatch Workers elsewhere in this solution, StopAsync here has NO try/catch around
    // broker.Dispose() — a Dispose failure propagates out of StopAsync rather than being swallowed.
    // SportsbookRepository itself constructs a real MongoClient in its constructor with no seam —
    // not unit-testable without a live MongoDB, same limitation as OE.Job.Lookup's repository.
    public class WorkerTests
    {
        private static Worker BuildWorker(FakeMessageBroker broker, FakeSportsbookRepository? repository = null) =>
            new(NullLogger<Worker>.Instance, repository ?? new FakeSportsbookRepository(), broker, new Instrumentation());

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
        public async Task ExecuteAsync_RunsProcessSettlementOnce_ThenWaitsOnTheIntervalUntilStopped()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var worker = BuildWorker(broker, repository);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.ProcessCalls > 0);

            // Still waiting on the 5-minute delay for the next iteration — never completes on its own.
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.Equal(1, repository.ProcessCalls);

            await worker.StopAsync(CancellationToken.None);

            Assert.True(worker.ExecuteTask.IsCompleted);
        }

        [Fact]
        public async Task ExecuteAsync_ProcessSettlementThrows_IsCaughtAndLogged_LoopSurvives()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnProcess = true };
            var worker = BuildWorker(broker, repository);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.ProcessCalls > 0);

            Assert.False(worker.ExecuteTask!.IsCompleted); // still alive, waiting for the next interval

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

        // Contrast with every other Worker in this solution: no try/catch around Dispose() here,
        // so a disposal failure propagates out of StopAsync instead of being swallowed and logged.
        [Fact]
        public async Task StopAsync_BrokerDisposeThrows_PropagatesRatherThanBeingSwallowed()
        {
            var broker = new FakeMessageBroker { ThrowOnDispose = true };
            var worker = BuildWorker(broker);
            await worker.StartAsync(CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StopAsync(CancellationToken.None));
        }
        private sealed class FakeSportsbookRepository : ISportsbookRepository
        {
            public bool ThrowOnProcess { get; set; }
            public int ProcessCalls { get; private set; }

            public Task ProcessSettlement(IMessageBroker messageBroker)
            {
                ProcessCalls++;
                if (ThrowOnProcess)
                    throw new InvalidOperationException("settlement processing failed");
                return Task.CompletedTask;
            }
        }
    }
}
