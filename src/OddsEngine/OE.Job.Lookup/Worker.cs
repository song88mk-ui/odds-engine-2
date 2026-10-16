using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.Lookup.Repository;
using OE.Job.Lookup.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Job.Lookup
{
    public class Worker : BackgroundService
    {
        private const double DefaultConsumerCheckSeconds = 30;
        private const int DefaultConsumerDeadChecks = 2;
        private const double DefaultStatusLogSeconds = 60;
        private const long DefaultSlowMessageMilliseconds = 5000;

        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;
        private readonly IConfiguration _configuration;
        private readonly ActivitySource _activitySource;
        private readonly Counter<long> _processedMessagesCounter;
        private readonly Histogram<double> _handlerDurationMs;

        private readonly TimeSpan _consumerCheckInterval;
        private readonly int _consumerDeadChecks;
        private readonly TimeSpan _statusLogInterval;
        private readonly long _slowMessageMilliseconds;

        // Handler counters since the last status line. The handler runs on the RabbitMQ consumer thread
        // and the status line is written from ExecuteAsync, so both go through the lock.
        private readonly object _statsLock = new object();
        private long _received;
        private long _failed;
        private long _handlerMilliseconds;
        private long _maxHandlerMilliseconds;
        private DateTime _lastReceivedUtc = DateTime.MinValue;

        // True once the consumer watchdog gave up. Program reads it to exit non-zero: the host itself exits 0
        // after a failed BackgroundService, which Kubernetes would record as Completed instead of Error.
        public bool ConsumerLost { get; private set; }

        public Worker(ILogger<Worker> logger, IMessageBroker messageBroker, ISportsbookRepository sportsbookRepository, IConfiguration configuration, Instrumentation instrumentation)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _sportsbookRepository = sportsbookRepository;
            _configuration = configuration;
            _activitySource = instrumentation.ActivitySource;
            _processedMessagesCounter = instrumentation.ProcessedMessagesCounter;
            _handlerDurationMs = instrumentation.HandlerDurationMs;

            _consumerCheckInterval = TimeSpan.FromSeconds(PositiveOrDefault(configuration.GetValue<double>("Custom:ConsumerCheckSeconds"), DefaultConsumerCheckSeconds));
            _consumerDeadChecks = (int)PositiveOrDefault(configuration.GetValue<int>("Custom:ConsumerDeadChecks"), DefaultConsumerDeadChecks);
            _statusLogInterval = TimeSpan.FromSeconds(PositiveOrDefault(configuration.GetValue<double>("Custom:StatusLogSeconds"), DefaultStatusLogSeconds));
            _slowMessageMilliseconds = configuration.GetValue<long>("Custom:SlowMessageMilliseconds", DefaultSlowMessageMilliseconds);
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _messageBroker.Dispose();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error stopping service: {Message}", e.Message);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Subscribe before connecting: Connect() starts the consumer, and a delivery that arrives while
            // MessageReceived is still null throws inside the broker wrapper and is nacked back onto the queue.
            // A restart with a deep backlog hits that on its first deliveries.
            _messageBroker.MessageReceived += MessageReceivedEvent;

            try
            {
                _logger.LogInformation("Connecting to RabbitMQ Server");
                _messageBroker.Connect();
                _logger.LogInformation("Connected. Processing Incoming messages from queue {Queue} with prefetch {PrefetchCount}",
                    _configuration["MessageBroker:Queue"], _configuration["MessageBroker:PrefetchCount"] ?? "unset");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error running service: {Message}", e.Message);
                throw; // up to stack, so it can fail early
            }

            await MonitorConsumerAsync(stoppingToken);
        }

        // RabbitMQ.Client only recovers from a lost connection. A channel the broker closes (for example the
        // 406 delivery acknowledgement timeout) or a consumer the broker cancels (the queue was deleted) is not
        // recovered, and the process would stay up consuming nothing, invisible to Kubernetes (DEVOPS-443).
        // IsConsuming is false in all of those cases and true again once automatic recovery re-registers the
        // consumer, so only a consumer that stays down for several checks fails the host, and the pod restarts.
        private async Task MonitorConsumerAsync(CancellationToken stoppingToken)
        {
            var misses = 0;
            var nextStatusUtc = DateTime.UtcNow + _statusLogInterval;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_consumerCheckInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (_messageBroker.IsConsuming)
                {
                    misses = 0;
                }
                else
                {
                    misses++;
                    _logger.LogWarning("RabbitMQ consumer on queue {Queue} is not consuming, check {Misses} of {Limit}",
                        _configuration["MessageBroker:Queue"], misses, _consumerDeadChecks);

                    if (misses >= _consumerDeadChecks)
                    {
                        ConsumerLost = true;
                        LogStatus(false, LogLevel.Error);
                        throw new InvalidOperationException(
                            $"RabbitMQ consumer on queue {_configuration["MessageBroker:Queue"]} stopped and did not recover; failing so the pod restarts and consumes again");
                    }
                }

                if (DateTime.UtcNow >= nextStatusUtc)
                {
                    LogStatus(_messageBroker.IsConsuming, LogLevel.Information);
                    nextStatusUtc = DateTime.UtcNow + _statusLogInterval;
                }
            }
        }

        // One line per interval. A missing line means the process is gone, Received=0 for several lines while the
        // queue is growing means the consumer is stuck, and IsConsuming=False shows the consumer was lost.
        private void LogStatus(bool isConsuming, LogLevel level)
        {
            long received, failed, averageMilliseconds, maxMilliseconds;
            double? lastMessageAgoSeconds;

            lock (_statsLock)
            {
                received = _received;
                failed = _failed;
                averageMilliseconds = received == 0 ? 0 : _handlerMilliseconds / received;
                maxMilliseconds = _maxHandlerMilliseconds;
                lastMessageAgoSeconds = _lastReceivedUtc == DateTime.MinValue
                    ? (double?)null
                    : Math.Round((DateTime.UtcNow - _lastReceivedUtc).TotalSeconds, 1);

                _received = 0;
                _failed = 0;
                _handlerMilliseconds = 0;
                _maxHandlerMilliseconds = 0;
            }

            _logger.Log(level,
                "Lookup consumer status: IsConsuming={IsConsuming} Received={Received} Failed={Failed} AvgMs={AvgMs} MaxMs={MaxMs} LastMessageAgoSeconds={LastMessageAgoSeconds}",
                isConsuming, received, failed, averageMilliseconds, maxMilliseconds, lastMessageAgoSeconds);
        }

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs e)
        {
            // Read the propagator per message, not into a static field: the default is a no-op until the
            // OpenTelemetry SDK initialises at host start, so a value cached earlier would never link the trace.
            var parentContext = Propagators.DefaultTextMapPropagator.Extract(default, e.BasicProperties, ExtractTraceFromBasicProperties);
            Baggage.Current = parentContext.Baggage;

            var payload = Encoding.UTF8.GetString(e.Body);
            var messageType = e.BasicProperties.Type;
            var fixtureId = string.Empty;
            var stopwatch = Stopwatch.StartNew();
            var failed = false;

            using (var activity = _activitySource.StartActivity($"{e.RoutingKey} receive", ActivityKind.Consumer, parentContext.ActivityContext))
            {
                activity?.SetTag("message.type", messageType);

                try
                {
                    switch (messageType)
                    {
                        case MessageType.ON_MARKET_CHANGE:
                            var oddschange = JsonSerializer.Deserialize<OnMarketChangeMessage>(payload);
                            fixtureId = oddschange?.PrFiId;

                            _sportsbookRepository.ProcessOnOddsChangeMessage(oddschange);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    failed = true;
                    stopwatch.Stop();
                    _logger.LogError(ex, "Error processing message {MessageType} for fixture {FixtureId}, redelivered {Redelivered}, after {ElapsedMs} ms",
                        messageType, fixtureId, e.Redelivered, stopwatch.ElapsedMilliseconds);
                }

                activity?.SetStatus(failed ? ActivityStatusCode.Error : ActivityStatusCode.Ok);
            }

            stopwatch.Stop();
            var elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            RecordMessage(failed, elapsedMilliseconds);

            if (_processedMessagesCounter.Enabled)
                _processedMessagesCounter.Add(1, new KeyValuePair<string, object>("message.type", failed ? "error" : messageType));

            if (_handlerDurationMs.Enabled)
                _handlerDurationMs.Record(elapsedMilliseconds, new KeyValuePair<string, object>("message.type", messageType));

            if (!failed && elapsedMilliseconds >= _slowMessageMilliseconds)
                _logger.LogWarning("Slow message {MessageType} for fixture {FixtureId} took {ElapsedMs} ms", messageType, fixtureId, elapsedMilliseconds);
        }

        private void RecordMessage(bool failed, long elapsedMilliseconds)
        {
            lock (_statsLock)
            {
                _received++;
                if (failed)
                    _failed++;
                _handlerMilliseconds += elapsedMilliseconds;
                if (elapsedMilliseconds > _maxHandlerMilliseconds)
                    _maxHandlerMilliseconds = elapsedMilliseconds;
                _lastReceivedUtc = DateTime.UtcNow;
            }
        }

        // The publisher injects the trace context into the message headers as UTF-8 bytes (MessageBroker.Publish).
        private static IEnumerable<string> ExtractTraceFromBasicProperties(IBasicProperties props, string key)
        {
            if (props.Headers != null && props.Headers.TryGetValue(key, out var value) && value is byte[] bytes)
                return new[] { Encoding.UTF8.GetString(bytes) };

            return Array.Empty<string>();
        }

        private static double PositiveOrDefault(double value, double fallback) => value > 0 ? value : fallback;
    }
}
