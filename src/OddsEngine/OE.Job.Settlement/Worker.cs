using OE.Job.Settlement.Repository;
using OE.Job.Settlement.Telemetry;
using OE.Shared.Utilities;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.Settlement
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;

        private readonly ActivitySource _activitySource;
        private readonly Histogram<double> _jobDurationMs;
        private readonly Gauge<long> _jobLastExecutionTimestampSeconds;
        private readonly Gauge<short> _jobSuccessResult;
        private readonly Counter<long> _jobRunCount;

        public Worker(ILogger<Worker> logger, ISportsbookRepository sportsbookRepository, IMessageBroker messageBroker, Instrumentation instrumentation)
        {
            _logger = logger;
            _sportsbookRepository = sportsbookRepository;
            _messageBroker = messageBroker;
            _activitySource = instrumentation.ActivitySource;
            _jobDurationMs = instrumentation.JobDurationMs;
            _jobLastExecutionTimestampSeconds = instrumentation.JobLastExecutionTimestampSeconds;
            _jobSuccessResult = instrumentation.JobSuccessResult;
            _jobRunCount = instrumentation.JobRunCount;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting worker, connecting to message broker");
            _messageBroker.Connect();
            _logger.LogInformation("Connected.");

            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping worker");
            _messageBroker.Dispose();
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    _logger.LogInformation("Processing settlements");
                    if (_jobRunCount.Enabled)
                        _jobRunCount.Add(1);

                    await _sportsbookRepository.ProcessSettlement(_messageBroker);

                    if (_jobSuccessResult.Enabled)
                    {
                        _jobSuccessResult.Record(1);
                        _jobDurationMs.Record(sw.Elapsed.TotalMilliseconds);
                        _jobLastExecutionTimestampSeconds.Record(DateTimeOffset.Now.ToUnixTimeSeconds());
                    }

                    _logger.LogInformation("Done at: {time}", DateTimeOffset.Now);
                    await Task.Delay(300000, stoppingToken);
                }
                catch (Exception ex)
                {
                    if (_jobSuccessResult.Enabled)
                            _jobSuccessResult.Record(0);

                    _logger.LogError($"Error: {ex.Message}");
                    await Task.Delay(300000, stoppingToken);
                }
            }
        }
    }
}
