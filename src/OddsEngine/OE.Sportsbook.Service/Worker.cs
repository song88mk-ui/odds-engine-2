using Microsoft.Extensions.Hosting;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OE.Sportsbook.Service
{
    public class Worker : BackgroundService
    {
        #region Fields

        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;

        #endregion

        #region Ctor

        public Worker(IMessageBroker messageBroker, ISportsbookRepository sportsbookRepository)
        {
            _messageBroker = messageBroker;
            _sportsbookRepository = sportsbookRepository;
        }

        #endregion

        #region Methods

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _messageBroker.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _messageBroker.Connect();
            _messageBroker.MessageReceived += MessageReceivedEvent;
        }

        #endregion

        #region Utilities

        private void MessageReceivedEvent(object? sender, BasicDeliverEventArgs ea)
        {
            try
            {
                var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
                var messageType = ea.BasicProperties.Type;
                var correlationId = ea.BasicProperties.CorrelationId;

                switch (messageType)
                {
                    case MessageType.ON_FIXTURE_CHANGE:
                        ProcessMessage<OnFixtureChangeMessage>(
                            payload,
                            (msg) => _sportsbookRepository.ProcessOnFixtureChangeMessage(_messageBroker, msg, correlationId)
                        ); 
                        break;
                    case MessageType.ON_MARKET_CHANGE:
                        ProcessMessage<OnMarketChangeMessage>(
                            payload,
                            (msg) => _sportsbookRepository.ProcessOnMarketChangeMessage(_messageBroker, msg, correlationId)
                        );
                        break;
                    case MessageType.ON_MARKET_SETTLEMENT:
                        ProcessMessage<OnSettlementChangeMessage>(
                            payload,
                            (msg) => _sportsbookRepository.ProcessOnSettlementMessage(_messageBroker, msg, correlationId)
                        );
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }
        private void ProcessMessage<T>(string payload, Action<T> processor)
        {
            if (string.IsNullOrWhiteSpace(payload)) return;

            var message = JsonSerializer.Deserialize<T>(payload);
            if (message != null)
            {
                processor(message);
            }
        }

        #endregion
    }
}
