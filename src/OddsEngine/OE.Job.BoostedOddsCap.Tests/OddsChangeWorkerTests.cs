using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Job.BoostedOddsCap.Alerts;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Data;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Job.BoostedOddsCap.Telemetry;
using OE.Shared.Test;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;

namespace OE.Job.BoostedOddsCap.Tests
{
    // Drives the consumer through the same event the real broker raises, with no broker. What matters here is the
    // contract MessageBroker imposes on the handler: it runs for every odds message at full rate, and anything it
    // throws makes the broker redeliver the same message forever, so it must swallow everything.
    public class OddsChangeWorkerTests
    {
        private sealed class Rig
        {
            public readonly FakeMessageBroker Broker = new FakeMessageBroker();
            public readonly AlertQueue Queue = new AlertQueue(100, _ => { });
            public readonly OddsChangeWorker Worker;

            public Rig(bool enabled = true, bool withBoost = true, string consumerCheckSeconds = "30", string consumerDeadChecks = "2")
            {
                Broker.IsConsuming = true;

                var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Custom:Enabled"] = enabled.ToString(),
                    ["Custom:StartupSilentSeconds"] = "0",
                    ["Custom:ConsumerCheckSeconds"] = consumerCheckSeconds,
                    ["Custom:ConsumerDeadChecks"] = consumerDeadChecks
                }).Build();

                var holder = new BoostSnapshotHolder();
                if (withBoost)
                {
                    holder.Set(BoostSnapshot.Build(
                        new[]
                        {
                            new PriceBoostRow
                            {
                                ClientCode = "S88", ProviderFixtureId = TestMessages.FixtureId, MarketTemplateId = "1",
                                SelectionTemplateId = "1", BoostedOdds = 2.10m
                            }
                        },
                        new[] { new BoostedSettingsRow { ClientCode = "S88", Cap = 0.05m } },
                        new Dictionary<string, string[]>(), DateTime.UtcNow));
                }

                var tracker = new CapAlertTracker(0.25m);
                Worker = new OddsChangeWorker(NullLogger<OddsChangeWorker>.Instance, Broker, configuration, holder,
                        new OddsChangeEvaluator(tracker, null), tracker, Queue, new Instrumentation());
            }

            public async Task Start()
            {
                await Worker.StartAsync(CancellationToken.None);
                SpinWait.SpinUntil(() => Broker.ConnectCalls > 0, TimeSpan.FromSeconds(2));
            }

            public void Raise(string type, byte[] body)
            {
                Broker.Raise(new BasicDeliverEventArgs
                {
                    BasicProperties = new BasicProperties { Type = type },
                    Body = body
                });
            }

            public List<CapAlert> Drain()
            {
                var alerts = new List<CapAlert>();
                while (Queue.Reader.TryRead(out var alert))
                    alerts.Add(alert);
                return alerts;
            }
        }

        private static byte[] Message(string fixtureId = TestMessages.FixtureId, int status = 0, decimal odds = 2.00m)
            => JsonSerializer.SerializeToUtf8Bytes(TestMessages.OddsChange(fixtureId, status, TestMessages.EvenMarket(odds)));

        [Fact]
        public async Task QueuesAnAlertForABoostThatHasReachedItsCap()
        {
            var rig = new Rig();
            await rig.Start();

            rig.Raise("OnOddsChange", Message());

            var alert = Assert.Single(rig.Drain());
            Assert.Equal("S88", alert.ClientCode);
            Assert.Equal(2.00m, alert.CurrentOdds);
        }

        [Fact]
        public async Task SubscribesBeforeItConnectsSoNoEarlyDeliveryIsLost()
        {
            var rig = new Rig();
            // MessageBroker acknowledges a delivery that finds MessageReceived null, so one that arrives while
            // Connect is still running would be dropped; the worker must be listening by then
            rig.Broker.OnConnect = () => rig.Raise("OnOddsChange", Message());

            await rig.Start();

            Assert.Single(rig.Drain());
        }

        [Fact]
        public async Task DoesNotAlertAgainForTheSameBreachOnTheNextMessage()
        {
            var rig = new Rig();
            await rig.Start();

            rig.Raise("OnOddsChange", Message());
            rig.Raise("OnOddsChange", Message());

            Assert.Single(rig.Drain());
        }

        [Fact]
        public async Task StaysQuietForAFixtureWithoutBoostsAndForAnyOtherMessageType()
        {
            var rig = new Rig();
            await rig.Start();

            rig.Raise("OnOddsChange", Message(fixtureId: "sr:match:nobody-boosted-this"));
            rig.Raise("OnFixtureChange", Message());
            rig.Raise(null!, Message());

            Assert.Empty(rig.Drain());
        }

        [Fact]
        public async Task LeavesALiveFixtureAlone()
        {
            var rig = new Rig();
            await rig.Start();

            rig.Raise("OnOddsChange", Message(status: 1));

            Assert.Empty(rig.Drain());
        }

        [Fact]
        public async Task NeverThrowsBackToTheBrokerWhateverArrives()
        {
            var rig = new Rig();
            await rig.Start();

            // a throw would make MessageBroker requeue the message and redeliver it forever
            var truncated = Encoding.UTF8.GetBytes("{\"PrFiId\":\"" + TestMessages.FixtureId + "\",\"FiStId\":0,\"Ma\":[{\"MaId\":");

            rig.Raise("OnOddsChange", truncated);
            rig.Raise("OnOddsChange", Encoding.UTF8.GetBytes("not json at all"));
            rig.Raise("OnOddsChange", new byte[0]);
            rig.Raise("OnOddsChange", null!);

            Assert.Empty(rig.Drain());

            // and it is still working afterwards
            rig.Raise("OnOddsChange", Message());
            Assert.Single(rig.Drain());
        }

        [Fact]
        public async Task DoesNotConnectWhenTheJobIsDisabled()
        {
            var rig = new Rig(enabled: false);

            await rig.Worker.StartAsync(CancellationToken.None);
            await Task.Delay(100);

            Assert.Equal(0, rig.Broker.ConnectCalls);
        }

        [Fact]
        public async Task WaitsForTheFirstBoostIndexLoadBeforeConnecting()
        {
            var rig = new Rig(withBoost: false);

            await rig.Worker.StartAsync(CancellationToken.None);
            await Task.Delay(100);

            Assert.Equal(0, rig.Broker.ConnectCalls);
            await rig.Worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task FailsTheHostWhenTheConsumerIsLostAndDoesNotRecover()
        {
            // RabbitMQ.Client does not restart a consumer the broker cancelled; without this the job would sit
            // running and alert on nothing
            var rig = new Rig(consumerCheckSeconds: "0.02", consumerDeadChecks: "2");
            rig.Broker.IsConsuming = false;

            await rig.Start();

            // bounded, so a worker that never gives up fails this test instead of hanging the run
            var finished = await Task.WhenAny(rig.Worker.ExecuteTask!, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(rig.Worker.ExecuteTask, finished);

            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Worker.ExecuteTask!);
            Assert.Contains("stopped and did not recover", failure.Message);
        }

        [Fact]
        public async Task ToleratesAConsumerThatRecoversBeforeTheLimit()
        {
            var rig = new Rig(consumerCheckSeconds: "0.02", consumerDeadChecks: "50");
            rig.Broker.IsConsuming = false;
            await rig.Start();

            await Task.Delay(100);
            rig.Broker.IsConsuming = true;
            await Task.Delay(300);

            Assert.False(rig.Worker.ExecuteTask!.IsCompleted);
            await rig.Worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task KeepsRunningWhileTheConsumerIsHealthy()
        {
            var rig = new Rig(consumerCheckSeconds: "0.02", consumerDeadChecks: "2");
            await rig.Start();

            await Task.Delay(300);

            Assert.False(rig.Worker.ExecuteTask!.IsCompleted);
            await rig.Worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ClosesTheBrokerWhenItStops()
        {
            var rig = new Rig();
            await rig.Start();

            await rig.Worker.StopAsync(CancellationToken.None);

            Assert.Equal(1, rig.Broker.DisposeCalls);
        }
    }
}
