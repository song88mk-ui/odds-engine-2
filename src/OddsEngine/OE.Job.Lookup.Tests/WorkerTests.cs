using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Job.Lookup.Repository;
using OE.Job.Lookup.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Test;
using OE.Shared.Utilities;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Framing;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace OE.Job.Lookup.Tests
{
    // SportsbookRepository itself constructs a real MongoClient in its constructor and writes via
    // a live IMongoCollection<T> with no seam to intercept — not unit-testable without a live
    // MongoDB (this repo has no such test infrastructure). What IS testable, and covered here, is
    // Worker's dispatch against the ISportsbookRepository interface, its logging, and the consumer
    // watchdog that fails the host when the RabbitMQ consumer is lost (DEVOPS-443).
    public class WorkerTests
    {
        private static BasicDeliverEventArgs Delivery(string messageType, object payload, string correlationId = "cid", bool redelivered = false)
        {
            var properties = new BasicProperties { Type = messageType, CorrelationId = correlationId };
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
            return new BasicDeliverEventArgs("consumer", 1, redelivered, "e.central", "r.oe.adapter.onmarketchange.00", properties, body);
        }

        private static IConfiguration Config(params (string Key, string Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
                .Build();

        // Short intervals so the watchdog tests finish in milliseconds. Production defaults are 30 s / 2 checks / 60 s.
        private static IConfiguration FastWatchdog(int deadChecks = 2, double statusSeconds = 3600) => Config(
            ("MessageBroker:Queue", "q.test.lookup"),
            ("Custom:ConsumerCheckSeconds", "0.01"),
            ("Custom:ConsumerDeadChecks", deadChecks.ToString()),
            ("Custom:StatusLogSeconds", statusSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        private static Worker BuildWorker(FakeMessageBroker broker, FakeSportsbookRepository? repository = null, ILogger<Worker>? logger = null, IConfiguration? configuration = null, Instrumentation? instrumentation = null) =>
            new(logger ?? NullLogger<Worker>.Instance, broker, repository ?? new FakeSportsbookRepository(), configuration ?? Config(), instrumentation ?? new Instrumentation());

        // StartAsync does not guarantee ExecuteAsync has run (on .NET 10 it does not run inline), so wait until the
        // worker has connected. It subscribes the handler just before Connect(). The watchdog loop keeps running
        // until the worker is stopped or disposed.
        private static async Task<Worker> StartAsync(FakeMessageBroker broker, Worker worker)
        {
            await worker.StartAsync(CancellationToken.None);
            Assert.True(WaitFor(() => broker.ConnectCalls >= 1), "the worker did not connect");
            return worker;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMilliseconds = 5000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        [Fact]
        public async Task ExecuteAsync_ConnectsToBroker()
        {
            var broker = new FakeMessageBroker();
            using var worker = await StartAsync(broker, BuildWorker(broker));

            Assert.Equal(1, broker.ConnectCalls);
        }

        // Connect() starts the consumer, so a message can be delivered before it returns. With the real
        // MessageBroker a delivery that finds MessageReceived null throws and is nacked back onto the queue.
        [Fact]
        public async Task ExecuteAsync_SubscribesBeforeConnecting_SoAnEarlyDeliveryIsNotLost()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            broker.OnConnect = () => broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "EARLY" }));

            using var worker = await StartAsync(broker, BuildWorker(broker, repository));

            Assert.True(WaitFor(() => repository.ProcessedMessages.Count == 1), "the early delivery was dropped");
            Assert.Equal("EARLY", repository.ProcessedMessages[0].PrFiId);
        }

        [Fact]
        public async Task MessageReceived_OnMarketChange_DeserializesAndDispatches()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            using var worker = await StartAsync(broker, BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            var message = Assert.Single(repository.ProcessedMessages);
            Assert.Equal("F1", message.PrFiId);
        }

        [Fact]
        public async Task MessageReceived_UnknownMessageType_IsIgnored()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            using var worker = await StartAsync(broker, BuildWorker(broker, repository));

            broker.Raise(Delivery("SomeUnhandledType", new { }));

            Assert.Empty(repository.ProcessedMessages);
        }

        // An empty payload fails JSON deserialization. The handler catches it, logs it and acks (the broker
        // wrapper acks whenever the handler returns), so the message is dropped, not retried.
        [Fact]
        public async Task MessageReceived_MalformedPayload_IsLoggedAndNotDispatched()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            var logger = new CapturingLogger();
            using var worker = await StartAsync(broker, BuildWorker(broker, repository, logger));

            var properties = new BasicProperties { Type = MessageType.ON_MARKET_CHANGE, CorrelationId = "cid" };
            broker.Raise(new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.test", properties, Array.Empty<byte>()));

            Assert.Empty(repository.ProcessedMessages);
            var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Error processing message"));
            Assert.IsType<JsonException>(error.Exception);
        }

        [Fact]
        public async Task StopAsync_DisposesBroker()
        {
            var broker = new FakeMessageBroker();
            using var worker = await StartAsync(broker, BuildWorker(broker));

            await worker.StopAsync(CancellationToken.None);

            Assert.Equal(1, broker.DisposeCalls);
        }

        [Fact]
        public async Task StopAsync_BrokerDisposeThrows_IsCaughtAndDoesNotPropagate()
        {
            var broker = new FakeMessageBroker { ThrowOnDispose = true };
            using var worker = await StartAsync(broker, BuildWorker(broker));

            await worker.StopAsync(CancellationToken.None); // must not throw

            Assert.Equal(1, broker.DisposeCalls);
        }

        [Fact]
        public async Task StopAsync_StopsTheConsumerMonitorWithoutFailing()
        {
            var broker = new FakeMessageBroker { IsConsuming = true };
            using var worker = await StartAsync(broker, BuildWorker(broker, configuration: FastWatchdog()));

            await worker.StopAsync(CancellationToken.None);

            Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        }

        [Fact]
        public async Task MessageReceived_RepositoryThrows_IsCaughtAndDoesNotCrashDispatch()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnProcess = true };
            using var worker = await StartAsync(broker, BuildWorker(broker, repository));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            // The dispatch loop must survive a repository exception: a later message on the same
            // subscription should still reach the repository rather than the handler having come
            // unsubscribed or the worker having faulted.
            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F2" }));

            Assert.Equal(new[] { "F1", "F2" }, repository.ProcessedMessages.Select(m => m.PrFiId));
        }

        // Before DEVOPS-443 the log line said nothing about which message failed or how long it took, so the
        // 30 s Mongo server-selection timeouts could not be tied to a fixture or a delivery.
        [Fact]
        public async Task MessageReceived_RepositoryThrows_LogsErrorWithMessageContext()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnProcess = true };
            var logger = new CapturingLogger();
            using var worker = await StartAsync(broker, BuildWorker(broker, repository, logger));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F9" }, redelivered: true));

            var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
            Assert.IsType<InvalidOperationException>(error.Exception);
            Assert.Equal(MessageType.ON_MARKET_CHANGE, error.Values["MessageType"]);
            Assert.Equal("F9", error.Values["FixtureId"]);
            Assert.Equal(true, error.Values["Redelivered"]);
            Assert.True(Convert.ToInt64(error.Values["ElapsedMs"]) >= 0);
        }

        [Fact]
        public async Task MessageReceived_SlowMessage_LogsWarning()
        {
            var broker = new FakeMessageBroker();
            var logger = new CapturingLogger();
            var configuration = Config(("Custom:SlowMessageMilliseconds", "0"));
            using var worker = await StartAsync(broker, BuildWorker(broker, logger: logger, configuration: configuration));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
            Assert.Equal("F1", warning.Values["FixtureId"]);
        }

        [Fact]
        public async Task MessageReceived_FastMessage_DoesNotLogSlowWarning()
        {
            var broker = new FakeMessageBroker();
            var logger = new CapturingLogger();
            using var worker = await StartAsync(broker, BuildWorker(broker, logger: logger));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
        }

        // The DEVOPS-443 failure: the broker closed the consumer's channel, the process stayed up consuming
        // nothing, and the queue grew for 26 hours. The host must fail so Kubernetes restarts the pod.
        [Fact]
        public async Task Monitor_ConsumerStaysDown_FailsTheHostAfterTheConfiguredChecks()
        {
            var broker = new FakeMessageBroker { IsConsuming = false };
            var logger = new CapturingLogger();
            using var worker = await StartAsync(broker, BuildWorker(broker, logger: logger, configuration: FastWatchdog(deadChecks: 2)));

            var finished = await Task.WhenAny(worker.ExecuteTask!, Task.Delay(5000));

            Assert.Same(worker.ExecuteTask, finished);
            Assert.True(worker.ConsumerLost);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!);
            Assert.Contains("q.test.lookup", failure.Message);
            Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Warning && e.Message.Contains("is not consuming")));
            var status = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error && e.Message.StartsWith("Lookup consumer status"));
            Assert.Equal(false, status.Values["IsConsuming"]);
        }

        // RabbitMQ.Client re-registers the consumer after a connection recovery, so a short gap must not restart the pod.
        [Fact]
        public async Task Monitor_ConsumerRecoversBeforeTheLimit_KeepsRunning()
        {
            var broker = new FakeMessageBroker { IsConsuming = false };
            var logger = new CapturingLogger();
            using var worker = await StartAsync(broker, BuildWorker(broker, logger: logger, configuration: FastWatchdog(deadChecks: 100)));

            Assert.True(WaitFor(() => logger.Entries.Any(e => e.Message.Contains("is not consuming"))));
            broker.IsConsuming = true;
            await Task.Delay(300);

            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.False(worker.ConsumerLost);
            Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Error);
        }

        [Fact]
        public async Task Monitor_ConsumerHealthy_KeepsRunning()
        {
            var broker = new FakeMessageBroker { IsConsuming = true };
            var logger = new CapturingLogger();
            using var worker = await StartAsync(broker, BuildWorker(broker, logger: logger, configuration: FastWatchdog()));

            await Task.Delay(200);

            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.False(worker.ConsumerLost);
            Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
        }

        [Fact]
        public async Task Monitor_WritesStatusLineWithTheMessageCounts()
        {
            var broker = new FakeMessageBroker { IsConsuming = true };
            var repository = new FakeSportsbookRepository();
            var logger = new CapturingLogger();
            using var worker = await StartAsync(broker, BuildWorker(broker, repository, logger, FastWatchdog(statusSeconds: 0.05)));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));
            repository.ThrowOnProcess = true;
            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F2" }));

            // The counters reset after every status line, so add them up across lines.
            long Sum(string key) => logger.Entries
                .Where(e => e.Message.StartsWith("Lookup consumer status"))
                .Sum(e => Convert.ToInt64(e.Values[key]));

            Assert.True(WaitFor(() => Sum("Received") >= 2));
            Assert.Equal(2, Sum("Received"));
            Assert.Equal(1, Sum("Failed"));
            Assert.All(logger.Entries.Where(e => e.Message.StartsWith("Lookup consumer status")), e => Assert.Equal(true, e.Values["IsConsuming"]));
        }

        // OpenTelemetry, as in the other OE services: a counter by message type (failures count as "error"), a
        // duration histogram and one consumer span per message that continues the publisher's trace.
        [Fact]
        public async Task MessageReceived_CountsProcessedMessagesByType_AndFailuresAsError()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            using var instrumentation = new Instrumentation();
            var counted = new List<(long Value, string? Type)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument, instrumentation.ProcessedMessagesCounter))
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                lock (counted)
                    counted.Add((value, tags.ToArray().Single(t => t.Key == "message.type").Value as string));
            });
            listener.Start();
            using var worker = await StartAsync(broker, BuildWorker(broker, repository, instrumentation: instrumentation));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));
            repository.ThrowOnProcess = true;
            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F2" }));

            Assert.Equal(new (long, string?)[] { (1L, MessageType.ON_MARKET_CHANGE), (1L, "error") }, counted.ToArray());
        }

        [Fact]
        public async Task MessageReceived_RecordsTheHandlerDuration_ForEveryMessage()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository();
            using var instrumentation = new Instrumentation();
            var recorded = new List<(double Value, string? Type)>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (ReferenceEquals(instrument, instrumentation.HandlerDurationMs))
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
            {
                lock (recorded)
                    recorded.Add((value, tags.ToArray().Single(t => t.Key == "message.type").Value as string));
            });
            listener.Start();
            using var worker = await StartAsync(broker, BuildWorker(broker, repository, instrumentation: instrumentation));

            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));
            repository.ThrowOnProcess = true;
            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F2" }));

            Assert.Equal(2, recorded.Count);
            Assert.All(recorded, r =>
            {
                Assert.True(r.Value >= 0);
                Assert.Equal(MessageType.ON_MARKET_CHANGE, r.Type);
            });
        }

        [Fact]
        public async Task MessageReceived_StartsAConsumerSpan_ContinuingTheTraceFromTheMessageHeaders()
        {
            // The default propagator is a no-op until the OpenTelemetry SDK initialises, which AddTelemetry does at
            // host start in production. Set the W3C one here, as the SDK would.
            Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator(new TextMapPropagator[] { new TraceContextPropagator(), new BaggagePropagator() }));

            var broker = new FakeMessageBroker();
            using var instrumentation = new Instrumentation();
            var stopped = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => ReferenceEquals(source, instrumentation.ActivitySource),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    lock (stopped)
                        stopped.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(listener);
            using var worker = await StartAsync(broker, BuildWorker(broker, instrumentation: instrumentation));

            const string traceId = "0af7651916cd43dd8448eb211c80319c";
            const string parentSpanId = "b7ad6b7169203331";
            var properties = new BasicProperties
            {
                Type = MessageType.ON_MARKET_CHANGE,
                CorrelationId = "cid",
                // MessageBroker.Publish injects the context as UTF-8 bytes
                Headers = new Dictionary<string, object> { ["traceparent"] = Encoding.UTF8.GetBytes($"00-{traceId}-{parentSpanId}-01") }
            };
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new OnMarketChangeMessage { PrFiId = "F1" }));
            broker.Raise(new BasicDeliverEventArgs("consumer", 1, false, "e.central", "r.oe.adapter.onmarketchange.00", properties, body));

            var span = Assert.Single(stopped);
            Assert.Equal("r.oe.adapter.onmarketchange.00 receive", span.OperationName);
            Assert.Equal(ActivityKind.Consumer, span.Kind);
            Assert.Equal(traceId, span.TraceId.ToHexString());
            Assert.Equal(parentSpanId, span.ParentSpanId.ToHexString());
            Assert.Equal(MessageType.ON_MARKET_CHANGE, span.GetTagItem("message.type"));
            Assert.Equal(ActivityStatusCode.Ok, span.Status);
        }

        [Fact]
        public async Task MessageReceived_RepositoryThrows_MarksTheSpanAsError()
        {
            var broker = new FakeMessageBroker();
            var repository = new FakeSportsbookRepository { ThrowOnProcess = true };
            using var instrumentation = new Instrumentation();
            var stopped = new List<Activity>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => ReferenceEquals(source, instrumentation.ActivitySource),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStopped = activity =>
                {
                    lock (stopped)
                        stopped.Add(activity);
                }
            };
            ActivitySource.AddActivityListener(listener);
            using var worker = await StartAsync(broker, BuildWorker(broker, repository, instrumentation: instrumentation));

            // no trace headers at all: the span must still be created, as a new root
            broker.Raise(Delivery(MessageType.ON_MARKET_CHANGE, new OnMarketChangeMessage { PrFiId = "F1" }));

            var span = Assert.Single(stopped);
            Assert.Equal(ActivityStatusCode.Error, span.Status);
            Assert.Equal(default, span.ParentSpanId);
        }

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

        private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message, IReadOnlyDictionary<string, object?> Values);

        // Keeps each entry's structured values (the template placeholders) so tests can assert on them.
        private sealed class CapturingLogger : ILogger<Worker>
        {
            private readonly object _lock = new();
            private readonly List<LogEntry> _entries = new();

            public IReadOnlyList<LogEntry> Entries
            {
                get
                {
                    lock (_lock)
                        return _entries.ToArray();
                }
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = (state as IEnumerable<KeyValuePair<string, object?>>)?.ToDictionary(p => p.Key, p => p.Value)
                    ?? new Dictionary<string, object?>();

                lock (_lock)
                    _entries.Add(new LogEntry(logLevel, exception, formatter(state, exception), values));
            }
        }
    }
}
