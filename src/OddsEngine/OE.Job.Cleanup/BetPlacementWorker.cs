using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.Cleanup.Messages;
using OE.Job.Cleanup.Repository;
using OE.Shared.Utilities;
using RabbitMQ.Client.Events;
using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OE.Job.Cleanup
{
    // Listens to bet placement messages (r.bet.placement, published by Betting.API after the
    // bet is persisted) and marks the related fixture so the cleanup will not remove it.
    // Same source and reasoning as OE.Translation.Job's BetPlacementWorker: a bet that is
    // persisted but later rejected by the provider still remains in bet history, so its
    // fixture must be retained too.
    public class BetPlacementWorker : BackgroundService
    {
        private const string ON_BET_PLACEMENT = "OnBetPlacement";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ILogger<BetPlacementWorker> _logger;
        private readonly IMessageBroker _messageBroker;
        private readonly IBetFixtureCache _betFixtureCache;

        public BetPlacementWorker(
            ILogger<BetPlacementWorker> logger,
            IMessageBroker messageBroker,
            IBetFixtureCache betFixtureCache)
        {
            _logger = logger;
            _messageBroker = messageBroker;
            _betFixtureCache = betFixtureCache;
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
                    var payload = Encoding.UTF8.GetString(e.Body);
                    var message = JsonSerializer.Deserialize<OnBetPlacementMessage>(payload, JsonOptions);

                    if (message?.BetSelections != null)
                    {
                        foreach (var betSelection in message.BetSelections)
                        {
                            if (betSelection?.BetSelection == null || betSelection.BetSelection.FixtureId == 0)
                                continue;

                            _betFixtureCache.Add(betSelection.BetSelection.FixtureId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Best-effort: acknowledge rather than requeue to avoid poison loops. A missed
                // fixture id is repaired by the SQL preload on the next restart.
                _logger.LogError(ex, "Failed to process bet placement message. Type: {Type}",
                        messageType);
            }

            _messageBroker.Acknowledge(e);
        }
    }
}
