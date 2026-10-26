using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Job.BoostedOddsCap.Telemetry;

namespace OE.Job.BoostedOddsCap.Boosts
{
    // Reloads the boost index from the Trading database on a timer. The odds consumer never queries the
    // database: it reads the snapshot this worker publishes. The three tables are small, so a periodic full
    // reload is cheaper and simpler than tracking changes, at the price of a boost created or edited in
    // Backoffice taking up to Custom:RefreshSeconds to be seen. A failed reload keeps the previous snapshot.
    public class BoostRefreshWorker : BackgroundService
    {
        private readonly ILogger<BoostRefreshWorker> _logger;
        private readonly IConfiguration _configuration;
        private readonly IBoostSource _source;
        private readonly BoostSnapshotHolder _holder;
        private readonly CapAlertTracker _tracker;
        private readonly Instrumentation _instrumentation;

        public BoostRefreshWorker(ILogger<BoostRefreshWorker> logger, IConfiguration configuration, IBoostSource source,
            BoostSnapshotHolder holder, CapAlertTracker tracker, Instrumentation instrumentation)
        {
            _logger = logger;
            _configuration = configuration;
            _source = source;
            _holder = holder;
            _tracker = tracker;
            _instrumentation = instrumentation;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue<bool>("Custom:Enabled", false))
            {
                _logger.LogWarning("Boosted odds cap is disabled (Custom:Enabled is false), the boost index is not loaded");
                return;
            }

            var interval = TimeSpan.FromSeconds(Math.Max(5, _configuration.GetValue<int>("Custom:RefreshSeconds", 60)));
            _logger.LogInformation("Boost index refresh started. Interval {RefreshSeconds}s", interval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var snapshot = await _source.LoadAsync(stoppingToken);
                    _holder.Set(snapshot);
                    _tracker.Retain(snapshot.RuleKeys);

                    _instrumentation.IndexRules.Record(snapshot.RuleCount);
                    _instrumentation.IndexFixtures.Record(snapshot.FixtureCount);
                    _instrumentation.IndexLastRefreshTimestampSeconds.Record(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

                    _logger.LogInformation(
                            "Boost index refreshed. Boosts {RuleCount}, fixtures {FixtureCount}, boosts without a cap {RowsWithoutCap}",
                            snapshot.RuleCount, snapshot.FixtureCount, snapshot.RowsWithoutCap);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _instrumentation.IndexRefreshFailures.Add(1);
                    _logger.LogError(ex, "Boost index refresh failed, keeping the previous index");
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
