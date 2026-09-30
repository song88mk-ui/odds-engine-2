using OE.Job.LiveStream.Telemetry;
using OE.Job.LiveStream.Repository;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.LiveStream
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ISportsbookRepository _sportsbookRepository;
        private readonly IConfiguration _configuration;

        private readonly Gauge<long> _jobLastExecutionTimestampSeconds;
        private readonly Gauge<short> _jobSuccessResult;
        private readonly Counter<long> _jobRunCount;

        public Worker(ILogger<Worker> logger, ISportsbookRepository sportsbookRepository, IConfiguration configuration, Instrumentation instrumentation)
        {
            _logger = logger;
            _configuration = configuration;
            _sportsbookRepository = sportsbookRepository;

            _jobLastExecutionTimestampSeconds = instrumentation.JobLastExecutionTimestampSeconds;
            _jobSuccessResult = instrumentation.JobSuccessResult;
            _jobRunCount = instrumentation.JobRunCount;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Running OE Job LiveStream...");

            while (true)
            {
                try
                {
                    // mark the counter as executed to identify the app is running
                    if (_jobLastExecutionTimestampSeconds.Enabled)
                        _jobLastExecutionTimestampSeconds.Record(DateTimeOffset.Now.ToUnixTimeSeconds());

                    if (!_configuration.GetValue<bool>("Custom:Enabled", false))
                        throw new InvalidOperationException("Work is disabled! Check if the crew is allowed to work.");

                    _logger.LogInformation("Dispatching work now.");
                    if (_jobRunCount.Enabled)
                        _jobRunCount.Add(1);

                    await _sportsbookRepository.ProcessLiveStream();

                    if (_jobSuccessResult.Enabled)
                        _jobSuccessResult.Record(1);

                    _logger.LogInformation("task complete, resting a bit");

                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (InvalidOperationException e)
                {
                    _logger.LogError("The Job failed: {message}", e.Message);
                    await Task.Delay(300000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Unknown error: {exception}", ex);
                    if (_jobSuccessResult.Enabled)
                        _jobSuccessResult.Record(0);

                    await Task.Delay(300000, stoppingToken);
                }
            }
        }
    }
}
