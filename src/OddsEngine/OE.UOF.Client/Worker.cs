using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Utilities;
using OE.UOF.Client.Models;
using OE.UOF.Client.Repository;
using OE.UOF.Client.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;

namespace OE.UOF.Client
{
    public class Worker : BackgroundService
    {
        private readonly IMessageBroker _messageBroker;
        private readonly IClientRepository _clientRepository;
        private readonly IPlatformRepository _platformRepository;
        private readonly IMessageBroker _messageBrokerExternal;
        private readonly ITradingRepository _tradingRepository;
        private readonly ILogger<Worker> _logger;
        private readonly ActivitySource _activitySource;
        private readonly Counter<long> _forwardedMessagesCounter;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;
        private readonly IConfiguration _configuration;
        private readonly string _clientCode;

        private int _currentMonth;

        private List<ClientConfigurationModel> _configurations = new List<ClientConfigurationModel>();
        private List<ClientLiveFixtureModel> _fixtureConfigurations = new List<ClientLiveFixtureModel>();

        public Worker(IMessageBroker messageBroker,
                      IClientRepository clientRepository,
                      IPlatformRepository platformRepository,
                      IMessageBroker messageBrokerExternal,
                      ITradingRepository tradingRepository,
                      ILogger<Worker> logger,
                      Instrumentation instrumentation,
                      IConfiguration configuration)
        {
            _configuration = configuration;
            _messageBroker = messageBroker;
            _clientRepository = clientRepository;
            _platformRepository = platformRepository;
            _messageBrokerExternal = messageBrokerExternal;
            _tradingRepository = tradingRepository;
            _logger = logger;
            _activitySource = instrumentation.ActivitySource;
            _forwardedMessagesCounter = instrumentation.ForwardedMessagesCounter;

            _currentMonth = DateTime.UtcNow.Month;

            _clientCode = configuration["Custom:ClientCode"];
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _messageBrokerExternal.Dispose();

                _messageBroker.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "failed to start worker");
            }

            return base.StopAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("processing market template player props");
            _platformRepository.ProcessMarketTemplatePlayerProps();

            _logger.LogInformation("getting client configurations");
            _configurations = _tradingRepository.GetClientConfigurations();

            _logger.LogInformation("getting client live fixtures");
            _fixtureConfigurations = _tradingRepository.GetClientLiveFixtures();

            var configurationPayload = JsonSerializer.Serialize(_configurations);
            _logger.LogDebug(configurationPayload);

            var fixtureConfigurationPayload = JsonSerializer.Serialize(_fixtureConfigurations);
            _logger.LogDebug(fixtureConfigurationPayload);

            _logger.LogInformation("connecting to external broker");
            _messageBrokerExternal.ExternalConnect();

            _logger.LogInformation("connecting to broker/RabbitMQ");
            _messageBroker.Connect();
            _messageBroker.MessageReceived += MessageReceivedEvent;
            _logger.LogInformation("connected, processing incoming messages");

            return Task.CompletedTask;
        }

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs ea)
        {
            var parentContext = Propagator.Extract(default, ea.BasicProperties, this.ExtractTraceFromBasicProperties);
            Baggage.Current = parentContext.Baggage;

            var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
            var messageType = ea.BasicProperties.Type;
            var correlationId = ea.BasicProperties.CorrelationId;

            using (var activity = _activitySource.StartActivity($"{ea.RoutingKey} receive", ActivityKind.Consumer, parentContext.ActivityContext))
                try
                {//
                    activity?.SetTag("message.type", messageType);
                    activity?.SetTag("client.code", _clientCode);
                    activity?.SetTag("correlation.id", correlationId);

                    ResetDate();

                    switch (messageType)
                    {
                        case MessageType.ON_FIXTURE_CHANGE:
                            ProcessMessageWithValidation<OnFixtureChangeMessage>(payload, msg =>
                                {
                                    _clientRepository.ProcessFixtureChange(msg, _messageBrokerExternal, correlationId);

                                    _clientRepository.UpdateFixtureConfiguration(msg, _messageBroker, _fixtureConfigurations, _configurations);
                                });
                            break;
                        case MessageType.ON_MARKET_CHANGE:
                            ProcessMessageWithValidation<OnMarketChangeMessage>(payload, msg =>
                                {
                                    _clientRepository.ProcessMarketChange(msg, _messageBrokerExternal, correlationId, _fixtureConfigurations, _configurations);

                                    //_clientRepository.UpdateFixtureConfiguration(msg, _messageBroker, _fixtureConfigurations, _configurations);
                                });
                            break;
                        case MessageType.ON_BET_STOP_FIXTURE:
                            ProcessMessageWithValidation<OnBetStopFixtureMessage>(payload, msg =>
                                {
                                    _clientRepository.ProcessBetStop(msg, _messageBrokerExternal, correlationId);
                                });
                            break;
                        case MessageType.ON_BET_STOP_GLOBAL:
                            ProcessMessage<OnBetStopGlobalMessage>(payload, msg => _clientRepository.ProcessBetStopGlobal(msg, _messageBrokerExternal, correlationId));
                            break;

                        case MessageType.ON_MARKET_SETTLEMENT:
                            ProcessMessageWithValidation<OnMarketSettlementMessage>(payload, msg =>
                                {
                                    _clientRepository.ProcessMarketSettlement(msg, _messageBrokerExternal, correlationId);
                                });
                            break;

                        case MessageType.ON_MARKET_CANCEL:
                            ProcessMessageWithValidation<OnMarketCancellationMessage>(payload, msg =>
                                {
                                    _clientRepository.ProcessMarketCancellation(msg, _messageBrokerExternal, correlationId);
                                });
                            break;

                        case "OnHeartbeat":
                            ProcessMessage<OFHeartbeatMessage>(payload, msg => _clientRepository.ProcessHeartbeat(msg, _messageBrokerExternal, correlationId));
                            break;
                    }

                    if (_forwardedMessagesCounter.Enabled)
                        _forwardedMessagesCounter.Add(1, new KeyValuePair<string, object?>("client.code", _clientCode));

                    activity?.SetStatus(ActivityStatusCode.Ok);

                }
                catch (Exception ex)
                {

                    activity?.SetTag("stack.trace", ex.StackTrace);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                    _logger.LogError(ex, $"trace: {ex.StackTrace}");
                    var errorMessage = new
                    {
                        CorrelationId = correlationId,
                        Message = ex.Message,
                        StackTrace = ex.StackTrace,
                        Payload = payload
                    };

                    _messageBroker.Publish(errorMessage, $"{messageType}Error", "r.error.engine.uof.client", correlationId);
                }
        }

        private void ProcessMessageWithValidation<T>(string payload, Action<T> handler)
        {
            //TODO: check config validation 
            //var sportModel = JsonSerializer.Deserialize<SportModel>(payload);

            //if (!IsMessageValid(sportModel))
            //    return;

            var message = JsonSerializer.Deserialize<T>(payload);
            handler(message);
        }

        private void ProcessMessage<T>(string payload, Action<T> handler)
        {
            var message = JsonSerializer.Deserialize<T>(payload);
            handler(message);
        }
        private void ProcessRawMessage(string payload, Action<string> handler)
        {
            handler(payload);
        }

        private bool IsMessageValid(SportModel model)
        {
            var sportConfig = _configurations.FirstOrDefault(c => c.SportId == model.SpId);
            if (sportConfig == null)
                return false;

            if (model.PrId == 3)
                return sportConfig.PrematchOdds;

            if (model.PrId != 1 || !sportConfig.LiveOdds)
                return false;

            if (!sportConfig.MaxFixtures.HasValue)
                return true;

            var fixtureConfig = _fixtureConfigurations.FirstOrDefault(c => c.SportId == model.SpId);

            if (fixtureConfig == null)
                return true;

            int currentFixtureCount = fixtureConfig.Fixtures.Count;
            int maxFixtures = sportConfig.MaxFixtures.Value;

            if (currentFixtureCount < maxFixtures)
                return true;

            return fixtureConfig.Fixtures.Any(f => f.FixtureSpecifier == model.FiSp);
        }

        private void ResetDate()
        {
            var newMonth = DateTime.UtcNow.Month;

            if (_currentMonth != newMonth)
            {
                _currentMonth = newMonth;
                _fixtureConfigurations = new List<ClientLiveFixtureModel>();
            }
        }

        private IEnumerable<string> ExtractTraceFromBasicProperties(IBasicProperties props, string key)
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
    }
}
