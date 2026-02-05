using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.Cleanup.Telemetry;
using OE.Job.Cleanup.Repository;
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;


namespace OE.Job.Cleanup
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ISportsbookRepository _sportsbookRepository;
        private readonly IConfiguration _configuration;

        private readonly ActivitySource _activitySource;
        private readonly Counter<long> _jobLastExecutionTimestampSeconds;
        private readonly Gauge<short> _jobSuccessResult;
        private readonly Counter<long> _jobRunTotal;

        public Worker(ILogger<Worker> logger, ISportsbookRepository sportsbookRepository, IConfiguration configuration, Instrumentation instrumentation)
        {
            _logger = logger;
            _configuration = configuration;
            _sportsbookRepository = sportsbookRepository;

            _activitySource = instrumentation.ActivitySource;
            _jobLastExecutionTimestampSeconds = instrumentation.JobLastExecutionTimestampSeconds;
            _jobSuccessResult = instrumentation.JobSuccessResult;
            _jobRunTotal = instrumentation.JobRunTotal;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var startHour = _configuration.GetValue<uint>("Custom:UtcWindowHours:Start", 1);
            var endHour = _configuration.GetValue<uint>("Custom:UtcWindowHours:End", 10);

            _logger.LogInformation("Running OE CleanUp Crew. Schedule is from {start}-{end} UTC Hours",
                    startHour, endHour);
            while (true)
            {
                try
                {
                    // mark the counter as executed to identify the app is running
                    if (_jobLastExecutionTimestampSeconds.Enabled)
                        _jobLastExecutionTimestampSeconds.Add(DateTimeOffset.Now.ToUnixTimeSeconds());

                    if (!_configuration.GetValue<bool>("Custom:Enabled", false))
                        throw new InvalidOperationException("Work is disabled! Check if the crew is allowed to work.");

                    var time = DateTime.UtcNow.TimeOfDay;
                    if (time.Hours >= startHour && time.Hours < endHour)
                    {
                        _logger.LogInformation("Dispatching work now.");
                        if (_jobRunTotal.Enabled)
                            _jobRunTotal.Add(1);

                        await _sportsbookRepository.Cleanup();

                        if (_jobSuccessResult.Enabled)
                            _jobSuccessResult.Record(1);

                        _logger.LogInformation("task complete, resting a bit");
                    }
                    else
                    {
                        _logger.LogInformation("Skipping work. No task is scheduled at the moment");
                    }

                    await Task.Delay(300000, stoppingToken);
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
