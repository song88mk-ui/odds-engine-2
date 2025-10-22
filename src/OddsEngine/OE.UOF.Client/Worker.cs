using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Text.Json;
using System.Text;
using OE.Shared.Utilities;
using OE.UOF.Client.Repository;
using OE.Shared.Models;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;

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

        private int _currentMonth;

        private List<ClientConfigurationModel> _configurations = new List<ClientConfigurationModel>();
        private List<ClientLiveFixtureModel> _fixtureConfigurations = new List<ClientLiveFixtureModel>();

        public Worker(IMessageBroker messageBroker, IClientRepository clientRepository, IPlatformRepository platformRepository, IMessageBroker messageBrokerExternal, ITradingRepository tradingRepository, ILogger<Worker> logger)
        {
            _messageBroker = messageBroker;
            _clientRepository = clientRepository;
            _platformRepository = platformRepository;
            _messageBrokerExternal = messageBrokerExternal;
            _tradingRepository = tradingRepository;
            _logger = logger;

            _currentMonth = DateTime.UtcNow.Month;
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
                _logger.LogError(ex.Message);
            }

            return base.StopAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _platformRepository.ProcessMarketTemplatePlayerProps();

            _configurations = _tradingRepository.GetClientConfigurations();
            _fixtureConfigurations = _tradingRepository.GetClientLiveFixtures();

            var configurationPayload = JsonSerializer.Serialize(_configurations);
            _logger.LogDebug(configurationPayload);

            var fixtureConfigurationPayload = JsonSerializer.Serialize(_fixtureConfigurations);
            _logger.LogDebug(fixtureConfigurationPayload);

            _messageBrokerExternal.ExternalConnect();

            _messageBroker.Connect();
            _messageBroker.MessageReceived += MessageReceivedEvent;

            return Task.CompletedTask;
        }

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs e)
        {
            var payload = Encoding.UTF8.GetString(e.Body.ToArray());
            var messageType = e.BasicProperties.Type;
            var correlationId = e.BasicProperties.CorrelationId;

            try
            {
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                ResetDate();

                switch (messageType)
                {
                    case MessageType.ON_FIXTURE_CHANGE:
                        var onOddsChangeSport = JsonSerializer.Deserialize<SportModel>(payload);

                        if (IsMessageValid(onOddsChangeSport))
                        {
                            var message = JsonSerializer.Deserialize<OnInternalOddsChangeMessage>(payload);

                            _clientRepository.ProcessFixtureChange(message, _messageBrokerExternal, correlationId, _fixtureConfigurations, _configurations);

                            _clientRepository.UpdateFixtureConfiguration(message, _messageBroker, _fixtureConfigurations, _configurations);
                        }
                        break;
                    case MessageType.ON_BET_STOP_FIXTURE:
                        var onBetStopSport = JsonSerializer.Deserialize<SportModel>(payload);

                        if (IsMessageValid(onBetStopSport))
                            _clientRepository.ProcessBetStop(payload, _messageBrokerExternal, correlationId);
                        break;
                    case MessageType.ON_BET_STOP_GLOBAL:
                        _clientRepository.ProcessBetStopGlobal(payload, _messageBrokerExternal, correlationId);
                        break;
                    case MessageType.ON_MARKET_SETTLEMENT:
                        var onBetSettlementSport = JsonSerializer.Deserialize<SportModel>(payload);

                        if (IsMessageValid(onBetSettlementSport))
                            _clientRepository.ProcessMarketSettlement(payload, _messageBrokerExternal, correlationId);
                        break;
                    case MessageType.ON_MARKET_CANCEL:
                        var onBetCancellationSport = JsonSerializer.Deserialize<SportModel>(payload);

                        if (IsMessageValid(onBetCancellationSport))
                            _clientRepository.ProcessMarketCancellation(payload, _messageBrokerExternal, correlationId);
                        break;
                    case "OnHeartbeat":
                        _clientRepository.ProcessHeartbeat(payload, _messageBrokerExternal, correlationId);
                        break;
                }

                _logger.LogDebug($"correlationId: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
                stopwatch.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogError("${ex.Message} - {ex.StackTrace}");
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

        private bool IsMessageValid(SportModel model)
        {
            //var stopwatch = new Stopwatch();
            //stopwatch.Start();

            var retval = false;

            var sportConfiguration = _configurations.FirstOrDefault(c => c.SportId == model.SportId);

            if (sportConfiguration != null)
            {
                if (model.ProducerId == 3 && sportConfiguration.PrematchOdds)
                {
                    retval = true;
                }
                else if (model.ProducerId == 1 && sportConfiguration.LiveOdds)
                {
                    if (!sportConfiguration.MaxFixtures.HasValue)
                    {
                        // if max fixture doesn't have value it means no max fixture is set.
                        retval = true;
                    }
                    else
                    {
                        var fixtureConfig = _fixtureConfigurations.FirstOrDefault(c => c.SportId == model.SportId);

                        if (fixtureConfig == null)
                        {
                            // if sport doesn't exist then return true.
                            retval = true;
                        }
                        else
                        {
                            if (fixtureConfig.Fixtures.Count < sportConfiguration.MaxFixtures)
                            {
                                // if fixture count didn't reach the treshold yet then return true.
                                retval = true;
                            }
                            else if (fixtureConfig.Fixtures.Count >= sportConfiguration.MaxFixtures)
                            {
                                // check if the fixture is included in the list. if not then return false.
                                var existingFixture = fixtureConfig.Fixtures.FirstOrDefault(c => c.FixtureSpecifier == model.FixtureSpecifier);

                                retval = existingFixture != null ? true : false;
                            }
                        }
                    }
                }
            }

            //Console.WriteLine($"IsMessageValid: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
            //stopwatch.Stop();

            return retval;
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
    }
}
