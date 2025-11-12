using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Adapter.Service.Repositories;
using OE.Adapter.Service.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using RabbitMQ.Client.Events;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;

namespace OE.Adapter.Service
{
    public class Worker : BackgroundService
    {
        #region Fields

        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ITradingRepository _tradingRepository;

        private readonly Counter<long> _processedMessagesCounter;

        #endregion

        #region Ctor

        public Worker(ILogger<Worker> logger, IMessageBroker messageBroker, ITradingRepository tradingRepository, Instrumentation instrumentation)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _tradingRepository = tradingRepository;
            _processedMessagesCounter = instrumentation.ProcessedMessagesCounter;
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
                _logger.LogError($"caught exception on stop: {ex.Message}");
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try {
                _logger.LogInformation("Connecting to RabbitMQ Server");
                _messageBroker.Connect();
            }
            catch (Exception e)
            {
                _logger.LogError($"Error connecting to RabbitMQ: {e.Message}");
                throw e; // up to stack, so it can fail early
            }
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
                    case MessageType.ON_FIXTURE_CHANGE:
                        ProcessMessage<OnFixtureChangeMessage>(
                            payload,
                            (msg) => _tradingRepository.ProcessOnFixtureChangeMessage(_messageBroker, msg, correlationId)
                        );
                        break;
                    case MessageType.ON_MARKET_CHANGE:
                        ProcessMessage<OnMarketChangeMessage>(
                            payload,
                            (msg) => _tradingRepository.ProcessOnMarketChangeMessage(_messageBroker, msg, correlationId)
                        );
                        break;
                    case MessageType.ON_MARKET_SETTLEMENT:
                        ProcessMessage<OnMarketSettlementMessage>(
                            payload,
                            (msg) => _tradingRepository.ProcessOnMarketSettlementMessage(_messageBroker, msg, correlationId)
                        );
                        break;
                }

                if (_processedMessagesCounter.Enabled)
                    _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));
            }
            catch (Exception ex)
            {
                if (_processedMessagesCounter.Enabled)
                    _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", "error"));

                _logger.LogError($"caught exception: {ex.Message}");
                _logger.LogError($"--> stacktrace  : {ex.StackTrace}");
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
