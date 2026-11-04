using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using OE.Sportsbook.Service.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;

namespace OE.Sportsbook.Service
{
    public class Worker : BackgroundService
    {
        #region Fields

        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;

        private readonly Counter<long> _processedMessagesCounter;
        private readonly ActivitySource _activitySource;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        #endregion

        #region Ctor

        public Worker(ILogger<Worker> logger, IMessageBroker messageBroker, ISportsbookRepository sportsbookRepository, Instrumentation instrumentation)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _sportsbookRepository = sportsbookRepository;
            _processedMessagesCounter = instrumentation.ProcessedMessagesCounter;
            _activitySource = instrumentation.ActivitySource;
        }

        #endregion

        #region Methods

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _messageBroker.Dispose();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error stopping service: {Message}", e.Message);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("Prefetch SportsBook data");
                await _sportsbookRepository.SetInitialData();
                _logger.LogInformation("Connecting to RabbitMQ Server");
                _messageBroker.Connect();
                _logger.LogInformation("Connected. Processing Incoming messages");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error running service: {Message}", e.Message);
                throw; // up to stack, so it can fail early
            }
            _messageBroker.MessageReceived += MessageReceivedEvent;
        }

        #endregion

        #region Utilities

        private void MessageReceivedEvent(object? sender, BasicDeliverEventArgs ea)
        {

            var parentContext = Propagator.Extract(default, ea.BasicProperties, ExtractTraceFromBasicProperties);
            Baggage.Current = parentContext.Baggage;

            var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
            var messageType = ea.BasicProperties.Type;
            var correlationId = ea.BasicProperties.CorrelationId;

            using (var activity = _activitySource.StartActivity($"{ea.RoutingKey} receive", ActivityKind.Consumer, parentContext.ActivityContext))
                try
                {
                    activity?.SetTag("message.type", messageType);

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
                            ProcessMessage<OnMarketSettlementMessage>(
                                payload,
                                (msg) => _sportsbookRepository.ProcessOnMarketSettlementMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_BET_SETTLEMENT:
                            ProcessMessage<OnBetSettlementMessage>(
                                payload,
                                (msg) => _sportsbookRepository.ProcessOnBetSettlementMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_BET_STOP_GLOBAL:
                            ProcessMessage<OnBetStopGlobalMessage>(
                                payload,
                                (msg) => _sportsbookRepository.ProcessOnBetStopGlobalMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_BET_STOP_FIXTURE:
                            ProcessMessage<OnBetStopFixtureMessage>(
                                payload,
                                (msg) => _sportsbookRepository.ProcessOnBetStopFixtureMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_PRICE_BOOST:
                            ProcessMessage<OnPriceBoostMessage>(
                                payload,
                                (msg) => _sportsbookRepository.ProcessOnPriceBoostMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_LEAGUE_CLIENTS_CHANGE:
                            _logger.LogInformation("OESportsbook {MessageType}: {Payload}", messageType, payload);
                            ProcessMessage<OnLeagueClientsChangeMessage>(
                                payload,
                                (msg) => _sportsbookRepository.ProcessOnLeagueClientsChangeMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_DYNAMIC_TRANSLATION_CHANGE:
                            ProcessMessage<OnDeleteCacheMessage>(
                                payload,
                                (msg) => _sportsbookRepository.ProcessCacheInvalidationMessage(msg)
                            );
                            break;
                    }

                    if (_processedMessagesCounter.Enabled)
                        _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));

                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception ex)
                {
                    if (_processedMessagesCounter.Enabled)
                        _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", "error"));

                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                    _logger.LogError(ex, "Error processing message: Payload: {@Payload}",
                            payload);
                }
        }
        private static void ProcessMessage<T>(string payload, Action<T> processor)
        {
            if (string.IsNullOrWhiteSpace(payload)) return;

            var message = JsonSerializer.Deserialize<T>(payload);
            if (!EqualityComparer<T>.Default.Equals(message, default))
            {
                processor(message);
            }
        }

        private static IEnumerable<string> ExtractTraceFromBasicProperties(IBasicProperties props, string key)
        {
            try
            {
                if (props.Headers.TryGetValue(key, out var value))
                {
                    var bytes = value as byte[];
                    return new[] { Encoding.UTF8.GetString(bytes) };
                }
            }
            catch (Exception) { }

            return Enumerable.Empty<string>();
        }


        #endregion
    }
}
