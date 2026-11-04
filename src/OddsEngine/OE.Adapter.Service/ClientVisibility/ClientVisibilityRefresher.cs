using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using System.Collections.Concurrent;

namespace OE.Adapter.Service.ClientVisibility
{
    // Backoffice saved a league setting (OnLeagueConfig): reload the lists and send that league to the shard even if
    // the reload finds no change, so a restriction saved while the adapter was down still reaches the stored fixtures.
    public interface ILeagueRefreshRequests
    {
        void Request(int sportId, int segmentId, int leagueId);
    }

    // Keeps the clients of every league current without waiting for the next odds change of each fixture. It reloads
    // the lists on a timer (the safety net for rows changed without a message) and as soon as a league is requested,
    // and for each league that changed or was requested it sends the new list to the OE.Sportsbook.Service shard that
    // does league-wide writes, which stamps it onto the stored fixtures and markets of the league.
    //
    // Messages that arrive from now on are stamped from the lists directly (TradingRepository), so only what is
    // already stored needs this. A reload that fails keeps the requests for the next try.
    public class ClientVisibilityRefresher : BackgroundService, ILeagueRefreshRequests
    {
        // the shard that also handles OnBetStopGlobal: league-wide messages are not tied to one fixture
        public const string GlobalShardInstance = "10";

        private readonly IClientVisibility _visibility;
        private readonly IMessageBroker _messageBroker;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ClientVisibilityRefresher> _logger;

        private readonly ConcurrentDictionary<LeagueKey, byte> _requested = new ConcurrentDictionary<LeagueKey, byte>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        public ClientVisibilityRefresher(IClientVisibility visibility, IMessageBroker messageBroker, IConfiguration configuration,
            ILogger<ClientVisibilityRefresher> logger)
        {
            _visibility = visibility;
            _messageBroker = messageBroker;
            _configuration = configuration;
            _logger = logger;
        }

        public void Request(int sportId, int segmentId, int leagueId)
        {
            // nothing runs the loop when the lists are not configured, so nothing would ever take the request
            if (!_visibility.IsConfigured)
                return;

            _requested[new LeagueKey(sportId, segmentId, leagueId)] = 0;
            _signal.Release();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_visibility.IsConfigured)
                return;

            var interval = TimeSpan.FromSeconds(Math.Max(1, _configuration.GetValue("Custom:ClientVisibilityRefreshSeconds", 60)));

            // a save of many leagues sends one message each, which one reload serves
            var debounce = TimeSpan.FromMilliseconds(Math.Max(0, _configuration.GetValue("Custom:ClientVisibilityDebounceMilliseconds", 1000)));

            _logger.LogInformation("Client visibility refresh started. Interval {RefreshSeconds}s", interval.TotalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (await _signal.WaitAsync(interval, stoppingToken))
                    {
                        await Task.Delay(debounce, stoppingToken);

                        while (_signal.Wait(0))
                        {
                        }
                    }

                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // never end the loop: the next tick tries again
                    _logger.LogError(ex, "Client visibility refresh failed");
                }
            }
        }

        // One reload and the sends that follow it. Separate from the loop so that it is exercised without waiting.
        public async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            var requested = new List<LeagueKey>(_requested.Keys);
            foreach (var league in requested)
                _requested.TryRemove(league, out _);

            IReadOnlyList<LeagueKey> changed;

            try
            {
                changed = await _visibility.RefreshAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reading the client visibility failed, keeping the lists in use and trying the {RequestedCount} requested leagues again at the next refresh", requested.Count);

                foreach (var league in requested)
                    _requested[league] = 0;

                return;
            }

            foreach (var league in requested.Union(changed))
                Send(league);
        }

        private void Send(LeagueKey league)
        {
            var clientCodes = _visibility.ClientCodesFor(league.SportId, league.SegmentId, league.LeagueId);
            if (clientCodes == null)
                return;

            try
            {
                // on its own channel: this runs on a timer thread, not the consumer thread that owns the shared one
                _messageBroker.CPublish(
                    new OnLeagueClientsChangeMessage
                    {
                        SportId = league.SportId,
                        SegmentId = league.SegmentId,
                        LeagueId = league.LeagueId,
                        ClientCodes = clientCodes
                    },
                    MessageType.ON_LEAGUE_CLIENTS_CHANGE,
                    $"r.oe.adapter.{MessageType.ON_LEAGUE_CLIENTS_CHANGE.ToLower()}.{GlobalShardInstance}",
                    Guid.NewGuid().ToString());

                _logger.LogInformation("League {SportId}:{SegmentId}:{LeagueId} sent to the shard with {ClientCount} clients",
                    league.SportId, league.SegmentId, league.LeagueId, clientCodes.Length == 0 ? 0 : clientCodes.Split('|').Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not send league {SportId}:{SegmentId}:{LeagueId} to the shard, it is tried again at the next refresh",
                    league.SportId, league.SegmentId, league.LeagueId);

                _requested[league] = 0;
            }
        }
    }
}
