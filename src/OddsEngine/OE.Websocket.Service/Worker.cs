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

        // Upper bound on clients whose startup data (5 Platform API calls each) loads at
        // the same time. Unbounded fan-out at 500+ clients means 500 concurrent request
        // chains against the Platform API, which is how loads time out and clients end
        // up half-configured.
        internal const int LoadClientConcurrency = 16;

        private const string SkippingUnservedClientLogTemplate = "skipping {MessageType} for unserved client (routingKey={RoutingKey})";

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

        // Host stopping token, captured in ExecuteAsync so the Closed handler's manual
        // reconnect loop can stop retrying when the service shuts down.
        private CancellationToken _stoppingToken;

        private readonly ActivitySource _activitySource;
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        // Pairs a single-client message with the client resolved from its routing key
        // (or payload ClientId), so the dispatch threads do not have to re-resolve.
        private sealed record ClientScoped<T>(ClientContext Client, T Message);


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
        private readonly BlockingCollection<ClientScoped<bool>> _messagesWebPropsChange = new BlockingCollection<ClientScoped<bool>>();

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
        private async void ThreadWebPropsChange()
        {
            while (true)
            {
                var item = _messagesWebPropsChange.Take();
                await _sportsbookRepository.ProcessWebPropsChange(item.Client, _hubConnection);
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
            _logger.LogWarning(error, "hub connection lost, automatic reconnect in progress. State={State}", _hubConnection.State);
            return Task.CompletedTask;
        }

        private Task OnHubConnectionReconnected(string connectionId)
        {
            _logger.LogInformation("hub connection reconnected. connectionId: {ConnectionId}", connectionId);
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

            try
            {
                await ConnectHubWithRetryAsync(_stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }

        // Connects to the hub, retrying with exponential backoff (1s .. 30s) until it
        // succeeds or the service is stopping. Shared by startup and by the Closed
        // handler so both paths tolerate a hub that is down or restarting (GM-791: a
        // refused first connect used to fault ExecuteAsync and stop the host).
        private async Task ConnectHubWithRetryAsync(CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromSeconds(1);
            var maxDelay = TimeSpan.FromSeconds(30);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await _hubConnection.StartAsync(cancellationToken);
                    _logger.LogInformation("hub connection established. State={State}", _hubConnection.State);
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "hub connect attempt failed, retrying in {DelaySeconds}s", delay.TotalSeconds);
                }

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, maxDelay.TotalSeconds));
            }
        }

        // Dispatch threads block forever in BlockingCollection.Take(), so they MUST be
        // background threads. A foreground thread keeps the process alive after the
        // host has stopped, which is the GM-791 zombie: host stopped after a startup
        // failure, container stayed Running, nothing consumed, no restart.
        internal static Thread CreateDispatchThread(ThreadStart start, string name)
        {
            return new Thread(start)
            {
                IsBackground = true,
                Name = name
            };
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Cancels stoppingToken so the startup connect retry and the poll loops end.
                await base.StopAsync(cancellationToken);
            }
            finally
            {
                try
                {
                    await _hubConnection.DisposeAsync();

                    _messageBroker.Dispose();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error stopping service: {Message}", e.Message);
                }
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _stoppingToken = stoppingToken;

            // Do not start consuming until discovery has produced at least one client;
            // otherwise every message would be drained with nobody to serve.
            var clients = await _clientRegistry.RefreshAsync();

            while (_clientRegistry.Clients.Count == 0 && !stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("no clients discovered yet, retrying in 10s");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                clients = await _clientRegistry.RefreshAsync();
            }

            _logger.LogInformation("serving {ClientCount} client(s): {ClientCodes}",
                    _clientRegistry.Clients.Count, string.Join(",", _clientRegistry.Clients.Select(c => c.ClientCode)));

            await LoadClientData(clients);

            var threadTopFixtures = CreateDispatchThread(ThreadTopFixtures, "ws-top-fixtures");
            var threadTopLeagues = CreateDispatchThread(ThreadTopLeagues, "ws-top-leagues");
            var threadMarketCount = CreateDispatchThread(ThreadMarketCount, "ws-market-count");
            var threadMainMarketsOddsChange = CreateDispatchThread(ThreadMainMarketsOddsChange, "ws-main-markets-odds");
            var threadAllMarketsOddsChange = CreateDispatchThread(ThreadAllMarketsOddsChange, "ws-all-markets-odds");
            var threadBetStatus = CreateDispatchThread(ThreadBetStatus, "ws-bet-status");
            var threadCashOut = CreateDispatchThread(ThreadCashOut, "ws-cashout");
            var threadWallet = CreateDispatchThread(ThreadWallet, "ws-wallet");
            var threadFavorites = CreateDispatchThread(ThreadFavorites, "ws-favorites");
            var threadRemoveFavorites = CreateDispatchThread(ThreadRemoveFavorites, "ws-remove-favorites");
            var threadBoostedFixtures = CreateDispatchThread(ThreadBoostedFixtures, "ws-boosted-fixtures");
            var threadPayable = CreateDispatchThread(ThreadPayable, "ws-payable");
            var threadMarketSortOrderChange = CreateDispatchThread(ThreadMarketSortOrder, "ws-market-sort-order");
            var threadWebPropsChange = CreateDispatchThread(ThreadWebPropsChange, "ws-webprops-change");

            threadTopFixtures.Start();
            threadTopLeagues.Start();
            threadMarketCount.Start();
            threadMainMarketsOddsChange.Start();
            threadAllMarketsOddsChange.Start();
            threadBetStatus.Start();
            threadCashOut.Start();
            threadWallet.Start();
            threadFavorites.Start();
            threadRemoveFavorites.Start();
            threadBoostedFixtures.Start();
            threadPayable.Start();
            threadMarketSortOrderChange.Start();
            threadWebPropsChange.Start();

            // The hub is a separate deployment that may be down or restarting while this pod
            // starts. Keep retrying instead of faulting ExecuteAsync (which stops the host), and
            // only then start consuming, so RabbitMQ holds the backlog until we can push it.
            try
            {
                await ConnectHubWithRetryAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return; // shutting down before the hub came up
            }

            _logger.LogInformation("Connecting to broker/RabbitMQ");
            _messageBroker.Connect();

            _messageBroker.MessageReceived += MessageReceivedEvent;
            _logger.LogInformation("connected. processing incoming messages");

            try
            {
                var connectionId = await _hubConnection.InvokeAsync<string>("GetConnectionId", stoppingToken);
                _logger.LogInformation($"connectionId: {connectionId}");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Diagnostics only: the hub dropping right here must not stop the host.
                _logger.LogWarning(ex, "could not read hub connectionId");
            }

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

            _logger.LogInformation("polling presence at {PresenceUrl}", presenceUrl);

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

                        // Also retry clients whose template load failed earlier; until it
                        // succeeds they stay out of the odds fan-out (IsReady=false).
                        var notReady = _clientRegistry.Clients.Where(c => !c.IsReady && !added.Contains(c)).ToList();

                        await LoadClientData(added.Concat(notReady).ToList());
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

        // Loads each client's startup data with bounded parallelism. A client becomes
        // ready only once its main-market templates actually loaded: publishing with an
        // empty template list would silently strip every main market for that client.
        // Sort orders and bet-builder/player-props data have safe defaults when missing.
        private async Task LoadClientData(IReadOnlyList<ClientContext> clients)
        {
            if (clients.Count == 0)
                return;

            var options = new ParallelOptions { MaxDegreeOfParallelism = LoadClientConcurrency };

            await Parallel.ForEachAsync(clients, options, async (client, _) =>
            {
                var templatesLoaded = await _sportsbookRepository.ProcessMainMarketTemplates(client);
                await _sportsbookRepository.ProcessMarketSortOrder(client);
                await _sportsbookRepository.ProcessFixtureSortOrder(client);
                await _sportsbookRepository.ProcessBetBuilderMarketTemplates(client);
                await _sportsbookRepository.ProcessMarketTemplatePlayerProps(client);

                if (templatesLoaded)
                    client.IsReady = true;
            });

            var notReady = clients.Count(c => !c.IsReady);

            if (notReady > 0)
                _logger.LogWarning("{NotReady} of {Total} client(s) not ready (template load failed); retrying in {Minutes}m",
                        notReady, clients.Count, ClientRefreshMinutes);
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
        {
            var parentContext = Propagator.Extract(default, e.BasicProperties, ExtractTraceFromBasicProperties);
            Baggage.Current = parentContext.Baggage;

            var payload = Encoding.UTF8.GetString(e.Body);
            var msgType = e.BasicProperties.Type;

            _logger.LogInformation("{MessageType}:[{RoutingKey}] {@Payload}", msgType, e.RoutingKey, payload);

            using (var activity = _activitySource.StartActivity($"{e.RoutingKey} receive", ActivityKind.Consumer, parentContext.ActivityContext))
                try
                {
                    activity?.SetTag("message.type", msgType);

                    switch (msgType)
                    {
                        case MessageType.ON_ODDS_CHANGE:
                            var onOddsChange = JsonSerializer.Deserialize<OnMarketChangeMessage>(payload);

                            if (onOddsChange.FiStId == (int)FixtureStatus.Closed)
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
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesBetStatus.Add(new ClientScoped<IncomingBetStatus>(betStatusClient, onBetStatus));
                            break;

                        case "OnCashOutChange":
                            var onCashOutChange = JsonSerializer.Deserialize<IncomingCashOutChange>(payload);
                            var cashOutClient = ResolveClient(e.RoutingKey, onCashOutChange.ClientId);

                            if (cashOutClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesCashOut.Add(new ClientScoped<IncomingCashOutChange>(cashOutClient, onCashOutChange));
                            break;

                        case "OnWalletChange":
                            var onWalletChange = JsonSerializer.Deserialize<IncomingWalletChange>(payload);
                            var walletClient = ResolveClient(e.RoutingKey, onWalletChange.ClientId);

                            if (walletClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesWallet.Add(new ClientScoped<IncomingWalletChange>(walletClient, onWalletChange));
                            break;

                        case "OnVisitFavorites":
                            var onVisitFavorites = JsonSerializer.Deserialize<IncomingVisitFavorites>(payload);
                            var favoritesClient = ResolveClient(e.RoutingKey);

                            if (favoritesClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesFavorites.Add(new ClientScoped<IncomingVisitFavorites>(favoritesClient, onVisitFavorites));
                            break;
                        case "OnRemoveUserFavorites":
                            var onRemoveFavorites = JsonSerializer.Deserialize<IncomingVisitFavorites>(payload);
                            var removeFavoritesClient = ResolveClient(e.RoutingKey);

                            if (removeFavoritesClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesRemoveFavorites.Add(new ClientScoped<IncomingVisitFavorites>(removeFavoritesClient, onRemoveFavorites));
                            break;
                        case "OnVisitBoostedFixtures":
                            var onVisitBoostedFixtures = JsonSerializer.Deserialize<IncomingVisitBoostedFixtures>(payload);
                            var boostedClient = ResolveClient(e.RoutingKey);

                            if (boostedClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesBoostedFixtures.Add(new ClientScoped<IncomingVisitBoostedFixtures>(boostedClient, onVisitBoostedFixtures));
                            break;
                        case "OnPayableChange":
                            var onPayableChange = JsonSerializer.Deserialize<IncomingPayableChange>(payload);
                            var payableClient = ResolveClient(e.RoutingKey, onPayableChange.ClientId);

                            if (payableClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesPayable.Add(new ClientScoped<IncomingPayableChange>(payableClient, onPayableChange));
                            break;
                        case "OnMarketSortOrderChange":
                            var sortOrderClient = ResolveClient(e.RoutingKey);

                            if (sortOrderClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesMarketSortOrder.Add(new ClientScoped<bool>(sortOrderClient, true));
                            break;

                        case "OnWebPropsChange":
                            var webPropsClient = ResolveClient(e.RoutingKey);

                            if (webPropsClient == null)
                            {
                                _logger.LogDebug(SkippingUnservedClientLogTemplate, msgType, e.RoutingKey);
                                break;
                            }

                            _messagesWebPropsChange.Add(new ClientScoped<bool>(webPropsClient, true));
                            break;

                    }
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                catch (Exception exception)
                {

                    activity?.SetTag("stack.trace", exception.StackTrace);
                    activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
                    _logger.LogError(exception, "error processing message: {Trace}", exception.StackTrace);
                    // abruptly terminate the process and let the operating system
                    // supervisor handles its job of restarting it
                    System.Environment.Exit(1);
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
    }
}
