using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Adapter.Service.Repositories;
using OE.Adapter.Service.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using OE.Shared.Extensions;

namespace OE.Adapter.Service
{
    public class Worker : BackgroundService
    {
        #region Fields

        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ITradingRepository _tradingRepository;
        private readonly ITranslationCache _translationCache;
        private readonly IConfiguration _configuration;

        // Preload must succeed before consuming starts (messages would otherwise flow through
        // un-enriched); transient SQL failures are retried, then the service crashes so the
        // orchestrator restarts it. When no Translation connection string is configured at all
        // (secrets are committed empty — deployment injects ConnectionStrings__Translation),
        // the preload is skipped entirely so the adapter keeps flowing un-enriched instead of
        // crash-looping.
        private const int PRELOAD_RETRY_COUNT = 5;
        private const int PRELOAD_RETRY_DELAY_SECONDS = 10;

        private readonly ActivitySource _activitySource;
        private readonly Counter<long> _processedMessagesCounter;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        #endregion

        #region Ctor

        public Worker(ILogger<Worker> logger, IMessageBroker messageBroker, ITradingRepository tradingRepository, ITranslationCache translationCache, IConfiguration configuration, Instrumentation instrumentation)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _tradingRepository = tradingRepository;
            _translationCache = translationCache;
            _configuration = configuration;
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
            catch (Exception ex)
            {
                _logger.LogError($"caught exception on stop: {ex.Message}");
            }

            return base.StopAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await PreloadTranslationCache(stoppingToken);

            try
            {
                _logger.LogInformation("Connecting to RabbitMQ Server");
                _messageBroker.Connect();
                _logger.LogInformation("Processing incoming messages");
            }
            catch (Exception e)
            {
                _logger.LogError($"Error connecting to RabbitMQ: {e.Message}");
                throw;
            }
            _messageBroker.MessageReceived += MessageReceivedEvent;
        }

        private async Task PreloadTranslationCache(CancellationToken stoppingToken)
        {
            if (string.IsNullOrWhiteSpace(_configuration.GetConnectionString("Translation")))
            {
                _logger.LogWarning("No Translation connection string configured — translation preload skipped; messages flow un-enriched");
                return;
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _logger.LogInformation("Preloading translation cache");
                    _translationCache.Preload();
                    return;
                }
                catch (Exception e)
                {
                    _logger.LogError($"Error preloading translation cache (attempt {attempt}/{PRELOAD_RETRY_COUNT}): {e.Message}");

                    if (attempt >= PRELOAD_RETRY_COUNT)
                        throw;

                    await Task.Delay(TimeSpan.FromSeconds(PRELOAD_RETRY_DELAY_SECONDS), stoppingToken);
                }
            }
        }

        #endregion

        #region Utilities

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs ea)
        {
            var parentContext = Propagator.Extract(default, ea.BasicProperties, this.ExtractTraceFromBasicProperties);
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
                        case MessageType.ON_BET_SETTLEMENT:
                            ProcessMessage<OnBetSettlementMessage>(
                                payload,
                                (msg) => _tradingRepository.ProcessOnBetSettlementMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_BET_STOP_GLOBAL:
                            _logger.LogInformationEx($"OEAdapter {messageType}: {payload}");
                            ProcessMessage<OnBetStopGlobalMessage>(
                                payload,
                                (msg) => _tradingRepository.ProcessOnBetStopGlobalMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_MARKET_CANCEL:
                            ProcessMessage<OnMarketCancellationMessage>(
                                payload,
                                (msg) => _tradingRepository.ProcessMarketCancellation(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_BET_STOP_FIXTURE:
                            _logger.LogInformationEx($"OEAdapter {messageType}: {payload}");
                            ProcessMessage<OnBetStopFixtureMessage>(
                                payload,
                                (msg) => _tradingRepository.ProcessOnBetStopFixture(_messageBroker, msg, correlationId)
                            );
                            break;
                        case MessageType.ON_TRANSLATION_CHANGE:
                            _logger.LogInformationEx($"OEAdapter {messageType}: {payload}");
                            ProcessMessage<OnTranslationChangeMessage>(
                                payload,
                                (msg) => _tradingRepository.ProcessOnTranslationChangeMessage(_messageBroker, msg, correlationId)
                            );
                            break;
                    }

                    if (_processedMessagesCounter.Enabled)
                        _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));

                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorEx($"OEAdapter Error: {ex.Message} {ex.StackTrace} {payload}");

                    if (_processedMessagesCounter.Enabled)
                        _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", "error"));

                    _messageBroker.Publish(payload, messageType, $"r.oe.adapter.error", correlationId);

                    activity?.SetTag("payload", payload);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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

        #endregion
    }
}
