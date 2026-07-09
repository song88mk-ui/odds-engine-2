using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Shared.Constants;
using OE.Shared.Extensions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;
using OE.Translation.Service.Repositories;
using OE.Translation.Service.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;

namespace OE.Translation.Service
{
    public class Worker : BackgroundService
    {
        #region Fields

        private const int PRELOAD_RETRY_ATTEMPTS = 5;
        private const int PRELOAD_RETRY_DELAY_SECONDS = 10;

        private readonly ILogger<Worker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly ITranslationCache _translationCache;
        private readonly ITranslationRepository _translationRepository;
        private readonly IMongoTranslationRepository _mongoTranslationRepository;
        private readonly IConfiguration _configuration;

        private readonly ActivitySource _activitySource;
        private readonly Counter<long> _processedMessagesCounter;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        #endregion

        #region Ctor

        public Worker(
            ILogger<Worker> logger,
            IMessageBroker messageBroker,
            ITranslationCache translationCache,
            ITranslationRepository translationRepository,
            IMongoTranslationRepository mongoTranslationRepository,
            IConfiguration configuration,
            Instrumentation instrumentation)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _translationCache = translationCache;
            _translationRepository = translationRepository;
            _mongoTranslationRepository = mongoTranslationRepository;
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
            // The message path resolves translations from the in-memory snapshot — consuming
            // before the preload finishes would write stale/empty translations, so block startup
            // on it (retried; the pod restarts if the database stays unreachable).
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _translationCache.Preload();
                    break;
                }
                catch (Exception ex) when (attempt < PRELOAD_RETRY_ATTEMPTS && !stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning($"Translation cache preload attempt {attempt}/{PRELOAD_RETRY_ATTEMPTS} failed: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(PRELOAD_RETRY_DELAY_SECONDS), stoppingToken);
                }
            }

            if (_configuration.GetValue("Custom:Translation:SeedActiveSnapshot", false))
            {
                try
                {
                    _logger.LogInformation("SeedActiveSnapshot: reading active fixture/market/selection snapshot from Mongo");
                    var snapshot = _mongoTranslationRepository.ReadActiveSnapshot();
                    await _translationRepository.SeedActiveSnapshotAsync(snapshot);
                }
                catch (Exception ex)
                {
                    // Non-fatal: this is a startup enhancement, not core functionality — a failure
                    // here must not block the service from starting to process the live queue.
                    _logger.LogError(ex, "SeedActiveSnapshot failed, continuing startup without it: {Message}", ex.Message);
                }
            }

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
                            ProcessMessage<OnFixtureChangeMessage>(payload, message =>
                            {
                                _translationRepository.OnProcessFixtureSetUpdate(message).GetAwaiter().GetResult();
                                _mongoTranslationRepository.WriteFixtureTranslations(message);
                            });
                            break;
                        case MessageType.ON_MARKET_CHANGE:
                            ProcessMessage<OnMarketChangeMessage>(payload, message =>
                            {
                                _translationRepository.OnProcessMarketSetUpdate(message).GetAwaiter().GetResult();
                                _mongoTranslationRepository.WriteMarketTranslations(message);
                            });
                            break;
                        case MessageType.ON_TRANSLATION_CHANGE:
                            ProcessMessage<OnTranslationChangeMessage>(payload, message =>
                            {
                                _translationCache.Apply(message.Tr);
                                _mongoTranslationRepository.ProcessTranslationChange(_messageBroker, message, correlationId);
                            });
                            break;
                    }

                    if (_processedMessagesCounter.Enabled)
                        _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", messageType));

                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorEx($"OETranslation Error: {ex.Message} {ex.StackTrace} {payload}");

                    if (_processedMessagesCounter.Enabled)
                        _processedMessagesCounter.Add(1, new KeyValuePair<string, object?>("message.type", "error"));

                    _messageBroker.Publish(payload, messageType, $"r.oe.translation.error", correlationId);

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
