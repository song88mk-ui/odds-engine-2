using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.Lookup.Repository;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using RabbitMQ.Client.Events;

namespace OE.Job.Lookup
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;

        public Worker(ILogger<Worker> logger, IMessageBroker messageBroker, ISportsbookRepository sportsbookRepository)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _sportsbookRepository = sportsbookRepository;
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
                _logger.LogError(e, "Error stopping service: {message}", e.Message);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Connecting to RabbitMQ Server");
                _messageBroker.Connect();
                _logger.LogInformation("Connected. Processing Incoming messages");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error running service: {message}", e.Message);
                throw; // up to stack, so it can fail early
            }
            _messageBroker.MessageReceived += MessageReceivedEvent;
        }

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs e)
        {
            var payload = Encoding.UTF8.GetString(e.Body);
            var messageType = e.BasicProperties.Type;
            var correlationId = e.BasicProperties.CorrelationId;

            try
            {
                switch (messageType)
                {
                    case MessageType.ON_MARKET_CHANGE:
                        var oddschange = JsonSerializer.Deserialize<OnMarketChangeMessage>(payload);

                        _sportsbookRepository.ProcessOnOddsChangeMessage(oddschange);
                        break;
                }
            }
            catch (Exception)
            {

            }
        }
    }
}
