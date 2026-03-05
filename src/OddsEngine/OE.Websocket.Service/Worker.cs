using MessagePack.Resolvers;
using MessagePack;
using Microsoft.AspNetCore.SignalR.Client;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Messages;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.WebSocket.Service.Repository;
using OE.WebSocket.Service.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OE.Shared.Constants;
using OE.Shared.Enums;

namespace OE.Websocket.Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        private readonly IConfiguration _configuration;
        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;
        private readonly HubConnection _hubConnection;

        private readonly ActivitySource _activitySource;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        private Thread _threadTopFixtures;
        private Thread _threadTopLeagues;
        private Thread _threadMarketCount;
        private Thread _threadMainMarketsOddsChange;
        private Thread _threadAllMarketsOddsChange;
        private Thread _threadBetStatus;
        private Thread _threadCashOut;
        private Thread _threadWallet;
        private Thread _threadFavorites;
        private Thread _threadRemoveFavorites;
        private Thread _threadBoostedFixtures;
        private Thread _threadPayable;
        private Thread _threadMarketSortOrderChange;

        private readonly BlockingCollection<OnMarketChangeMessage> _messagesTopFixtures = new BlockingCollection<OnMarketChangeMessage>();
        private readonly BlockingCollection<OnMarketChangeMessage> _messagesTopLeagues = new BlockingCollection<OnMarketChangeMessage>();
        private readonly BlockingCollection<OnMarketChangeMessage> _messagesMarketCount = new BlockingCollection<OnMarketChangeMessage>();
        private readonly BlockingCollection<OnMarketChangeMessage> _messagesMainMarketsOddsChange = new BlockingCollection<OnMarketChangeMessage>();
        private readonly BlockingCollection<OnMarketChangeMessage> _messagesAllMarketsOddsChange = new BlockingCollection<OnMarketChangeMessage>();
        private readonly BlockingCollection<IncomingBetStatus> _messagesBetStatus = new BlockingCollection<IncomingBetStatus>();
        private readonly BlockingCollection<IncomingCashOutChange> _messagesCashOut = new BlockingCollection<IncomingCashOutChange>();
        private readonly BlockingCollection<IncomingWalletChange> _messagesWallet = new BlockingCollection<IncomingWalletChange>();
        private readonly BlockingCollection<IncomingVisitFavorites> _messagesFavorites = new BlockingCollection<IncomingVisitFavorites>();
        private readonly BlockingCollection<IncomingVisitFavorites> _messagesRemoveFavorites = new BlockingCollection<IncomingVisitFavorites>();
        private readonly BlockingCollection<IncomingVisitBoostedFixtures> _messagesBoostedFixtures = new BlockingCollection<IncomingVisitBoostedFixtures>();
        private readonly BlockingCollection<IncomingPayableChange> _messagesPayable = new BlockingCollection<IncomingPayableChange>();
        private readonly BlockingCollection<bool> _messagesMarketSortOrder = new BlockingCollection<bool>();

        private async void ThreadTopFixtures()
        {
            while (true)
            {
                var message = _messagesTopFixtures.Take();
                await _sportsbookRepository.ProcessTopFixtures(message);
            }
        }
        private async void ThreadTopLeagues()
        {
            while (true)
            {
                var message = _messagesTopLeagues.Take();
                await _sportsbookRepository.ProcessTopLeagues(message);
            }
        }
        private async void ThreadMarketCount()
        {
            while (true)
            {
                var message = _messagesMarketCount.Take();
                await _sportsbookRepository.ProcessMarketCount(message);
            }
        }
        private async void ThreadMainMarketsOddsChange()
        {
            while (true)
            {
                var message = _messagesMainMarketsOddsChange.Take();
                await _sportsbookRepository.ProcessOddsChangeForMainMarkets(message, _hubConnection);
            }
        }
        private async void ThreadAllMarketsOddsChange()
        {
            while (true)
            {
                var message = _messagesAllMarketsOddsChange.Take();
                await _sportsbookRepository.ProcessOddsChangeForAllMarkets(message, _hubConnection);
            }
        }
        private async void ThreadBetStatus()
        {
            while (true)
            {
                var message = _messagesBetStatus.Take();
                await _sportsbookRepository.ProcessBetStatus(message, _hubConnection);
            }
        }
        private async void ThreadCashOut()
        {
            while (true)
            {
                var message = _messagesCashOut.Take();
                await _sportsbookRepository.ProcessCashOutChange(message, _hubConnection);
            }
        }
        private async void ThreadWallet()
        {
            while (true)
            {
                var message = _messagesWallet.Take();
                await _sportsbookRepository.ProcessWalletChange(message, _hubConnection);
            }
        }
        private async void ThreadFavorites()
        {
            while (true)
            {
                var message = _messagesFavorites.Take();
                await _sportsbookRepository.ProcessFavorites(message);
            }
        }
        private async void ThreadRemoveFavorites()
        {
            while (true)
            {
                var message = _messagesRemoveFavorites.Take();
                await _sportsbookRepository.ProcessRemoveFavorites(message);
            }
        }
        private async void ThreadBoostedFixtures()
        {
            while (true)
            {
                var message = _messagesBoostedFixtures.Take();
                await _sportsbookRepository.ProcessBoostedFixtures(message);
            }
        }
        private async void ThreadPayable()
        {
            while (true)
            {
                var message = _messagesPayable.Take();
                await _sportsbookRepository.ProcessPayableChange(message, _hubConnection);
            }
        }
        private async void ThreadMarketSortOrder()
        {
            while (true)
            {
                var message = _messagesMarketSortOrder.Take();
                await _sportsbookRepository.ProcessMarketSortOrder();
            }
        }

        public Worker(
                ILogger<Worker> logger,
                IConfiguration configuration,
                IMessageBroker messageBroker,
                ISportsbookRepository sportsbookRepository,
                Instrumentation instrumentation)
        {
            _configuration = configuration;
            _activitySource = instrumentation.ActivitySource;
            _logger = logger;
            _messageBroker = messageBroker;
            _sportsbookRepository = sportsbookRepository;
            //
            if (bool.Parse(_configuration["Custom:UseMessagePack"]))
            {
                _logger.LogInformation("Using messagepack serialization protocol");
                _hubConnection = new HubConnectionBuilder()
                                    .WithUrl(_configuration["Custom:WebSocketAPI"])
                                    .AddMessagePackProtocol(options =>
                                    {
                                        StaticCompositeResolver.Instance.Register(
                                            ContractlessStandardResolver.Instance
                                        );
                                        options.SerializerOptions = MessagePackSerializerOptions.Standard
                                            .WithResolver(ContractlessStandardResolver.Instance)
                                            .WithSecurity(MessagePackSecurity.UntrustedData);
                                    })
                                    .WithAutomaticReconnect()
                                    .Build();

            }
            else
            {
                _logger.LogInformation("Not using messagepack serialization protocol");
                _hubConnection = new HubConnectionBuilder()
                                    .WithUrl(_configuration["Custom:WebSocketAPI"])
                                    .WithAutomaticReconnect()
                                    .Build();
            }

        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _hubConnection.DisposeAsync();

            _messageBroker.Dispose();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await _sportsbookRepository.ProcessMainMarketTemplates();
            await _sportsbookRepository.ProcessMarketSortOrder();
            await _sportsbookRepository.ProcessFixtureSortOrder();
            await _sportsbookRepository.ProcessBetBuilderMarketTemplates();
            await _sportsbookRepository.ProcessMarketTemplatePlayerProps();

            _logger.LogInformation("Preparing threads");
            _threadTopFixtures = new Thread(ThreadTopFixtures);
            _threadTopLeagues = new Thread(ThreadTopLeagues);
            _threadMarketCount = new Thread(ThreadMarketCount);
            _threadMainMarketsOddsChange = new Thread(ThreadMainMarketsOddsChange);
            _threadAllMarketsOddsChange = new Thread(ThreadAllMarketsOddsChange);
            _threadBetStatus = new Thread(ThreadBetStatus);
            _threadCashOut = new Thread(ThreadCashOut);
            _threadWallet = new Thread(ThreadWallet);
            _threadFavorites = new Thread(ThreadFavorites);
            _threadRemoveFavorites = new Thread(ThreadRemoveFavorites);
            _threadBoostedFixtures = new Thread(ThreadBoostedFixtures);
            _threadPayable = new Thread(ThreadPayable);
            _threadMarketSortOrderChange = new Thread(ThreadMarketSortOrder);

            _logger.LogInformation("Starting threads");
            _threadTopFixtures.Start();
            _threadTopLeagues.Start();
            _threadMarketCount.Start();
            _threadMainMarketsOddsChange.Start();
            _threadAllMarketsOddsChange.Start();
            _threadBetStatus.Start();
            _threadCashOut.Start();
            _threadWallet.Start();
            _threadFavorites.Start();
            _threadRemoveFavorites.Start();
            _threadBoostedFixtures.Start();
            _threadPayable.Start();
            _threadMarketSortOrderChange.Start();

            await _hubConnection.StartAsync();

            _logger.LogInformation("Connecting to broker/RabbitMQ");
            _messageBroker.Connect();

            _messageBroker.MessageReceived += MessageReceivedEvent;
            _logger.LogInformation("connected. processing incoming messages");

            var connectionId = await _hubConnection.InvokeAsync<string>("GetConnectionId");
            _logger.LogInformation($"connectionId: {connectionId}");
        }

        private async void MessageReceivedEvent(object sender, BasicDeliverEventArgs e)
        {//
            var parentContext = Propagator.Extract(default, e.BasicProperties, this.ExtractTraceFromBasicProperties);
            Baggage.Current = parentContext.Baggage;

            var payload = Encoding.UTF8.GetString(e.Body);
            var msgType = e.BasicProperties.Type;
            var correlationId = e.BasicProperties.CorrelationId;

            _logger.LogInformation("{messageType}:[{routingKey}] {@payload}", msgType, e.RoutingKey, payload);

            using (var activity = _activitySource.StartActivity($"{e.RoutingKey} receive", ActivityKind.Consumer, parentContext.ActivityContext))
                try
                {
                    activity?.SetTag("message.type", msgType);

                    switch (msgType)
                    {
                        case MessageType.ON_ODDS_CHANGE:
                            var onOddsChange = JsonSerializer.Deserialize<OnMarketChangeMessage>(payload);

                            if(onOddsChange.FiStId == (int)FixtureStatus.Closed)
                                _logger.LogInformation($"FixtureId:{onOddsChange} {msgType} Fixture closed");

                            _messagesMarketCount.Add(onOddsChange);
                            _messagesTopFixtures.Add(onOddsChange);
                            _messagesTopLeagues.Add(onOddsChange);
                            _messagesMainMarketsOddsChange.Add(onOddsChange);
                            _messagesAllMarketsOddsChange.Add(onOddsChange);
                            break;

                        case MessageType.ON_BET_STOP_GLOBAL:
                            var onBetStopGlobal = JsonSerializer.Deserialize<OnBetStopGlobalMessage>(payload);
                            await _sportsbookRepository.ProcessBetStopGlobal(onBetStopGlobal, _hubConnection);
                            break;
                        case MessageType.ON_BET_STOP_FIXTURE:
                            var onBetStopFixture = JsonSerializer.Deserialize<OnBetStopFixtureMessage>(payload);
                            await _sportsbookRepository.ProcessBetStopFixture(onBetStopFixture, _hubConnection);
                            break;

                        case "OnBetStatus":
                            var onBetStatus = JsonSerializer.Deserialize<IncomingBetStatus>(payload);
                            _messagesBetStatus.Add(onBetStatus);
                            break;

                        case "OnCashOutChange":
                            var onCashOutChange = JsonSerializer.Deserialize<IncomingCashOutChange>(payload);
                            _messagesCashOut.Add(onCashOutChange);
                            break;

                        case "OnWalletChange":
                            var onWalletChange = JsonSerializer.Deserialize<IncomingWalletChange>(payload);
                            _messagesWallet.Add(onWalletChange);
                            break;

                        case "OnVisitFavorites":
                            var onVisitFavorites = JsonSerializer.Deserialize<IncomingVisitFavorites>(payload);
                            _messagesFavorites.Add(onVisitFavorites);
                            break;
                        case "OnRemoveUserFavorites":
                            var onRemoveFavorites = JsonSerializer.Deserialize<IncomingVisitFavorites>(payload);
                            _messagesRemoveFavorites.Add(onRemoveFavorites);
                            break;
                        case "OnVisitBoostedFixtures":
                            var onVisitBoostedFixtures = JsonSerializer.Deserialize<IncomingVisitBoostedFixtures>(payload);
                            _messagesBoostedFixtures.Add(onVisitBoostedFixtures);
                            break;
                        case "OnPayableChange":
                            var onPayableChange = JsonSerializer.Deserialize<IncomingPayableChange>(payload);
                            _messagesPayable.Add(onPayableChange);
                            break;
                        case "OnMarketSortOrderChange":
                            _messagesMarketSortOrder.Add(true);
                            break;

                    }
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception exception)
                {

                    activity?.SetTag("stack.trace", exception.StackTrace);
                    activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                    _logger.LogError(exception, "error processing message: {trace}", exception.StackTrace);
                    // abruptly terminate the process and let the operating system
                    // supervisor handles its job of restarting it
                    System.Environment.Exit(1);
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
