using OE.Job.Settlement.Repository;
using OE.Shared.Utilities;

namespace OE.Job.Settlement
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;

        public Worker(ILogger<Worker> logger, ISportsbookRepository sportsbookRepository, IMessageBroker messageBroker)
        {
            _logger = logger;
            _sportsbookRepository = sportsbookRepository;
            _messageBroker = messageBroker;
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
                try
                {
                    _logger.LogInformation("Processing settlements");
                    await _sportsbookRepository.ProcessSettlement(_messageBroker);
                    _logger.LogInformation("Done at: {time}", DateTimeOffset.Now);
                    await Task.Delay(300000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error: {ex.Message}");
                    await Task.Delay(300000, stoppingToken);
                }
            }
        }
    }
}
