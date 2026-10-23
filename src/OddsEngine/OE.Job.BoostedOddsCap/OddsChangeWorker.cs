using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.BoostedOddsCap.Alerts;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Job.BoostedOddsCap.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;
using RabbitMQ.Client.Events;

namespace OE.Job.BoostedOddsCap
{
    // Listens to every odds change the Sportsbook shards publish (r.oe.sb.engine.onoddschange) and hands the
    // few that belong to a boosted fixture to the evaluator. This is the hot path: a message for a fixture with
    // no boost costs a short scan of its header and a dictionary lookup, and nothing here touches the database,
    // the network or a lock. Alerts are queued for another thread to deliver.
    //
    // The queue is exclusive and auto-deleting (MessageBroker:IsExclusive), so a stopped job leaves no backlog
    // piling up on the shared broker and never replays stale odds. MessageBroker acknowledges after the handler
    // returns and requeues on an exception, so the handler must never throw: a message that threw would be
    // redelivered forever.
    public class OddsChangeWorker : BackgroundService
    {
        private static readonly long ErrorLogIntervalTicks = Stopwatch.Frequency * 10;

        private readonly ILogger<OddsChangeWorker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly IConfiguration _configuration;
        private readonly BoostSnapshotHolder _holder;
        private readonly OddsChangeEvaluator _evaluator;
        private readonly CapAlertTracker _tracker;
        private readonly AlertQueue _queue;
        private readonly Instrumentation _instrumentation;

        private long _lastErrorLogTimestamp;
        private long _received;
        private long _matched;
        private long _failed;
        private long _alerts;

        public OddsChangeWorker(ILogger<OddsChangeWorker> logger, IMessageBroker messageBroker, IConfiguration configuration,
            BoostSnapshotHolder holder, OddsChangeEvaluator evaluator, CapAlertTracker tracker, AlertQueue queue,
            Instrumentation instrumentation)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _configuration = configuration;
            _holder = holder;
            _evaluator = evaluator;
            _tracker = tracker;
            _queue = queue;
            _instrumentation = instrumentation;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _messageBroker.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping OddsChangeWorker");
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue<bool>("Custom:Enabled", false))
                return;

            // A message judged against an empty index could never alert, so wait for the first load.
            _logger.LogInformation("Waiting for the first boost index load before consuming odds changes");
            await _holder.FirstLoad.WaitAsync(stoppingToken);

            // Boosts already inside their cap when the job starts are recorded without alerting for this long,
            // as the job cannot know whether it alerted for them before it was restarted.
            var silentSeconds = _configuration.GetValue<int>("Custom:StartupSilentSeconds", 120);
            _tracker.StartSilentWindow(DateTime.UtcNow.AddSeconds(silentSeconds));

            _messageBroker.MessageReceived += MessageReceivedEvent;

            try
            {
                _messageBroker.Connect();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not connect to RabbitMQ");
                throw; // up to the host, so the pod fails early and restarts
            }

            _logger.LogInformation("Consuming odds changes from queue {Queue}. Alerts for boosts already inside their cap are held back for {StartupSilentSeconds}s",
                    _configuration["MessageBroker:Queue"], silentSeconds);

            await MonitorConsumerAsync(stoppingToken);
        }

        // RabbitMQ.Client only recovers from a lost connection. A channel the broker closes or a consumer it cancels
        // is not recovered, and the process would stay up consuming nothing, which for an alerting job means no
        // alerts and no sign of it (docs/TechnicalDebt.md H9). IsConsuming is false in those cases and true again
        // once automatic recovery re-registers the consumer, so only a consumer that stays down for several checks
        // fails the host, and the pod restarts. Same approach as OE.Job.Lookup (DEVOPS-443).
        private async Task MonitorConsumerAsync(CancellationToken stoppingToken)
        {
            var checkInterval = TimeSpan.FromSeconds(Math.Max(0.01, _configuration.GetValue<double>("Custom:ConsumerCheckSeconds", 30)));
            var deadChecks = Math.Max(1, _configuration.GetValue<int>("Custom:ConsumerDeadChecks", 2));
            var statusInterval = TimeSpan.FromSeconds(Math.Max(1, _configuration.GetValue<double>("Custom:StatusLogSeconds", 60)));

            var misses = 0;
            var nextStatusUtc = DateTime.UtcNow + statusInterval;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(checkInterval, stoppingToken);
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
                            _configuration["MessageBroker:Queue"], misses, deadChecks);

                    if (misses >= deadChecks)
                    {
                        LogStatus(false, LogLevel.Error);
                        throw new InvalidOperationException(
                            $"RabbitMQ consumer on queue {_configuration["MessageBroker:Queue"]} stopped and did not recover; failing so the pod restarts and consumes again");
                    }
                }

                if (DateTime.UtcNow >= nextStatusUtc)
                {
                    LogStatus(_messageBroker.IsConsuming, LogLevel.Information);
                    nextStatusUtc = DateTime.UtcNow + statusInterval;
                }
            }
        }

        // One line per interval: a missing line means the process is gone, Received not moving while the market is
        // open means the consumer is stuck, and IsConsuming=False means it was lost.
        private void LogStatus(bool isConsuming, LogLevel level)
        {
            var snapshot = _holder.Current;

            _logger.Log(level,
                    "Boosted odds cap status: IsConsuming={IsConsuming} Received={Received} Matched={Matched} Alerts={Alerts} Failed={Failed} Boosts={Boosts} Fixtures={Fixtures} IndexAgeSeconds={IndexAgeSeconds}",
                    isConsuming, Interlocked.Read(ref _received), Interlocked.Read(ref _matched), Interlocked.Read(ref _alerts),
                    Interlocked.Read(ref _failed), snapshot.RuleCount, snapshot.FixtureCount,
                    (long)(DateTime.UtcNow - snapshot.LoadedAtUtc).TotalSeconds);
        }

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs e)
        {
            try
            {
                if (!string.Equals(e.BasicProperties?.Type, MessageType.ON_ODDS_CHANGE, StringComparison.OrdinalIgnoreCase))
                    return;

                _instrumentation.MessagesReceived.Add(1);
                Interlocked.Increment(ref _received);

                var body = e.Body;
                if (body == null || !OddsChangeHeaderScanner.TryRead(body, out var providerFixtureId, out var fixtureStatusId))
                    return;

                if (fixtureStatusId != -1 && fixtureStatusId != (int)FixtureStatus.Prematch)
                    return;

                if (!_holder.Current.TryGetRules(providerFixtureId, out var rules))
                    return;

                var started = Stopwatch.GetTimestamp();
                _instrumentation.MessagesMatched.Add(1);
                Interlocked.Increment(ref _matched);

                var message = JsonSerializer.Deserialize<OnInternalOddsChangeMessage>(body);

                foreach (var alert in _evaluator.Evaluate(message, rules, DateTime.UtcNow))
                {
                    _queue.Enqueue(alert);
                    _instrumentation.AlertsRaised.Add(1);
                    Interlocked.Increment(ref _alerts);
                }

                if (_instrumentation.EvaluationDuration.Enabled)
                    _instrumentation.EvaluationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _instrumentation.EvaluationErrors.Add(1);
                Interlocked.Increment(ref _failed);

                // a systematic failure would otherwise log once per odds message
                var now = Stopwatch.GetTimestamp();
                if (now - Interlocked.Read(ref _lastErrorLogTimestamp) >= ErrorLogIntervalTicks)
                {
                    Interlocked.Exchange(ref _lastErrorLogTimestamp, now);
                    _logger.LogError(ex, "Failed to evaluate an odds change message. Further failures in the next 10 seconds are only counted");
                }
            }
        }
    }
}
