using Microsoft.Extensions.Hosting;
using OE.Adapter.Service.Repositories;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Utilities;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OE.Adapter.Service
{
    public class Worker : BackgroundService
    {
        #region Fields

        private readonly IMessageBroker _messageBroker;
        private readonly ITradingRepository _tradingRepository;

        #endregion

        #region Ctor

        public Worker(IMessageBroker messageBroker, ITradingRepository tradingRepository)
        {
            _messageBroker = messageBroker;
            _tradingRepository = tradingRepository;
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

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs ea)
        {
            try
            {
                var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
                var messageType = ea.BasicProperties.Type;
                var correlationId = ea.BasicProperties.CorrelationId;

                switch (messageType)
                {
                    case MessageType.ON_FIXTURE_SET:
                        var onFixtureSetMessage = JsonSerializer.Deserialize<OnFixtureSetMessage>(payload);
                        _tradingRepository.ProcessOnFixtureSetMessage(_messageBroker, onFixtureSetMessage, correlationId);
                        break;
                    case MessageType.ON_MARKET_SET:
                        var onMarketSetMessage = JsonSerializer.Deserialize<OnMarketSetMessage>(payload);
                        _tradingRepository.ProcessOnMarketSetMessage(_messageBroker, onMarketSetMessage, correlationId);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ex.StackTrace);
            }
        }

        #endregion
    }
}
