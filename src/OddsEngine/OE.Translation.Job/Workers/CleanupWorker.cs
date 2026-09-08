using OE.Translation.Job.Repositories;

namespace OE.Translation.Job.Workers
{
    // Periodically removes stale, unflagged TranslationCache2 rows. Rows the feed still emits
    // are kept fresh by the seeding path's liveness touch (UPDATED_AT); rows flagged
    // HAS_BET_PLACEMENT = 1 by the BetPlacementWorker are always preserved.
    public class CleanupWorker : BackgroundService
    {
        private readonly ILogger<CleanupWorker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;

        public CleanupWorker(
            ILogger<CleanupWorker> logger,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue("Custom:Cleanup:Enabled", true);

            if (!enabled)
            {
                _logger.LogInformation("Translation cleanup is disabled (Custom:Cleanup:Enabled = false)");
                return;
            }

            var intervalMinutes = _configuration.GetValue("Custom:Cleanup:IntervalMinutes", 60);
            var startupDelaySeconds = _configuration.GetValue("Custom:Cleanup:StartupDelaySeconds", 60);

            if (startupDelaySeconds > 0)
                await Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)));

            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (!stoppingToken.IsCancellationRequested
                   && await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            try
            {
                var retentionDays = _configuration.GetValue("Custom:Cleanup:RetentionDays", 30);
                var batchSize = _configuration.GetValue("Custom:Cleanup:BatchSize", 2000);
                var prefixes = (_configuration.GetValue("Custom:Cleanup:Prefixes", "MaTe:,SeTe:,Te:,SpId:,SegId:,LeId:") ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                using var scope = _scopeFactory.CreateScope();

                var repository = scope.ServiceProvider
                    .GetRequiredService<ITranslationCleanupRepository>();

                var deleted = await repository.CleanupAsync(prefixes, retentionDays, batchSize, stoppingToken);

                _logger.LogInformation(
                    "TranslationCache2 cleanup removed {Count} stale rows (retentionDays={Days}, prefixes={Prefixes})",
                    deleted, retentionDays, string.Join(",", prefixes));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down — expected.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Translation cleanup run failed");
            }
        }

        private static async Task Delay(TimeSpan delay, CancellationToken token)
        {
            try
            {
                await Task.Delay(delay, token);
            }
            catch (OperationCanceledException)
            {
                // Shutting down during the startup delay.
            }
        }
    }
}
