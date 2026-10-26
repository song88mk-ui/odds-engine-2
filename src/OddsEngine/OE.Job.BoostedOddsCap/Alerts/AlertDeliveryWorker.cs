using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.BoostedOddsCap.Evaluation;

namespace OE.Job.BoostedOddsCap.Alerts
{
    // Drains the alert queue on its own thread, away from the odds consumer. After the first alert arrives it
    // waits a short window so a burst (many boosts crossing at once) is delivered as one batch, one summary toast
    // and one digest email per recipient instead of one of each per boost.
    public class AlertDeliveryWorker : BackgroundService
    {
        private const int MaxBatchSize = 500;

        private readonly ILogger<AlertDeliveryWorker> _logger;
        private readonly IConfiguration _configuration;
        private readonly AlertQueue _queue;
        private readonly AlertDispatcher _dispatcher;

        public AlertDeliveryWorker(ILogger<AlertDeliveryWorker> logger, IConfiguration configuration, AlertQueue queue,
            AlertDispatcher dispatcher)
        {
            _logger = logger;
            _configuration = configuration;
            _queue = queue;
            _dispatcher = dispatcher;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue<bool>("Custom:Enabled", false))
                return;

            var window = TimeSpan.FromSeconds(Math.Max(0, _configuration.GetValue<int>("Custom:BatchWindowSeconds", 5)));
            _logger.LogInformation("Price boost cap alert delivery started. Batch window {BatchWindowSeconds}s",
                    window.TotalSeconds);

            try
            {
                while (await _queue.Reader.WaitToReadAsync(stoppingToken))
                {
                    if (window > TimeSpan.Zero)
                        await Task.Delay(window, stoppingToken);

                    var batch = new List<CapAlert>();
                    while (batch.Count < MaxBatchSize && _queue.Reader.TryRead(out var alert))
                        batch.Add(alert);

                    if (batch.Count == 0)
                        continue;

                    try
                    {
                        await _dispatcher.DispatchAsync(batch, DateTime.UtcNow, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to deliver {Count} price boost cap alerts", batch.Count);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // stopping
            }
        }
    }
}
