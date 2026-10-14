using OE.Translation.Job.Messages;
using OE.Translation.Job.Repositories;
using OE.Translation.Job.Translation;
using OE.Shared.Utilities;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace OE.Translation.Job.Workers
{
    // Listens to bet placement messages (r.bet.placement, published by Betting.API after the
    // bet is persisted) and flags the related fixture / market / selection translation rows so
    // the cleanup job will not remove them.
    //
    // r.bet.placement is used deliberately (not the provider-accepted r.bet.betinfo): a bet that
    // is persisted but later rejected by the provider still remains in the database and bet
    // history, so its translations must be retained too. (Provider.Service also echoes
    // OnBetPlacement to r.bet.placement with empty selections; those simply yield no keys.)
    public class BetPlacementWorker : BackgroundService
    {
        private const string ON_BET_PLACEMENT = "OnBetPlacement";
        private const string ERROR_ROUTING_KEY = "r.error.oe.translation.job";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ILogger<BetPlacementWorker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly IServiceScopeFactory _scopeFactory;

        public BetPlacementWorker(
            ILogger<BetPlacementWorker> logger,
            IMessageBroker messageBroker,
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _scopeFactory = scopeFactory;
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                _messageBroker.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping BetPlacementWorker");
            }

            return base.StopAsync(cancellationToken);
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _messageBroker.ConnectWithManualAck();
            _messageBroker.MessageReceived += MessageReceivedEvent;

            return Task.CompletedTask;
        }

        private void MessageReceivedEvent(object sender, BasicDeliverEventArgs e)
        {
            var messageType = e.BasicProperties.Type;

            try
            {
                if (string.Equals(messageType, ON_BET_PLACEMENT, StringComparison.OrdinalIgnoreCase))
                {
                    var payload = Encoding.UTF8.GetString(e.Body.ToArray());
                    FlagFromPayload(payload);
                }
            }
            catch (Exception ex)
            {
                // Best-effort: a missed flag only means a (still recent) row could be eligible
                // for cleanup later, so we acknowledge rather than requeue to avoid poison loops.
                // The error is surfaced on the bus and logged for visibility.
                PublishError(e, ex);
                _logger.LogError(ex, "Failed to flag bet placement message. Type: {Type}", messageType);
            }

            _messageBroker.Acknowledge(e);
        }

        private void FlagFromPayload(string payload)
        {
            var message = JsonSerializer.Deserialize<OnBetPlacementMessage>(payload, JsonOptions);

            var stringIds = TranslationKeyBuilder.Extract(message);

            if (stringIds.Count == 0)
                return;

            using var scope = _scopeFactory.CreateScope();

            var repository = scope.ServiceProvider
                .GetRequiredService<ITranslationCleanupRepository>();

            // Block until persisted so we only acknowledge after the flag is committed.
            var flagged = repository
                .FlagBetPlacementAsync(stringIds, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            if (flagged > 0)
                _logger.LogInformation("Flagged {Count} translation rows from bet placement", flagged);
        }

        private void PublishError(BasicDeliverEventArgs e, Exception ex)
        {
            try
            {
                var errorMessage = new
                {
                    e.BasicProperties.CorrelationId,
                    ex.Message,
                    ex.StackTrace
                };

                _messageBroker.Publish(
                    errorMessage,
                    ON_BET_PLACEMENT,
                    ERROR_ROUTING_KEY,
                    e.BasicProperties.CorrelationId ?? Guid.NewGuid().ToString());
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx, "Failed to publish bet placement flag error");
            }
        }
    }
}
