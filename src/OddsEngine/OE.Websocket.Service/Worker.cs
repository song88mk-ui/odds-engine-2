using MessagePack.Resolvers;
using MessagePack;
using Microsoft.AspNetCore.SignalR.Client;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
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
        // How often the client list is re-read from the Platform API; bounds how long
        // a newly onboarded client waits before this instance starts serving it.
        private const int ClientRefreshMinutes = 5;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly ILogger<Worker> _logger;

        private readonly IConfiguration _configuration;
        private readonly IMessageBroker _messageBroker;
        private readonly ISportsbookRepository _sportsbookRepository;
        private readonly IClientRegistry _clientRegistry;
        private readonly IClientPresence _clientPresence;
        private readonly HubConnection _hubConnection;

        private readonly ActivitySource _activitySource;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        // Pairs a single-client message with the client resolved from its routing key
        // (or payload ClientId), so the dispatch threads do not have to re-resolve.
        private sealed record ClientScoped<T>(ClientContext Client, T Message);

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
        private readonly BlockingCollection<ClientScoped<IncomingBetStatus>> _messagesBetStatus = new BlockingCollection<ClientScoped<IncomingBetStatus>>();
        private readonly BlockingCollection<ClientScoped<IncomingCashOutChange>> _messagesCashOut = new BlockingCollection<ClientScoped<IncomingCashOutChange>>();
        private readonly BlockingCollection<ClientScoped<IncomingWalletChange>> _messagesWallet = new BlockingCollection<ClientScoped<IncomingWalletChange>>();
        private readonly BlockingCollection<ClientScoped<IncomingVisitFavorites>> _messagesFavorites = new BlockingCollection<ClientScoped<IncomingVisitFavorites>>();
        private readonly BlockingCollection<ClientScoped<IncomingVisitFavorites>> _messagesRemoveFavorites = new BlockingCollection<ClientScoped<IncomingVisitFavorites>>();
        private readonly BlockingCollection<ClientScoped<IncomingVisitBoostedFixtures>> _messagesBoostedFixtures = new BlockingCollection<ClientScoped<IncomingVisitBoostedFixtures>>();
        private readonly BlockingCollection<ClientScoped<IncomingPayableChange>> _messagesPayable = new BlockingCollection<ClientScoped<IncomingPayableChange>>();
        private readonly BlockingCollection<ClientScoped<bool>> _messagesMarketSortOrder = new BlockingCollection<ClientScoped<bool>>();

        // Fan-out for messages every client receives. Clients are processed in
        // parallel but the call returns only when ALL clients finished this message
        // (per-message barrier), so each client sees messages in consumption order.
        private async Task ProcessForAllClients(Func<ClientContext, Task> process)
        {
            var clients = _clientRegistry.Clients;

            if (clients.Count == 1)
            {
                await process(clients[0]);
                return;
            }

            await Task.WhenAll(clients.Select(process));
        }

        // Presence-filtered variant for the two odds-change PUBLISH fan-outs only:
        // clients with no connected users are skipped (missed odds changes self-heal
        // on the next update). Not used for cache-maintenance fan-outs (market count,
        // top fixtures/leagues maintain INCREMENTAL state that must never miss a
        // message), nor for bet stops and user-specific one-offs (they do not
        // self-heal). Also skips clients whose template data is still loading.
        private async Task ProcessForActiveClients(Func<ClientContext, Task> process)
        {
            var clients = _clientRegistry.Clients.Where(c => c.IsReady && _clientPresence.IsActive(c.ClientCode)).ToList();

            if (clients.Count == 0)
                return;

            if (clients.Count == 1)
            {
                await process(clients[0]);
                return;
            }

            await Task.WhenAll(clients.Select(process));
        }

        private async void ThreadTopFixtures()
        {
            while (true)
            {
                var message = _messagesTopFixtures.Take();
                await ProcessForAllClients(client => _sportsbookRepository.ProcessTopFixtures(client, message));
            }
        }
        private async void ThreadTopLeagues()
        {
            while (true)
            {
                var message = _messagesTopLeagues.Take();
                await ProcessForAllClients(client => _sportsbookRepository.ProcessTopLeagues(client, message));
            }
        }
        private async void ThreadMarketCount()
        {
            while (true)
            {
                var message = _messagesMarketCount.Take();
                await ProcessForAllClients(client => _sportsbookRepository.ProcessMarketCount(client, message));
            }
        }
        private async void ThreadMainMarketsOddsChange()
        {
            while (true)
            {
                var message = _messagesMainMarketsOddsChange.Take();
                await ProcessForActiveClients(client => _sportsbookRepository.ProcessOddsChangeForMainMarkets(client, message, _hubConnection));
            }
        }
        private async void ThreadAllMarketsOddsChange()
        {
            while (true)
            {
                var message = _messagesAllMarketsOddsChange.Take();
                await ProcessForActiveClients(client => _sportsbookRepository.ProcessOddsChangeForAllMarkets(client, message, _hubConnection));
            }
        }
        private async void ThreadBetStatus()
        {
            while (true)
            {
                var item = _messagesBetStatus.Take();
                await _sportsbookRepository.ProcessBetStatus(item.Client, item.Message, _hubConnection);
            }
        }
        private async void ThreadCashOut()
        {
            while (true)
            {
                var item = _messagesCashOut.Take();
                await _sportsbookRepository.ProcessCashOutChange(item.Client, item.Message, _hubConnection);
            }
        }
        private async void ThreadWallet()
        {
            while (true)
            {
                var item = _messagesWallet.Take();
                await _sportsbookRepository.ProcessWalletChange(item.Client, item.Message, _hubConnection);
            }
        }
        private async void ThreadFavorites()
        {
            while (true)
            {
                var item = _messagesFavorites.Take();
                await _sportsbookRepository.ProcessFavorites(item.Client, item.Message);
            }
        }
        private async void ThreadRemoveFavorites()
        {
            while (true)
            {
                var item = _messagesRemoveFavorites.Take();
                await _sportsbookRepository.ProcessRemoveFavorites(item.Client, item.Message);
            }
        }
        private async void ThreadBoostedFixtures()
        {
            while (true)
            {
                var item = _messagesBoostedFixtures.Take();
                await _sportsbookRepository.ProcessBoostedFixtures(item.Client, item.Message);
            }
        }
        private async void ThreadPayable()
        {
            while (true)
            {
                var item = _messagesPayable.Take();
                await _sportsbookRepository.ProcessPayableChange(item.Client, item.Message, _hubConnection);
            }
        }
        private async void ThreadMarketSortOrder()
        {
            while (true)
            {
                var item = _messagesMarketSortOrder.Take();
                await _sportsbookRepository.ProcessMarketSortOrder(item.Client);
            }
        }

        public Worker(
                ILogger<Worker> logger,
                IConfiguration configuration,
                IMessageBroker messageBroker,
                ISportsbookRepository sportsbookRepository,
                IClientRegistry clientRegistry,
                IClientPresence clientPresence,
                Instrumentation instrumentation)
        {
            _configuration = configuration;
            _activitySource = instrumentation.ActivitySource;
            _logger = logger;
            _messageBroker = messageBroker;
            _sportsbookRepository = sportsbookRepository;
            _clientRegistry = clientRegistry;
            _clientPresence = clientPresence;
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

            _hubConnection.Reconnecting += OnHubConnectionReconnecting;
            _hubConnection.Reconnected += OnHubConnectionReconnected;
            _hubConnection.Closed += OnHubConnectionClosed;
        }

        private Task OnHubConnectionReconnecting(Exception error)
        {
            _logger.LogWarning(error, "hub connection lost, automatic reconnect in progress. State={state}", _hubConnection.State);
            return Task.CompletedTask;
        }

        private Task OnHubConnectionReconnected(string connectionId)
        {
            _logger.LogInformation("hub connection reconnected. connectionId: {connectionId}", connectionId);
            return Task.CompletedTask;
        }

        // WithAutomaticReconnect() gives up after its default retry sequence (0s/2s/10s/30s) and
        // leaves the connection permanently Disconnected - without this handler the service would
        // never push updates again until the process itself is restarted.
        private async Task OnHubConnectionClosed(Exception error)
        {
            if (error == null)
            {
                _logger.LogInformation("hub connection closed intentionally, not reconnecting");
                return;
            }

            _logger.LogError(error, "hub connection closed permanently after automatic reconnect gave up. Reconnecting manually");

            var delay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);

            while (true)
            {
                try
                {
                    await Task.Delay(delay);
                    await _hubConnection.StartAsync();
                    _logger.LogInformation("hub connection re-established after manual reconnect");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "manual reconnect attempt failed, retrying in {delaySeconds}s", delay.TotalSeconds);
                    delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
                }
            }
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _hubConnection.DisposeAsync();

            _messageBroker.Dispose();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Do not start consuming until discovery has produced at least one client;
            // otherwise every message would be drained with nobody to serve.
            var clients = await _clientRegistry.RefreshAsync();

            while (_clientRegistry.Clients.Count == 0 && !stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("no clients discovered yet, retrying in 10s");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                clients = await _clientRegistry.RefreshAsync();
            }

            _logger.LogInformation("serving {clientCount} client(s): {clientCodes}",
                    _clientRegistry.Clients.Count, string.Join(",", _clientRegistry.Clients.Select(c => c.ClientCode)));

            await LoadClientData(clients);

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

            _ = Task.Run(() => RefreshClientsLoop(stoppingToken), stoppingToken);
            _ = Task.Run(() => PresencePollLoop(stoppingToken), stoppingToken);
        }

        // Polls the hub's /v1/activeclients endpoint so the odds fan-out can skip
        // clients with no connected users. Any failure marks presence unavailable,
        // which makes IsActive() fail open (publish to everyone) - so this is safe
        // to run against a hub that does not expose the endpoint yet. Failures are
        // logged once per outage, not per poll, to keep log volume flat.
        private async Task PresencePollLoop(CancellationToken stoppingToken)
        {
            // The hub serves its HTTP endpoints on the same host as the SignalR path,
            // so the presence URL is always derived from Custom:WebSocketAPI.
            var hubUri = new Uri(_configuration["Custom:WebSocketAPI"]);
            var presenceUrl = $"{hubUri.GetLeftPart(UriPartial.Authority)}/v1/activeclients";

            _logger.LogInformation("polling presence at {presenceUrl}", presenceUrl);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(ClientPresence.PollSeconds));

            var wasAvailable = true;

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        var response = await httpClient.GetAsync(presenceUrl, stoppingToken);

                        if (!response.IsSuccessStatusCode)
                        {
                            if (wasAvailable)
                                _logger.LogWarning("presence poll returned {statusCode}, failing open until it recovers", (int)response.StatusCode);

                            wasAvailable = false;
                            _clientPresence.MarkUnavailable();
                            continue;
                        }

                        var body = JsonSerializer.Deserialize<ActiveClientsResponseDTO>(
                            await response.Content.ReadAsStringAsync(stoppingToken),
                            JsonOptions);

                        if (!wasAvailable)
                            _logger.LogInformation("presence poll recovered");

                        wasAvailable = true;
                        _clientPresence.Update(body?.Clients ?? new List<string>());
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        if (wasAvailable)
                            _logger.LogWarning(ex, "presence poll failed, failing open until it recovers");

                        wasAvailable = false;
                        _clientPresence.MarkUnavailable();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        private class ActiveClientsResponseDTO
        {
            public List<string> Clients { get; set; }
        }

        // Picks up clients added (or removed) in the Platform Client table without a
        // redeploy; newly added clients get their template/sort-order data loaded
        // before they start receiving fan-out.
        private async Task RefreshClientsLoop(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(ClientRefreshMinutes));

            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        var added = await _clientRegistry.RefreshAsync();
                        await LoadClientData(added);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "error refreshing client registry");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        private async Task LoadClientData(IReadOnlyList<ClientContext> clients)
        {
            await Task.WhenAll(clients.Select(async client =>
            {
                await _sportsbookRepository.ProcessMainMarketTemplates(client);
                await _sportsbookRepository.ProcessMarketSortOrder(client);
                await _sportsbookRepository.ProcessFixtureSortOrder(client);
                await _sportsbookRepository.ProcessBetBuilderMarketTemplates(client);
                await _sportsbookRepository.ProcessMarketTemplatePlayerProps(client);

                client.IsReady = true;
            }));
        }

        // Owning client for a client-suffixed routing key (r.bet.betstatus.gsa),
        // falling back to the ClientId GUID some payloads carry. Null means this
        // instance does not serve that client (e.g. sharded or mid-cutover) and the
        // message is skipped.
        private ClientContext ResolveClient(string routingKey, string clientId = null)
        {
            return _clientRegistry.ResolveByRoutingKey(routingKey) ?? _clientRegistry.ResolveByClientId(clientId);
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
                                _logger.LogInformation($"FixtureId:{onOddsChange.PrFiId} {msgType} Fixture closed");

                            _messagesMarketCount.Add(onOddsChange);
                            _messagesTopFixtures.Add(onOddsChange);
                            _messagesTopLeagues.Add(onOddsChange);
                            _messagesMainMarketsOddsChange.Add(onOddsChange);
                            _messagesAllMarketsOddsChange.Add(onOddsChange);
                            break;

                        case MessageType.ON_BET_STOP_GLOBAL:
                            var onBetStopGlobal = JsonSerializer.Deserialize<OnBetStopGlobalMessage>(payload);
                            await ProcessForAllClients(client => _sportsbookRepository.ProcessBetStopGlobal(client, onBetStopGlobal, _hubConnection));
                            break;
                        case MessageType.ON_BET_STOP_FIXTURE:
                            var onBetStopFixture = JsonSerializer.Deserialize<OnBetStopFixtureMessage>(payload);
                            await ProcessForAllClients(client => _sportsbookRepository.ProcessBetStopFixture(client, onBetStopFixture, _hubConnection));
                            break;

                        case "OnBetStatus":
                            var onBetStatus = JsonSerializer.Deserialize<IncomingBetStatus>(payload);
                            var betStatusClient = ResolveClient(e.RoutingKey, onBetStatus.ClientId);

                            if (betStatusClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesBetStatus.Add(new ClientScoped<IncomingBetStatus>(betStatusClient, onBetStatus));
                            break;

                        case "OnCashOutChange":
                            var onCashOutChange = JsonSerializer.Deserialize<IncomingCashOutChange>(payload);
                            var cashOutClient = ResolveClient(e.RoutingKey, onCashOutChange.ClientId);

                            if (cashOutClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesCashOut.Add(new ClientScoped<IncomingCashOutChange>(cashOutClient, onCashOutChange));
                            break;

                        case "OnWalletChange":
                            var onWalletChange = JsonSerializer.Deserialize<IncomingWalletChange>(payload);
                            var walletClient = ResolveClient(e.RoutingKey, onWalletChange.ClientId);

                            if (walletClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesWallet.Add(new ClientScoped<IncomingWalletChange>(walletClient, onWalletChange));
                            break;

                        case "OnVisitFavorites":
                            var onVisitFavorites = JsonSerializer.Deserialize<IncomingVisitFavorites>(payload);
                            var favoritesClient = ResolveClient(e.RoutingKey);

                            if (favoritesClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesFavorites.Add(new ClientScoped<IncomingVisitFavorites>(favoritesClient, onVisitFavorites));
                            break;
                        case "OnRemoveUserFavorites":
                            var onRemoveFavorites = JsonSerializer.Deserialize<IncomingVisitFavorites>(payload);
                            var removeFavoritesClient = ResolveClient(e.RoutingKey);

                            if (removeFavoritesClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesRemoveFavorites.Add(new ClientScoped<IncomingVisitFavorites>(removeFavoritesClient, onRemoveFavorites));
                            break;
                        case "OnVisitBoostedFixtures":
                            var onVisitBoostedFixtures = JsonSerializer.Deserialize<IncomingVisitBoostedFixtures>(payload);
                            var boostedClient = ResolveClient(e.RoutingKey);

                            if (boostedClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesBoostedFixtures.Add(new ClientScoped<IncomingVisitBoostedFixtures>(boostedClient, onVisitBoostedFixtures));
                            break;
                        case "OnPayableChange":
                            var onPayableChange = JsonSerializer.Deserialize<IncomingPayableChange>(payload);
                            var payableClient = ResolveClient(e.RoutingKey, onPayableChange.ClientId);

                            if (payableClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesPayable.Add(new ClientScoped<IncomingPayableChange>(payableClient, onPayableChange));
                            break;
                        case "OnMarketSortOrderChange":
                            var sortOrderClient = ResolveClient(e.RoutingKey);

                            if (sortOrderClient == null)
                            {
                                _logger.LogDebug("skipping {messageType} for unserved client (routingKey={routingKey})", msgType, e.RoutingKey);
                                break;
                            }

                            _messagesMarketSortOrder.Add(new ClientScoped<bool>(sortOrderClient, true));
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
