using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Shared.Utilities;
using OE.Shared.Extensions;
using OE.Job.KeepAlive.Messages;
using System;
using System.Threading;
using System.Threading.Tasks;


namespace OE.Job.KeepAlive
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;

        public Worker(ILogger<Worker> logger, IMessageBroker messageBroker)
        {
            _logger = logger;
            _messageBroker = messageBroker;
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Connecting to RabbitMQ Server");
            _messageBroker.Connect();
            _logger.LogInformation("Connected, running...");

            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            _messageBroker.Dispose();
            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Running Heartbeat publish loop()");
            while (true)
            {
                try
                {
                    var timestamp = DateTime.UtcNow.ToEpochTimestamp();

                    var payload = new OnHeartbeatMessage
                    {
                        IsHealthy = true,
                        Timestamp = timestamp
                    };

                    _logger.LogDebug("sending payload: [{payload}]", payload);

                    _messageBroker.Publish(payload, "OnHeartbeat", "r.oe.uof.heartbeat", timestamp.ToString());
                    await Task.Delay(5000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error: {ex.Message}");
                    await Task.Delay(5000, stoppingToken);
                }
            }
        }
    }
}
