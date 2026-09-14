using Microsoft.AspNetCore.SignalR.Client;
using OE.Shared.Enums;
using OE.Shared.Extensions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Messages;
using OE.WebSocket.Service.Telemetry;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OE.WebSocket.Service.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {
        #region Fields

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly bool _isOEEnabled;

        private readonly IConfiguration _configuration;
        private readonly IMemCache _memCache;
        private readonly ILogger<SportsbookRepository> _logger;

        private readonly ActivitySource _activitySource;
        private readonly Counter<long> _publishedToWebSocketCounter;

        private readonly List<int> _fixtureStatus = new List<int> { (int)FixtureStatus.Prematch, (int)FixtureStatus.Live };

        private readonly List<int> _marketStatusToRemove = new List<int> { (int)MarketStatus.Inactive, (int)MarketStatus.Cancelled, (int)MarketStatus.Settled, (int)MarketStatus.HandedOver };
        private readonly List<int> _marketStatusToAdd = new List<int> { (int)MarketStatus.Active, (int)MarketStatus.Suspended };

        #endregion

        #region Ctor

        public SportsbookRepository(IConfiguration configuration, IMemCache memCache, Instrumentation instrumentation, ILogger<SportsbookRepository> logger)
        {
            _logger = logger;
            _configuration = configuration;
            _memCache = memCache;
            _activitySource = instrumentation.ActivitySource;
            _publishedToWebSocketCounter = instrumentation.PublishedToWebSocketCounter;

            _isOEEnabled = Convert.ToBoolean(_configuration["Custom:IsOEEnabled"]);
        }

        #endregion

        #region Methods

        public async Task ProcessOddsChangeForMainMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection)
        {
            try
            {
                var mainMarkets = client.MainMarketTemplates.Where(c => c.Item1 == message.SpId).FirstOrDefault() ?? new Tuple<int, List<string>>(message.SpId, new List<string>());

                var finalMarkets = new List<MarketMessage>();

                if (message.FiTyId == (int)FixtureType.Match || (message.SpId.IsVirtualSportId() && message.FiTyId == (int)FixtureType.Outright))
                    finalMarkets = message.Ma.Where(c => mainMarkets.Item2.Contains(c.MaTeId)).ToList();
                else
                    finalMarkets = message.Ma;

                _logger.LogDebug("ProcessOddsChangeForMainMarkets: {@finalMarkets}", JsonSerializer.Serialize(finalMarkets));
                _logger.LogDebug("ProcessOddsChangeForMainMarkets: {@markets}", JsonSerializer.Serialize(message.Ma));

                if (message.SpId.IsVirtualSportId() || message.FiTyId == (int)FixtureType.Outright)
                {
                    await ProcessMatchOddsChangeForMainMarkets(client, message, hubConnection, finalMarkets);
                }
                else
                {
                    if (_isOEEnabled)
                        await ProcessMatchOddsChangeForMainMarketsOE(client, message, hubConnection, finalMarkets);
                    else
                        await ProcessMatchOddsChangeForMainMarkets(client, message, hubConnection, finalMarkets);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing odds change for main markets");
            }
        }

        public async Task ProcessOddsChangeForAllMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection)
        {
            try
            {
                if (message.SpId.IsVirtualSportId() || message.FiTyId == (int)FixtureType.Outright)
                {
                    await ProcessMatchOddsChangeForAllMarkets(client, message, hubConnection);
                }
                else
                {
                    if (_isOEEnabled)
                        await ProcessMatchOddsChangeForAllMarketsOE(client, message, hubConnection);
                    else
                        await ProcessMatchOddsChangeForAllMarkets(client, message, hubConnection);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing oddschange for all markets");
            }
        }

        public async Task ProcessBetStopGlobal(ClientContext client, OnBetStopGlobalMessage message, HubConnection hubConnection)
        {
            try
            {
                var payload = new OutgoingBetStopGlobal
                {
                    IsBeSt = message.IsBetStop
                };

                if (_publishedToWebSocketCounter.Enabled)
                    _publishedToWebSocketCounter.Add(1,
                            new KeyValuePair<string, object?>("method", "PublishBetStopGlobal"),
                            new KeyValuePair<string, object?>("client.code", client.ClientCode));
                await PublishToWebSocket(client, "PublishBetStopGlobal", hubConnection, payload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing bet stop global");
            }
        }

        public async Task ProcessBetStopFixture(ClientContext client, OnBetStopFixtureMessage message, HubConnection hubConnection)
        {
            try
            {
                _logger.LogInformationEx($"FixtureId:{message.PrFiId} Incoming OnBetStopFixtureMessage --> {JsonSerializer.Serialize(message)}");

                var payload = new OutgoingBetStopFixture
                {
                    Gr = GetGroups(client, message),
                    Pa = new BetStopFixture
                    {
                        FiId = message.FiId.ToString(),
                        IsBeSt = message.IsBetStop,
                        SeAcMaToIn = message.SetActiveMarketsToInactive
                    }
                };

                if (_publishedToWebSocketCounter.Enabled)
                    _publishedToWebSocketCounter.Add(1,
                            new KeyValuePair<string, object?>("method", "PublishBetStopFixture"),
                            new KeyValuePair<string, object?>("client.code", client.ClientCode));
                await PublishToWebSocket(client, "PublishBetStopFixture", hubConnection, payload);

            }
            catch (Exception e)
            {
                _logger.LogError(e, $"FixtureId:{message.PrFiId} Incoming OnBetStopFixtureMessage: error processing bet stop fixture");
            }
        }

        public async Task ProcessBetStatus(ClientContext client, IncomingBetStatus message, HubConnection hubConnection)
        {
            try
            {
                var payload = new OutgoingBetStatus
                {
                    UsId = message.UserId,
                    BeId = message.BetId.ToUpper(),
                    BeStId = (int)message.BetStatus,
                    BeStNa = message.BetStatus.ToString(),
                    Me = string.IsNullOrEmpty(message.Message) ? string.Empty : message.Message
                };

                if (_publishedToWebSocketCounter.Enabled)
                    _publishedToWebSocketCounter.Add(1,
                            new KeyValuePair<string, object?>("method", "PublishBetStatus"),
                            new KeyValuePair<string, object?>("client.code", client.ClientCode));
                await PublishToWebSocket(client, "PublishBetStatus", hubConnection, payload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing bet status");
            }
        }

        public async Task ProcessCashOutChange(ClientContext client, IncomingCashOutChange message, HubConnection hubConnection)
        {
            try
            {
                var payload = new OutgoingCashOutChange
                {
                    UsId = message.UserId,
                    BeId = message.BetId.ToUpper(),
                    CaOuStId = (int)message.CashOutStatus,
                    CaOuStNa = message.CashOutStatus.ToString(),
                    CaOuSt = message.CashOutStake
                };

                if (_publishedToWebSocketCounter.Enabled)
                    _publishedToWebSocketCounter.Add(1,
                            new KeyValuePair<string, object?>("method", "PublishCashOutChange"),
                            new KeyValuePair<string, object?>("client.code", client.ClientCode));
                await PublishToWebSocket(client, "PublishCashOutChange", hubConnection, payload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessCashOutChange");
            }
        }

        public async Task ProcessWalletChange(ClientContext client, IncomingWalletChange message, HubConnection hubConnection)
        {
            try
            {
                var payload = new OutgoingWalletChange
                {
                    UsId = message.UserId,
                    Ba = message.Balance
                };

                if (_publishedToWebSocketCounter.Enabled)
                    _publishedToWebSocketCounter.Add(1,
                            new KeyValuePair<string, object?>("method", "PublishWalletChange"),
                            new KeyValuePair<string, object?>("client.code", client.ClientCode));
                await PublishToWebSocket(client, "PublishWalletChange", hubConnection, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessWalletChange");
            }
        }

        public async Task ProcessTopFixtures(ClientContext client, OnMarketChangeMessage message)
        {
            try
            {
                if (message.FiTyId == (int)FixtureType.Outright) return;
                if (message.SpId.IsVirtualSportId()) return;

                var isTopFixtureExisting = _memCache.GetCache<TopCache>($"{client.ClientCode}:TF");

                if (isTopFixtureExisting == null)
                {
                    _memCache.AddOrUpdateCache($"{client.ClientCode}:TF", new TopCache { Id = 0 }, DateTimeOffset.UtcNow.AddMinutes(5));

                    var result = await GetTopFixtures(client);

                    if (result.StatusCode == StatusCode.Success)
                    {
                        if (result.Result.Fixtures.Count > 0)
                            _memCache.AddOrUpdateCache($"{client.ClientCode}:TFL", new TopFixture { FiIds = result.Result.Fixtures.Select(s => Convert.ToInt64(s.FiId)).ToList() }, DateTimeOffset.UtcNow.AddHours(1));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessTopFixtures");
            }
        }

        public async Task ProcessTopLeagues(ClientContext client, OnMarketChangeMessage message)
        {
            try
            {
                if (message.FiTyId == (int)FixtureType.Outright) return;
                if (message.SpId.IsVirtualSportId()) return;

                var isTopFixtureExisting = _memCache.GetCache<TopCache>($"{client.ClientCode}:TL:{message.SpId}");

                if (isTopFixtureExisting == null)
                {
                    _memCache.AddOrUpdateCache($"{client.ClientCode}:TL:{message.SpId}", new TopCache { Id = 0 }, DateTimeOffset.UtcNow.AddMinutes(5));

                    var result = await GetTopLeagues(client, message.SpId);

                    if (result.StatusCode == StatusCode.Success)
                    {
                        if (result.Result.TopLeagues.Count > 0)
                            _memCache.AddOrUpdateCache($"{client.ClientCode}:TLL:{message.SpId}", new TopLeague { SegmentLeagueIds = result.Result.TopLeagues.Select(s => new Tuple<int, int>(s.SegmentId, s.LeagueId)).ToList() }, DateTimeOffset.UtcNow.AddHours(1));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessTopLeagues");
            }
        }

        public async Task ProcessMarketCount(ClientContext client, OnMarketChangeMessage message)
        {
            using var activity = _activitySource.StartActivity("ProcessMarketCount");
            try
            {
                var clientId = client.ClientId;

                activity?.SetTag("client.id", client.ClientId);
                activity?.SetTag("fixture.id", message.FiId.ToString());
                activity?.SetTag("fixture.name", message.FiNa);
                activity?.SetTag("fixture.status", message.FiStNa);
                var key = $"{client.ClientCode}:mc:{message.FiId}";

                if (!_fixtureStatus.Contains(message.FiStId))
                {
                    _memCache.DeleteCache(key);
                    return;
                }

                var marketToRemove = message.Ma.Where(c => _marketStatusToRemove.Contains(c.MaStId)).Select(s => s.MaId).ToHashSet();
                var marketToAdd = _isOEEnabled == false || message.SpId.IsVirtualSportId() || message.FiTyId == (int)FixtureType.Outright
                    ? message.Ma.Where(c => _marketStatusToAdd.Contains(c.MaStId)).Select(s => s.MaId).ToHashSet()
                    : message.Ma.Where(c => _marketStatusToAdd.Contains(c.MaStId) && c.ClCo.Contains(client.ClientCode.ToUpper())).Select(s => s.MaId).ToHashSet();

                var cache = _memCache.GetCache<MarketCount>(key);

                HashSet<long> currentMarketIds;

                // Runs once per client per odds change: at 500 clients this line alone is
                // 500 log entries and 500 JSON serializations per message, so Debug-only.
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("ProcessMarketCount {clientId} {fixtureId} {fixtureName} {fixtureStatus} {cache}", clientId, message.FiId.ToString(), message.FiNa, message.FiStNa, cache == null ? "Null" : JsonSerializer.Serialize(cache));

                if (cache == null)
                {
                    var result = await GetFixtureDetails(client, message.FiId);
                    if (result.StatusCode != StatusCode.Success)
                    {
                        _logger.LogError("ProcessMarketCount {clientId} {fixtureId} {fixtureName} {fixtureStatus} {fixturedetailsStatus} {resultMessage}", clientId, message.FiId.ToString(), message.FiNa, message.FiStNa, result.StatusCode, result.Message);
                        activity?.SetStatus(ActivityStatusCode.Error, result.Message);
                        return;
                    }

                    var fixture = result.Result.Fixtures.FirstOrDefault();
                    if (fixture == null)
                        return;

                    currentMarketIds = fixture.Markets.Select(m => Convert.ToInt64(m.MarketId)).ToHashSet();
                }
                else
                {
                    currentMarketIds = cache.MarketIds.ToHashSet();

                }

                currentMarketIds.UnionWith(marketToAdd);
                currentMarketIds.ExceptWith(marketToRemove);

                var changed = cache == null || !currentMarketIds.SetEquals(cache?.MarketIds ?? Enumerable.Empty<long>());
                if (changed)
                {
                    _logger.LogDebug("ProcessMarketCount {fixtureId} {fixtureName} {fixtureStatus} {marketCount}", message.FiId, message.FiNa, message.FiStNa, currentMarketIds.Count);
                    _memCache.AddOrUpdateCache(key, new MarketCount { FiId = message.FiId, MarketIds = currentMarketIds.ToList() }, DateTimeOffset.UtcNow.AddDays(7));
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessMarketCount");
            }
        }

        public async Task ProcessFavorites(ClientContext client, IncomingVisitFavorites message)
        {
            try
            {
                foreach (var favorite in message.Favorites)
                {
                    string key;

                    if (favorite.HierarchyId == 2)
                        key = $"{client.ClientCode}:fli:{favorite.SportId}:{favorite.Id}";
                    else
                        key = $"{client.ClientCode}:ffi:{favorite.SportId}:{favorite.Id}";

                    var cache = _memCache.GetCache<Favorites>(key);

                    if (cache == null)
                    {
                        var toAdd = new Favorites
                        {
                            UserIds = new List<string>
                            {
                                message.UserId
                            }
                        };

                        _memCache.AddOrUpdateCache(key, toAdd, DateTimeOffset.UtcNow.AddDays(1));
                    }
                    else
                    {
                        if (cache.UserIds.Contains(message.UserId) == false)
                        {
                            cache.UserIds.Add(message.UserId);

                            _memCache.AddOrUpdateCache(key, cache, DateTimeOffset.UtcNow.AddDays(1));
                        }
                    }
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessFavorites");
            }
        }

        public async Task ProcessRemoveFavorites(ClientContext client, IncomingVisitFavorites message)
        {
            try
            {
                foreach (var favorite in message.Favorites)
                {
                    string key;

                    if (favorite.HierarchyId == 2)
                        key = $"{client.ClientCode}:fli:{favorite.SportId}:{favorite.Id}";
                    else
                        key = $"{client.ClientCode}:ffi:{favorite.SportId}:{favorite.Id}";

                    var cache = _memCache.GetCache<Favorites>(key);

                    if (cache != null)
                    {
                        if (cache.UserIds.Contains(message.UserId))
                        {
                            cache.UserIds.Remove(message.UserId);

                            _memCache.AddOrUpdateCache(key, cache, DateTimeOffset.UtcNow.AddDays(1));
                        }
                    }
                }

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessRemoveFavorites");
            }
        }

        public async Task ProcessBoostedFixtures(ClientContext client, IncomingVisitBoostedFixtures message)
        {
            try
            {
                _memCache.AddOrUpdateCache($"{client.ClientCode}:bf", message.FixtureIds, DateTimeOffset.UtcNow.AddDays(1));

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessBoostedFixtures");
            }
        }

        // Returns true only when the templates were loaded from the Platform API; the
        // Worker uses this to decide whether the client may join the odds fan-out.
        public async Task<bool> ProcessMainMarketTemplates(ClientContext client)
        {
            try
            {
                var result = await GetSelectorMarketTemplates(client);

                if (!HasResult(result, client, nameof(ProcessMainMarketTemplates)))
                    return false;

                client.MainMarketTemplates.Clear();

                foreach (var item in result.Result.GroupBy(g => g.SportId).ToList())
                {
                    var marketTemplates = item.SelectMany(s => s.MarketTemplates).Select(s => s.MaTeId).Distinct().ToList();
                    client.MainMarketTemplates.Add(new Tuple<int, List<string>>(item.Key, marketTemplates));
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("ProcessMainMarketTemplates: {@marketTemplates}", JsonSerializer.Serialize(client.MainMarketTemplates));

                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessMainMarketTemplates");
                return false;
            }
        }

        public async Task ProcessMarketSortOrder(ClientContext client)
        {
            try
            {
                var result = await GetMarketSortOrder(client);

                if (!HasResult(result, client, nameof(ProcessMarketSortOrder)))
                    return;

                foreach (var item in result.Result.MarketTemplateGroups)
                {
                    var key = string.Empty;

                    if (item.FiStId == 0)
                        key = $"{client.ClientCode}:pmso:{item.SportId}";
                    else
                        key = $"{client.ClientCode}:lmso:{item.SportId}";

                    _memCache.AddOrUpdateCache(key, new ClientMarketSortOrder { MaTeIds = item.MaTeIds.Split(',').ToList() }, DateTimeOffset.UtcNow.AddYears(1));
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessMarketSortOrder");
            }
        }

        public async Task ProcessFixtureSortOrder(ClientContext client)
        {
            try
            {
                var result = await GetFixtureSortOrder(client);

                if (!HasResult(result, client, nameof(ProcessFixtureSortOrder)))
                    return;

                foreach (var item in result.Result.Fixtures)
                    _memCache.AddOrUpdateCache($"{client.ClientCode}:fso:{item.SportId}:{item.SegmentId}:{item.LeagueId}", new FixtureSortOrder { SortOrder = item.SortOrder }, DateTimeOffset.UtcNow.AddYears(1));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessFixtureSortOrder");
            }
        }

        public async Task ProcessBetBuilderMarketTemplates(ClientContext client)
        {
            try
            {
                var result = await GetBetBuilderMarketTemplatesDTO(client);

                if (!HasResult(result, client, nameof(ProcessBetBuilderMarketTemplates)))
                    return;

                var key = $"{client.ClientCode}:bbmt";

                _memCache.AddOrUpdateCache(key, result.Result, DateTimeOffset.UtcNow.AddYears(1));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessBetBuilderMarketTemplates");
            }
        }

        public async Task ProcessMarketTemplatePlayerProps(ClientContext client)
        {
            try
            {
                var result = await GetMarketTemplatePlayerPropsDTO(client);

                if (!HasResult(result, client, nameof(ProcessMarketTemplatePlayerProps)))
                    return;

                var key = $"{client.ClientCode}:mtpp";

                _memCache.AddOrUpdateCache(key, result.Result, DateTimeOffset.UtcNow.AddYears(1));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessMarketTemplatePlayerProps");
            }
        }

        public async Task ProcessPayableChange(ClientContext client, IncomingPayableChange message, HubConnection hubConnection)
        {
            try
            {
                var payload = new OutgoingPayableChange
                {
                    UsId = message.UserId,
                    Pa = message.Payable
                };

                if (_publishedToWebSocketCounter.Enabled)
                    _publishedToWebSocketCounter.Add(1,
                            new KeyValuePair<string, object?>("method", "PublishPayableChange"),
                            new KeyValuePair<string, object?>("client.code", client.ClientCode));
                await PublishToWebSocket(client, "PublishPayableChange", hubConnection, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessPayableChange");
            }
        }

        public async Task ProcessWebPropsChange(ClientContext client, HubConnection hubConnection)
        {
            try
            {
                var payload = new OutgoingWebPropsChange { Ch = true };

                if (_publishedToWebSocketCounter.Enabled)
                    _publishedToWebSocketCounter.Add(1,
                            new KeyValuePair<string, object?>("method", "PublishWebPropsChange"),
                            new KeyValuePair<string, object?>("client.code", client.ClientCode));
                await PublishToWebSocket(client, "PublishWebPropsChange", hubConnection, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "error ProcessWebPropsChange");
            }
        }

        #endregion

        #region Utilities

        private async Task PublishToWebSocket<T>(ClientContext client, string method, HubConnection hubConnection, T payload)
        {
            try
            {
                await hubConnection.SendAsync(method, client.ClientCode, payload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error publishing {method} to websocket hub. HubState={hubState}", method, hubConnection.State);
            }
        }

        // The startup loaders used to dereference result.Result blindly, so a Platform API
        // error or an empty body surfaced as a NullReference/ArgumentNull stack trace per
        // client per endpoint (96 lines in the GM-791 log). Log the API's own status and
        // message once, and let the caller skip.
        private bool HasResult<T>(BaseResponseDTO<T> result, ClientContext client, string operation)
        {
            if (result != null && result.StatusCode == StatusCode.Success && result.Result != null)
                return true;

            _logger.LogWarning("{operation} skipped for {clientCode}: Platform API returned {statusCode} {message}",
                    operation, client.ClientCode, result?.StatusCode, result?.Message);

            return false;
        }

        private async Task<BaseResponseDTO<TWResponseDTO>> GetTopFixtures(ClientContext client)
        {
            try
            {
                var response = await client.HttpClient.GetAsync($"v2/sportsbook/topfixtures");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<TWResponseDTO>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<TWResponseDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private async Task<BaseResponseDTO<TopLeaguesResponseDTO>> GetTopLeagues(ClientContext client, int sportId)
        {
            try
            {
                var response = await client.HttpClient.GetAsync($"v1/sportsbook/sports/topleagues?sportId={sportId}");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<TopLeaguesResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<TopLeaguesResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<TopLeaguesResponseDTO>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<TopLeaguesResponseDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private async Task<BaseResponseDTO<TWResponseDTO>> GetFixtureDetails(ClientContext client, long FiId)
        {
            try
            {
                var response = await client.HttpClient.GetAsync($"v1/sportsbook/fixturedetails?fixtureId={FiId}");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        var headers = string.Join("; ", client.HttpClient.DefaultRequestHeaders.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
                        Uri requestUri = client.HttpClient.BaseAddress != null
                            ? new Uri(client.HttpClient.BaseAddress, $"v1/sportsbook/fixturedetails?fixtureId={FiId}")
                            : new Uri($"v1/sportsbook/fixturedetails?fixtureId={FiId}", UriKind.Relative);
                        client.HttpClient.DefaultRequestHeaders.TryGetValues("x-api-key", out var apiKey);

                        _logger.LogError("GetFixtureDetails {key} {fixtureId} {FiIdreqUrl} {reqHeader} {reasonPhrase}", apiKey?.FirstOrDefault(), FiId.ToString(), requestUri.ToString(), headers, response.ReasonPhrase);

                        return new BaseResponseDTO<TWResponseDTO>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<TWResponseDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private async Task<BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>> GetSelectorMarketTemplates(ClientContext client)
        {
            try
            {
                var response = await client.HttpClient.GetAsync($"v1/sportsbook/selectormarkettemplates");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private BetBuilderMarketTemplatesDTO GetBetBuilderMarketTemplates(ClientContext client)
        {
            return _memCache.GetCache<BetBuilderMarketTemplatesDTO>($"{client.ClientCode}:bbmt") ?? new BetBuilderMarketTemplatesDTO();
        }

        private List<MarketTemplatePlayerPropsResponseDTO> GetMarketTemplatePlayerProps(ClientContext client)
        {
            return _memCache.GetCache<List<MarketTemplatePlayerPropsResponseDTO>>($"{client.ClientCode}:mtpp") ?? new List<MarketTemplatePlayerPropsResponseDTO>();
        }

        private async Task<BaseResponseDTO<BetBuilderMarketTemplatesDTO>> GetBetBuilderMarketTemplatesDTO(ClientContext client)
        {
            try
            {
                var response = await client.HttpClient.PostAsync($"v1/sportsbook/betbuilder/markettemplates", new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<BetBuilderMarketTemplatesDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<BetBuilderMarketTemplatesDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<BetBuilderMarketTemplatesDTO>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<BetBuilderMarketTemplatesDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private async Task<BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>> GetMarketSortOrder(ClientContext client)
        {
            try
            {
                var response = await client.HttpClient.GetAsync($"v1/sportsbook/marketsortorder");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private async Task<BaseResponseDTO<ClientFixtureSortOrderResponseDTO>> GetFixtureSortOrder(ClientContext client)
        {
            try
            {
                var response = await client.HttpClient.GetAsync($"v1/sportsbook/fixturesortorder");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<ClientFixtureSortOrderResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<ClientFixtureSortOrderResponseDTO>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<ClientFixtureSortOrderResponseDTO>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<ClientFixtureSortOrderResponseDTO>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private async Task<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>> GetMarketTemplatePlayerPropsDTO(ClientContext client)
        {
            try
            {
                var response = await client.HttpClient.GetAsync($"v1/sportsbook/client/markettemplateplayerprops");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>>(await response.Content.ReadAsStringAsync(), JsonOptions);
                    else
                    {
                        return new BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>
                        {
                            StatusCode = StatusCode.Error,
                            Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({response.ReasonPhrase})"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                return new BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>
                {
                    StatusCode = StatusCode.Error,
                    Message = $"{StatusCode.Error.ToString().Replace('_', ' ')} ({ex.Message})"
                };
            }
        }

        private int GetMarketCount(ClientContext client, long FiId)
        {
            var key = $"{client.ClientCode}:mc:{FiId}";

            var cache = _memCache.GetCache<MarketCount>(key);

            if (cache == null)
                return 0;
            else
                return cache.MarketIds.Count();
        }

        private List<Tuple<string, int>> GetMarketSortOrder(ClientContext client, List<MarketMessage> markets, int sportId, int FiStId)
        {
            var result = new List<Tuple<string, int>>();

            if (markets.Count == 0)
                return result;

            ClientMarketSortOrder cache = null;

            if (FiStId == 0)
                cache = _memCache.GetCache<ClientMarketSortOrder>($"{client.ClientCode}:pmso:{sportId}");

            if (FiStId == 1)
                cache = _memCache.GetCache<ClientMarketSortOrder>($"{client.ClientCode}:lmso:{sportId}");

            if (cache != null)
            {
                foreach (var market in markets)
                {
                    var sortOrder = cache.MaTeIds.FindIndex(c => c == market.MaTeId);

                    if (sortOrder == -1)
                        sortOrder = Convert.ToInt32(market.MaTeId) + 1000;
                    else
                        sortOrder += 1;

                    result.Add(new Tuple<string, int>(market.MaTeId, sortOrder));
                }
            }

            return result;
        }

        private int GetFixtureSortOrder(ClientContext client, int sportId, int segmentId, int leagueId)
        {
            var result = _memCache.GetCache<FixtureSortOrder>($"{client.ClientCode}:fso:{sportId}:{segmentId}:{leagueId}");

            if (result == null)
                return 1000;

            return result.SortOrder;
        }

        private static string GetHomeName(string name, FixtureType fixtureType) => fixtureType == FixtureType.Match ? name.Split(" vs. ").First().Trim() : string.Empty;

        private static string GetAwayName(string name, FixtureType fixtureType) => fixtureType == FixtureType.Match ? name.Split(" vs. ").Last().Trim() : string.Empty;

        private static List<ClientOdds> GetClientOdds(int sportId, int activeSelectionCount, decimal margin, List<SelectionMessage> selections, string clientMarketConfig, string clientCode, string MaTeId, List<MarketTemplatePlayerPropsResponseDTO> marketTemplateGroups)
        {
            if (string.IsNullOrEmpty(clientMarketConfig))
                return new List<ClientOdds>();

            if (!clientMarketConfig.Contains(clientCode) && clientCode.ToUpper() != "OE")
                return new List<ClientOdds>();

            List<Tuple<string, decimal>> clients;

            if (clientCode.ToUpper() == "OE")
                clients = clientMarketConfig.Split('|').Select(s => new Tuple<string, decimal>(s.Split(':').First(), Convert.ToDecimal(s.Split(':').Last()))).ToList();
            else
                clients = clientMarketConfig.Split('|').Select(s => new Tuple<string, decimal>(s.Split(':').First(), Convert.ToDecimal(s.Split(':').Last()))).Where(c => c.Item1 == clientCode).ToList();

            var result = new List<ClientOdds>();
            var isBinary = selections.Count > 2 ? false : true;

            // prepare market player props
            var playerPropMaTeIds = string.Join(",", marketTemplateGroups.Select(s => s.MarketTemplates).ToList()).Split(",").ToList();

            foreach (var client in clients)
            {
                foreach (var selection in selections)
                {
                    if (activeSelectionCount == 1)
                        activeSelectionCount = selections.Count;

                    if (selection.IsAc == true && selection.Od > 0m)
                    {
                        var targetMargin = client.Item2;

                        if (playerPropMaTeIds.Contains(MaTeId))
                        {
                            if (selection.SeTeId != "12" && selection.SeTeId != "13")
                            {
                                var tempOdds = Math.Round(selection.Od - (selection.Od * targetMargin), 2);

                                result.Add(new ClientOdds
                                {
                                    ClientCode = client.Item1,
                                    DecimalOdds = tempOdds,
                                    MalaysianOdds = GetMalayOdds(tempOdds, isBinary),
                                    HongkongOdds = GetHongkongOdds(tempOdds, isBinary),
                                    IndonesianOdds = GetIndoOdds(tempOdds, isBinary),
                                    SelectionTemplateId = selection.SeTeId,
                                    SelectionId = selection.SeId
                                });

                                continue;
                            }
                        }

                        if (margin == targetMargin)
                        {
                            result.Add(new ClientOdds
                            {
                                ClientCode = client.Item1,
                                DecimalOdds = selection.Od,
                                MalaysianOdds = GetMalayOdds(selection.Od, isBinary),
                                HongkongOdds = GetHongkongOdds(selection.Od, isBinary),
                                IndonesianOdds = GetIndoOdds(selection.Od, isBinary),
                                SelectionTemplateId = selection.SeTeId,
                                SelectionId = selection.SeId
                            });

                            continue;
                        }

                        var fairOddsInitialDivideBy = activeSelectionCount - (margin * selection.Od);

                        if (fairOddsInitialDivideBy <= 0)
                        {
                            var tempOdds = GetMasterOddsWithAddedMargin(selection.Od);

                            result.Add(new ClientOdds
                            {
                                ClientCode = client.Item1,
                                DecimalOdds = tempOdds,
                                MalaysianOdds = GetMalayOdds(tempOdds, isBinary),
                                HongkongOdds = GetHongkongOdds(tempOdds, isBinary),
                                IndonesianOdds = GetIndoOdds(tempOdds, isBinary),
                                SelectionTemplateId = selection.SeTeId,
                                SelectionId = selection.SeId
                            });

                            continue;
                        }

                        var fairOdds = (activeSelectionCount * selection.Od) / fairOddsInitialDivideBy;

                        if (fairOdds <= 0)
                        {
                            var tempOdds = GetMasterOddsWithAddedMargin(selection.Od);

                            result.Add(new ClientOdds
                            {
                                ClientCode = client.Item1,
                                DecimalOdds = tempOdds,
                                MalaysianOdds = GetMalayOdds(tempOdds, isBinary),
                                HongkongOdds = GetHongkongOdds(tempOdds, isBinary),
                                IndonesianOdds = GetIndoOdds(tempOdds, isBinary),
                                SelectionTemplateId = selection.SeTeId,
                                SelectionId = selection.SeId
                            });

                            continue;
                        }

                        var finalOddsInitialDivideBy = ((fairOdds * targetMargin) / activeSelectionCount) + 1;

                        if (finalOddsInitialDivideBy <= 0)
                        {
                            var tempOdds = GetMasterOddsWithAddedMargin(selection.Od);

                            result.Add(new ClientOdds
                            {
                                ClientCode = client.Item1,
                                DecimalOdds = tempOdds,
                                MalaysianOdds = GetMalayOdds(tempOdds, isBinary),
                                HongkongOdds = GetHongkongOdds(tempOdds, isBinary),
                                IndonesianOdds = GetIndoOdds(tempOdds, isBinary),
                                SelectionTemplateId = selection.SeTeId,
                                SelectionId = selection.SeId
                            });

                            continue;
                        }

                        var odds = Math.Round(fairOdds / finalOddsInitialDivideBy, 2);

                        if (odds < 1.00m)
                        {
                            var tempOdds = GetMasterOddsWithAddedMargin(1m);

                            result.Add(new ClientOdds
                            {
                                ClientCode = client.Item1,
                                DecimalOdds = tempOdds,
                                MalaysianOdds = GetMalayOdds(tempOdds, isBinary),
                                HongkongOdds = GetHongkongOdds(tempOdds, isBinary),
                                IndonesianOdds = GetIndoOdds(tempOdds, isBinary),
                                SelectionTemplateId = selection.SeTeId,
                                SelectionId = selection.SeId
                            });

                            continue;
                        }

                        if (odds >= selection.Od)
                            odds = GetMasterOddsWithAddedMargin(selection.Od);

                        result.Add(new ClientOdds
                        {
                            ClientCode = client.Item1,
                            DecimalOdds = odds,
                            MalaysianOdds = GetMalayOdds(odds, isBinary),
                            HongkongOdds = GetHongkongOdds(odds, isBinary),
                            IndonesianOdds = GetIndoOdds(odds, isBinary),
                            SelectionTemplateId = selection.SeTeId,
                            SelectionId = selection.SeId
                        });
                    }
                    else
                    {
                        result.Add(new ClientOdds
                        {
                            ClientCode = client.Item1,
                            DecimalOdds = selection.Od,
                            MalaysianOdds = GetMalayOdds(selection.Od, isBinary),
                            HongkongOdds = GetHongkongOdds(selection.Od, isBinary),
                            IndonesianOdds = GetIndoOdds(selection.Od, isBinary),
                            SelectionTemplateId = selection.SeTeId,
                            SelectionId = selection.SeId
                        });
                    }
                }
            }

            return result;
        }

        private static decimal GetMasterOddsWithAddedMargin(decimal odds)
        {
            if (Math.Round(odds, 2) <= 1.01m)
                return odds;

            return Math.Round(100 / ((100 / odds) + 2m), 2);
        }

        private static decimal GetMalayOdds(decimal odds, bool isBinary)
        {
            // add binary condition if more than 2 value is decimal
            if (isBinary == false)
                return odds;

            if (odds == 1m)
                return 0;

            if (odds <= 2.00m)
                return odds - 1m;

            var retval = -1m / (odds - 1m);

            return Math.Truncate(retval * 100) / 100;
        }

        private static decimal GetHongkongOdds(decimal odds, bool isBinary)
        {
            // add binary condition if more than 2 value is decimal
            if (isBinary == false)
                return odds;

            if (odds == 1m)
                return 0;

            return odds - 1m;
        }

        private static decimal GetIndoOdds(decimal odds, bool isBinary)
        {
            // add binary condition if more than 2 value is decimal
            if (isBinary == false)
                return odds;

            if (odds == 1m)
                return 0;

            if (odds >= 2.00m)
                return odds - 1m;

            var retval = -1m / (odds - 1m);

            return Math.Truncate(retval * 100) / 100;
        }

        private List<string> GetGroupsForMainMarkets(ClientContext client, OnMarketChangeMessage message)
        {
            var clientCode = client.ClientCode;

            var result = new List<string>
            {
            };

            if (message.FiStId != (int)FixtureStatus.Prematch && message.FiStId != (int)FixtureStatus.Live)
            {
                if (message.FiTyId == (int)FixtureType.Match)
                {
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:livefixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:livefixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:livefixtures");

                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:todayfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:todayfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:todayfixtures");

                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:dailyfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:dailyfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:dailyfixtures");

                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:fixtures");
                }
                else if (message.FiTyId == (int)FixtureType.Outright)
                {
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:outrightfixtures");
                }

                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:fixtures");
            }
            else
            {
                if (message.FiStId == (int)FixtureStatus.Live)
                {
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:livefixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:livefixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:livefixtures");

                    if ((DateTime.UtcNow - message.FiStDa).TotalMinutes < 2)
                    {
                        result.Add($"endpoint:{clientCode}:sports:{message.SpId}:todayfixtures");
                        result.Add($"endpoint:{clientCode}:sports:{message.SpId}:dailyfixtures");
                    }
                }

                if (message.FiStId == (int)FixtureStatus.Prematch &&
                    message.FiStDa.Month == DateTime.UtcNow.Month &&
                    message.FiStDa.Day == DateTime.UtcNow.Day &&
                    message.FiStDa.Year == DateTime.UtcNow.Year &&
                    message.FiTyId == (int)FixtureType.Match)
                {
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:todayfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:todayfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:todayfixtures");

                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:dailyfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:dailyfixtures");
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:dailyfixtures");
                }

                if (message.FiStId == (int)FixtureStatus.Prematch &&
                    message.FiTyId == (int)FixtureType.Outright)
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:outrightfixtures");

                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:fixtures");
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:fixtures");
            }

            var topFixtures = _memCache.GetCache<TopFixture>($"{clientCode}:TFL");

            if (topFixtures != null)
            {
                if (topFixtures.FiIds.Any(c => c == message.FiId))
                    result.Add($"endpoint:{clientCode}:topfixtures");
            }

            var topLeagues = _memCache.GetCache<TopLeague>($"{clientCode}:TLL:{message.SpId}");

            if (topLeagues != null)
            {
                var temp = topLeagues.SegmentLeagueIds.Where(c => c.Item1 == message.SegId && c.Item2 == message.LeId).FirstOrDefault();

                if (temp != null)
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:topleaguefixtures");
            }

            var leagueFavorites = _memCache.GetCache<Favorites>($"{clientCode}:fli:{message.SpId}:{message.LeId}");

            if (leagueFavorites != null)
            {
                Parallel.ForEach(leagueFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            var fixtureFavorites = _memCache.GetCache<Favorites>($"{clientCode}:ffi:{message.SpId}:{message.FiId}");

            if (fixtureFavorites != null)
            {
                Parallel.ForEach(fixtureFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            return result;
        }

        private List<string> GetGroupsForAllMarkets(ClientContext client, OnMarketChangeMessage message)
        {
            var clientCode = client.ClientCode;

            var result = new List<string>
            {
                $"endpoint:{clientCode}:fixturedetails:{message.FiId}"
            };

            var boostedFixtures = _memCache.GetCache<List<long>>($"{clientCode}:bf");

            if (boostedFixtures != null && boostedFixtures.Contains(message.FiId))
                result.Add($"endpoint:{clientCode}:boostedfixtures");

            result.Add($"ontopmarketsoddschange:{clientCode}:{message.FiId}");

            return result;
        }

        private List<string> GetGroups(ClientContext client, OnBetStopFixtureMessage message)
        {
            var clientCode = client.ClientCode;

            var result = new List<string>
            {
                $"endpoint:{clientCode}:fixturedetails:{message.FiId}"
            };

            if (message.FiStId == (int)FixtureStatus.Live)
            {
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:livefixtures");
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:livefixtures");
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:livefixtures");
            }

            //message.FiStDa.Month == DateTime.UtcNow.Month &&
            //message.FiStDa.Day == DateTime.UtcNow.Day &&
            //message.FiStDa.Year == DateTime.UtcNow.Year &&
            if (message.FiStId == (int)FixtureStatus.Prematch &&
                message.FiTyId == (int)FixtureType.Match)
            {
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:todayfixtures");
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:todayfixtures");
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:todayfixtures");

                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:dailyfixtures");
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:dailyfixtures");
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:dailyfixtures");
            }

            if (message.FiStId == (int)FixtureStatus.Prematch &&
                message.FiTyId == (int)FixtureType.Outright)
            {
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:outrightfixtures");
            }

            result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:fixtures");
            result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:fixtures");

            var topLeagues = _memCache.GetCache<TopLeague>($"{clientCode}:TLL:{message.SpId}");

            if (topLeagues != null)
            {
                var temp = topLeagues.SegmentLeagueIds.Where(c => c.Item1 == message.SegId && c.Item2 == message.LeId).FirstOrDefault();

                if (temp != null)
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:topleaguefixtures");
            }

            var topFixtures = _memCache.GetCache<TopFixture>($"{clientCode}:TFL");

            if (topFixtures != null)
            {
                if (topFixtures.FiIds.Any(c => c == message.FiId))
                    result.Add($"endpoint:{clientCode}:topfixtures");
            }

            var leagueFavorites = _memCache.GetCache<Favorites>($"{clientCode}:fli:{message.SpId}:{message.LeId}");

            if (leagueFavorites != null)
            {
                Parallel.ForEach(leagueFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            var fixtureFavorites = _memCache.GetCache<Favorites>($"{clientCode}:ffi:{message.SpId}:{message.FiId}");

            if (fixtureFavorites != null)
            {
                Parallel.ForEach(fixtureFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            var boostedFixtures = _memCache.GetCache<List<long>>($"{clientCode}:bf");

            if (boostedFixtures != null && boostedFixtures.Contains(message.FiId))
                result.Add($"endpoint:{clientCode}:boostedfixtures");

            return result;
        }

        private string PrepareSelectionName(string MaTeId, string selectionTemplateId, string selectionName, string line)
        {
            var MaTeIds = new string[] { "16", "66" };

            if (!MaTeIds.Contains(MaTeId))
                return selectionName;

            var result = selectionName;

            var decLine = Convert.ToDecimal(line);
            var absLine = Math.Abs(decLine);

            if (decLine == 0m)
            {
                result = $"{selectionName} (0)";
            }
            else
            {
                if (selectionTemplateId == "1714")
                {
                    if (decLine > 0)
                        result = $"{selectionName} (+{absLine})";
                    else
                        result = $"{selectionName} (-{absLine})";
                }
                else if (selectionTemplateId == "1715")
                {
                    if (decLine > 0)
                        result = $"{selectionName} (-{absLine})";
                    else
                        result = $"{selectionName} (+{absLine})";
                }
            }

            return result;
        }

        private async Task ProcessMatchOddsChangeForMainMarketsOE(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection, List<MarketMessage> finalMarkets)
        {
            if (message.ClCo == null || message.ClCo.Contains(client.ClientCode.ToUpper()) == false)
                return;

            var betBuilderMarketTemplates = GetBetBuilderMarketTemplates(client);
            var betBuilderSportIds = betBuilderMarketTemplates.BetBuilderMarketTemplates.Select(s => s.SportId).Distinct().ToList();

            var isBetBuilder = false;

            if (betBuilderSportIds.Contains(message.SpId) && message.FiTyId == (int)FixtureType.Match)
            {
                if (message.FiStId == (int)FixtureStatus.Prematch)
                    isBetBuilder = betBuilderMarketTemplates.BetBuilderMarketTemplates.Any(c => c.SportId == message.SpId && c.Prematch == 1);
                else if (message.FiStId == (int)FixtureStatus.Live)
                    isBetBuilder = betBuilderMarketTemplates.BetBuilderMarketTemplates.Any(c => c.SportId == message.SpId && c.Live == 1);
            }

            var markets = new List<OutgoingMarket>();
            var marketTemplateGroup = GetMarketTemplatePlayerProps(client);

            foreach (var market in finalMarkets)
            {
                if (market.ClCo == null || !market.ClCo.Contains(client.ClientCode.ToUpper()))
                    continue;

                var selections = new List<OutgoingSelection>();
                var clientOdds = new List<ClientOdds>();
                var margin = 0m;

                if (market.Se.Count > 0)
                {
                    var activeSelections = market.Se.Where(c => c.IsAc == true && c.Od > 0m).ToList();

                    if (activeSelections.Count == 1)
                        activeSelections = market.Se.Where(c => c.Od > 0m).ToList();

                    margin = (Math.Round(activeSelections.Sum(s => 100 / s.Od)) - 100) / 100;

                    clientOdds = GetClientOdds(message.SpId, activeSelections.Count, margin, market.Se, market.ClCoMa, client.ClientCode.ToUpper(), market.MaTeId, marketTemplateGroup.Where(c => c.SportId == message.SpId).ToList());
                }

                foreach (var selection in market.Se)
                {
                    var odds = clientOdds.FirstOrDefault(c => c.ClientCode == client.ClientCode.ToUpper() && c.SelectionId == selection.SeId);

                    selections.Add(new OutgoingSelection
                    {
                        SeTeId = selection.SeTeId,
                        SeId = selection.SeId.ToString(),
                        SeNa = selection.SeNa,
                        DeOd = odds.DecimalOdds.ToString("0.00"),
                        MaOd = odds.MalaysianOdds.ToString("0.00"),
                        HoOd = odds.HongkongOdds.ToString("0.00"),
                        InOd = odds.IndonesianOdds.ToString("0.00"),
                        IsAc = selection.IsAc,
                        SoOr = selection.SoOr,
                        SeNaTr = ToOutgoingTranslations(selection.SeNaTr),
                    });
                }

                selections = selections.OrderBy(o => o.SoOr).ToList();

                var isMarketBetBuilder = false;

                markets.Add(new OutgoingMarket
                {
                    MaTeId = market.MaTeId,
                    MaId = market.MaId.ToString(),
                    MaNa = market.MaNa,
                    Li = market.Li,
                    MaStId = market.ClCo.Contains(client.ClientCode.ToUpper()) ? market.MaStId : (int)MarketStatus.Inactive,
                    MaStNa = market.ClCo.Contains(client.ClientCode.ToUpper()) ? market.MaStNa : MarketStatus.Inactive.ToString(),
                    SoOr = market.SoOr,
                    //IsIn = market.IsInsert,
                    IsBeBu = isMarketBetBuilder,
                    CaStId = market.CaStId,
                    CaStNa = market.CaStNa,
                    InPl = market.InPl,
                    IsHa = market.IsHa,
                    MaTeTr = ToOutgoingTranslations(market.MaTeTr),
                    Se = selections
                });
            }

            markets = markets.OrderBy(o => o.SoOr).ToList();

            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForMainMarkets(client, message),
                Pa = new OddsChangeFixture
                {
                    SpId = message.SpId,
                    SpNa = message.SpNa,
                    SegId = message.SegId,
                    SegNa = message.SegNa,
                    LeId = message.LeId,
                    LeNa = message.LeNa,
                    FiId = message.FiId.ToString(),
                    PrFiId = message.PrFiId,
                    FiNa = message.FiNa,
                    HoNa = GetHomeName(message.FiNa, (FixtureType)message.FiTyId),
                    AwNa = GetAwayName(message.FiNa, (FixtureType)message.FiTyId),
                    FiStDa = message.FiStDa,
                    IsOfLi = message.IsOfLi,
                    FiStId = message.FiStId,
                    FiStNa = message.FiStNa,
                    FiTyId = message.FiTyId,
                    FiTyNa = message.FiTyNa,
                    Sc = message.Sc,
                    CuPe = message.CuPe.ToString(),
                    CuMi = message.CuMi,
                    CuSe = message.CuSe,
                    SoOr = GetFixtureSortOrder(client, message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(client, message.FiId),
                    IsBeBu = isBetBuilder,
                    Ma = markets,
                    SpTr = ToOutgoingTranslations(message.SpTr),
                    SegTr = ToOutgoingTranslations(message.SegTr),
                    LeTr = ToOutgoingTranslations(message.LeTr),
                    HoTr = ToOutgoingTranslations(message.HoTr),
                    AwTr = ToOutgoingTranslations(message.AwTr)
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
            {
                _publishedToWebSocketCounter.Add(1, new KeyValuePair<string, object?>("method", "PublishOddsChangeForMainMarkets"), new KeyValuePair<string, object?>("client.code", client.ClientCode));
            }

            await PublishToWebSocket(client, "PublishOddsChangeForMainMarkets", hubConnection, payload);
        }

        private async Task ProcessMatchOddsChangeForMainMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection, List<MarketMessage> finalMarkets)
        {
            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForMainMarkets(client, message),
                Pa = new OddsChangeFixture
                {
                    SpId = message.SpId,
                    SpNa = message.SpNa,
                    SegId = message.SegId,
                    SegNa = message.SegNa,
                    LeId = message.LeId,
                    LeNa = message.LeNa,
                    FiId = message.FiId.ToString(),
                    PrFiId = message.PrFiId,
                    FiNa = message.FiNa,
                    HoNa = GetHomeName(message.FiNa, (FixtureType)message.FiTyId),
                    AwNa = GetAwayName(message.FiNa, (FixtureType)message.FiTyId),
                    FiStDa = message.FiStDa,
                    IsOfLi = message.IsOfLi,
                    FiStId = message.FiStId,
                    FiStNa = message.FiStNa,
                    FiTyId = message.FiTyId,
                    FiTyNa = message.FiTyNa,
                    Sc = message.Sc,
                    CuPe = message.CuPe.ToString(),
                    CuMi = message.CuMi,
                    CuSe = message.CuSe,
                    SoOr = GetFixtureSortOrder(client, message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(client, message.FiId),
                    IsBeBu = false,
                    SpTr = ToOutgoingTranslations(message.SpTr),
                    SegTr = ToOutgoingTranslations(message.SegTr),
                    LeTr = ToOutgoingTranslations(message.LeTr),
                    HoTr = ToOutgoingTranslations(message.HoTr),
                    AwTr = ToOutgoingTranslations(message.AwTr),
                    Ma = finalMarkets.Select(m => new OutgoingMarket
                    {
                        MaTeId = m.MaTeId,
                        MaId = m.MaId.ToString(),
                        MaNa = m.MaNa,
                        Li = m.Li,
                        MaStId = m.MaStId,
                        MaStNa = m.MaStNa,
                        SoOr = m.SoOr,
                        IsBeBu = false,
                        CaStId = m.CaStId,
                        CaStNa = m.CaStNa,
                        InPl = m.InPl,
                        IsHa = m.IsHa,
                        MaTeTr = ToOutgoingTranslations(m.MaTeTr),
                        Se = m.Se.Select(s => new OutgoingSelection
                        {
                            SeTeId = s.SeTeId,
                            SeId = s.SeId.ToString(),
                            SeNa = s.SeNa,
                            DeOd = s.Od.ToString("0.00"),
                            MaOd = GetMalayOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            HoOd = GetHongkongOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            InOd = GetIndoOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            IsAc = s.IsAc,
                            SoOr = s.SoOr,
                            SeNaTr = ToOutgoingTranslations(s.SeNaTr),
                        }).OrderBy(o => o.SoOr).ToList()
                    }).OrderBy(o => o.SoOr).ToList()
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
            {
                _publishedToWebSocketCounter.Add(1, new KeyValuePair<string, object?>("method", "PublishOddsChangeForMainMarkets"), new KeyValuePair<string, object?>("client.code", client.ClientCode));
            }

            if (message.FiStId == (int)FixtureStatus.Closed)
                _logger.LogInformationEx($"FixtureId:{message.PrFiId} OnOddsChange Closed");
            if (message.FiStId == (int)FixtureStatus.Ended)
                _logger.LogInformationEx($"FixtureId:{message.PrFiId} OnOddsChange Ended");

            await PublishToWebSocket(client, "PublishOddsChangeForMainMarkets", hubConnection, payload);
        }

        private async Task ProcessMatchOddsChangeForAllMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection)
        {
            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForAllMarkets(client, message),
                Pa = new OddsChangeFixture
                {
                    SpId = message.SpId,
                    SpNa = message.SpNa,
                    SegId = message.SegId,
                    SegNa = message.SegNa,
                    LeId = message.LeId,
                    LeNa = message.LeNa,
                    FiId = message.FiId.ToString(),
                    PrFiId = message.PrFiId,
                    FiNa = message.FiNa,
                    HoNa = GetHomeName(message.FiNa, (FixtureType)message.FiTyId),
                    AwNa = GetAwayName(message.FiNa, (FixtureType)message.FiTyId),
                    FiStDa = message.FiStDa,
                    IsOfLi = message.IsOfLi,
                    FiStId = message.FiStId,
                    FiStNa = message.FiStNa,
                    FiTyId = message.FiTyId,
                    FiTyNa = message.FiTyNa,
                    Sc = message.Sc,
                    CuPe = message.CuPe.ToString(),
                    CuMi = message.CuMi,
                    CuSe = message.CuSe,
                    SoOr = GetFixtureSortOrder(client, message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(client, message.FiId),
                    IsBeBu = false,
                    StUr = message.StUr,
                    SpTr = ToOutgoingTranslations(message.SpTr),
                    SegTr = ToOutgoingTranslations(message.SegTr),
                    LeTr = ToOutgoingTranslations(message.LeTr),
                    HoTr = ToOutgoingTranslations(message.HoTr),
                    AwTr = ToOutgoingTranslations(message.AwTr),
                    Ma = message.Ma.Select(m => new OutgoingMarket
                    {
                        MaTeId = m.MaTeId,
                        MaId = m.MaId.ToString(),
                        MaNa = m.MaNa,
                        Li = m.Li,
                        MaStId = m.MaStId,
                        MaStNa = m.MaStNa,
                        SoOr = m.SoOr,
                        CaStId = m.CaStId,
                        CaStNa = m.CaStNa,
                        InPl = m.InPl,
                        IsHa = m.IsHa,
                        MaTeTr = ToOutgoingTranslations(m.MaTeTr),
                        Se = m.Se.Select(s => new OutgoingSelection
                        {
                            SeTeId = s.SeTeId,
                            SeId = s.SeId.ToString(),
                            SeNa = s.SeNa,
                            DeOd = s.Od.ToString("0.00"),
                            MaOd = GetMalayOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            HoOd = GetHongkongOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            InOd = GetIndoOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            IsAc = s.IsAc,
                            SoOr = s.SoOr,
                            SeNaTr = ToOutgoingTranslations(s.SeNaTr),
                        }).OrderBy(o => o.SoOr).ToList()
                    }).OrderBy(o => o.SoOr).ToList()
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
                _publishedToWebSocketCounter.Add(1,
                        new KeyValuePair<string, object?>("method", "PublishOddsChangeForAllMarkets"),
                        new KeyValuePair<string, object?>("client.code", client.ClientCode));
            await PublishToWebSocket(client, "PublishOddsChangeForAllMarkets", hubConnection, payload);
        }

        private async Task ProcessMatchOddsChangeForAllMarketsOE(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection)
        {
            if (message.ClCo == null || message.ClCo.Contains(client.ClientCode.ToUpper()) == false)
                return;

            var betBuilderMarketTemplates = GetBetBuilderMarketTemplates(client);
            var betBuilderSportIds = betBuilderMarketTemplates.BetBuilderMarketTemplates.Select(s => s.SportId).Distinct().ToList();

            var isBetBuilder = false;

            if (betBuilderSportIds.Contains(message.SpId) && message.FiTyId == (int)FixtureType.Match)
            {
                if (message.FiStId == (int)FixtureStatus.Prematch)
                    isBetBuilder = betBuilderMarketTemplates.BetBuilderMarketTemplates.Any(c => c.SportId == message.SpId && c.Prematch == 1);
                else if (message.FiStId == (int)FixtureStatus.Live)
                    isBetBuilder = betBuilderMarketTemplates.BetBuilderMarketTemplates.Any(c => c.SportId == message.SpId && c.Live == 1);

            }

            var markets = new List<OutgoingMarket>();

            var marketSortOrder = GetMarketSortOrder(client, message.Ma, message.SpId, message.FiStId);
            var marketTemplateGroup = GetMarketTemplatePlayerProps(client);

            foreach (var market in message.Ma)
            {
                if (market.ClCo == null || !market.ClCo.Contains(client.ClientCode.ToUpper()))
                    continue;

                var selections = new List<OutgoingSelection>();
                var clientOdds = new List<ClientOdds>();
                var margin = 0m;

                if (market.Se.Count > 0)
                {
                    var activeSelections = market.Se.Where(c => c.IsAc == true && c.Od > 0m).ToList();

                    if (activeSelections.Count == 1)
                        activeSelections = market.Se.Where(c => c.Od > 0m).ToList();

                    margin = (Math.Round(activeSelections.Sum(s => 100 / s.Od)) - 100) / 100;

                    clientOdds = GetClientOdds(message.SpId, activeSelections.Count, margin, market.Se, market.ClCoMa, client.ClientCode.ToUpper(), market.MaTeId, marketTemplateGroup.Where(c => c.SportId == message.SpId).ToList());
                }

                foreach (var selection in market.Se)
                {
                    var odds = clientOdds.FirstOrDefault(c => c.ClientCode == client.ClientCode.ToUpper() && c.SelectionId == selection.SeId);

                    selections.Add(new OutgoingSelection
                    {
                        SeTeId = selection.SeTeId,
                        SeId = selection.SeId.ToString(),
                        SeNa = selection.SeNa,
                        DeOd = odds.DecimalOdds.ToString("0.00"),
                        MaOd = odds.MalaysianOdds.ToString("0.00"),
                        HoOd = odds.HongkongOdds.ToString("0.00"),
                        InOd = odds.IndonesianOdds.ToString("0.00"),
                        IsAc = selection.IsAc,
                        SoOr = selection.SoOr,
                        SeNaTr = ToOutgoingTranslations(selection.SeNaTr),
                    });
                }

                selections = selections.OrderBy(o => o.SoOr).ToList();

                var isMarketBetBuilder = false;
                var betBuilderMarketTemplate = betBuilderMarketTemplates.BetBuilderMarketTemplates.FirstOrDefault(c => c.SportId == message.SpId && c.MaTeId == market.MaTeId);

                if (betBuilderMarketTemplate != null)
                {
                    var isQuarterMarket = Convert.ToDecimal(market.Li).ToString("0.00").Contains(".25") || Convert.ToDecimal(market.Li).ToString("0.00").Contains(".75") ? true : false;
                    if (!isQuarterMarket && message.FiStId == (int)FixtureStatus.Prematch && betBuilderMarketTemplate.Prematch == 1)
                    {
                        isMarketBetBuilder = true;
                    }
                    else if (!isQuarterMarket && message.FiStId == (int)FixtureStatus.Live && betBuilderMarketTemplate.Live == 1)
                    {
                        isMarketBetBuilder = true;
                    }

                    var isDecimal = decimal.TryParse(market.Li, out decimal tempLine);

                    if (isDecimal == true)
                    {
                        var modulo = tempLine % 1.00m;

                        if (modulo == 0m && tempLine >= 1m)
                            isMarketBetBuilder = false;
                    }
                }

                markets.Add(new OutgoingMarket
                {
                    MaTeId = market.MaTeId,
                    MaId = market.MaId.ToString(),
                    MaNa = market.MaNa,
                    Li = market.Li,
                    MaStId = market.ClCo.Contains(client.ClientCode.ToUpper()) ? market.MaStId : (int)MarketStatus.Inactive,
                    MaStNa = market.ClCo.Contains(client.ClientCode.ToUpper()) ? market.MaStNa : MarketStatus.Inactive.ToString(),
                    SoOr = marketSortOrder.Where(c => c.Item1 == market.MaTeId).Select(s => s.Item2).FirstOrDefault(),
                    //IsIn = market.IsInsert,
                    IsBeBu = isMarketBetBuilder,
                    CaStId = market.CaStId,
                    CaStNa = market.CaStNa,
                    InPl = market.InPl,
                    IsHa = market.IsHa,
                    MaTeTr = ToOutgoingTranslations(market.MaTeTr),
                    Se = selections
                });
            }

            markets = markets.OrderBy(o => o.SoOr).ToList();

            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForAllMarkets(client, message),
                Pa = new OddsChangeFixture
                {
                    SpId = message.SpId,
                    SpNa = message.SpNa,
                    SegId = message.SegId,
                    SegNa = message.SegNa,
                    LeId = message.LeId,
                    LeNa = message.LeNa,
                    FiId = message.FiId.ToString(),
                    PrFiId = message.PrFiId,
                    FiNa = message.FiNa,
                    HoNa = GetHomeName(message.FiNa, (FixtureType)message.FiTyId),
                    AwNa = GetAwayName(message.FiNa, (FixtureType)message.FiTyId),
                    FiStDa = message.FiStDa,
                    IsOfLi = message.IsOfLi,
                    FiStId = message.FiStId,
                    FiStNa = message.FiStNa,
                    FiTyId = message.FiTyId,
                    FiTyNa = message.FiTyNa,
                    Sc = message.Sc,
                    CuPe = message.CuPe.ToString(),
                    CuMi = message.CuMi,
                    CuSe = message.CuSe,
                    SoOr = GetFixtureSortOrder(client, message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(client, message.FiId),
                    IsBeBu = isBetBuilder,
                    Ma = markets,
                    SpTr = ToOutgoingTranslations(message.SpTr),
                    SegTr = ToOutgoingTranslations(message.SegTr),
                    LeTr = ToOutgoingTranslations(message.LeTr),
                    HoTr = ToOutgoingTranslations(message.HoTr),
                    AwTr = ToOutgoingTranslations(message.AwTr)
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
                _publishedToWebSocketCounter.Add(1,
                        new KeyValuePair<string, object?>("method", "PublishOddsChangeForAllMarkets"),
                        new KeyValuePair<string, object?>("client.code", client.ClientCode));
            await PublishToWebSocket(client, "PublishOddsChangeForAllMarkets", hubConnection, payload);
        }

        // Forwards translation deltas only when there is something to forward (count > 0);
        // null keeps the property off the wire so clients retain their current translated names.
        // Internal for OE.Websocket.Service.Tests.
        internal static List<OutgoingTranslation> ToOutgoingTranslations(List<TranslationMessage> translations)
        {
            return translations?.Count > 0
                ? translations.Select(t => new OutgoingTranslation { LaCo = t.LaCo, Tx = t.Tx }).ToList()
                : null;
        }

        #endregion
    }

    #region Models

    public class TopCache
    {
        public int Id { get; set; }
    }

    public class BaseResponseDTO<T>
    {
        public StatusCode StatusCode { get; set; }
        public string Message { get; set; }
        public T Result { get; set; }
    }

    public class TWResponseDTO
    {
        public TWResponseDTO()
        {
            Fixtures = new List<FixtureDTO>();
        }
        public List<FixtureDTO> Fixtures { get; set; }
    }

    public class FixtureDTO
    {
        public FixtureDTO()
        {
            Markets = new List<MarketDTO>();
        }
        public string FiId { get; set; }
        public List<MarketDTO> Markets { get; set; }
    }

    public class MarketDTO
    {
        public string MarketId { get; set; }
    }

    public class TopLeaguesResponseDTO
    {
        public TopLeaguesResponseDTO()
        {
            TopLeagues = new List<TopLeagues>();
        }
        public List<TopLeagues> TopLeagues { get; set; }
    }

    public class TopLeagues
    {
        public int SegmentId { get; set; }
        public string SegmentName { get; set; }
        public int LeagueId { get; set; }
        public string LeagueName { get; set; }
    }

    public class Favorites
    {
        public List<string> UserIds { get; set; }
    }

    public class SportSelectorMarketTemplateDTO
    {
        public SportSelectorMarketTemplateDTO()
        {
            MarketTemplates = new List<SelectorMarketTemplateDTO>();
        }
        public int SportId { get; set; }
        public List<SelectorMarketTemplateDTO> MarketTemplates { get; set; }
    }

    public class SelectorMarketTemplateDTO
    {
        [JsonPropertyName("marketTemplateId")]
        public string MaTeId { get; set; }
    }

    public class MarketCount
    {
        public MarketCount()
        {
            MarketIds = new List<long>();
        }
        public long FiId { get; set; }

        public List<long> MarketIds { get; set; }
    }

    public class TopFixture
    {
        public TopFixture()
        {
            FiIds = new List<long>();
        }
        public List<long> FiIds { get; set; }
    }

    public class TopLeague
    {
        public TopLeague()
        {
            SegmentLeagueIds = new List<Tuple<int, int>>();
        }
        public List<Tuple<int, int>> SegmentLeagueIds { get; set; }
    }

    public class ClientMarketTemplateGroupResponseDTO
    {
        public ClientMarketTemplateGroupResponseDTO()
        {
            MarketTemplateGroups = new List<ClientMarketTemplateGroupDTO>();
        }
        public List<ClientMarketTemplateGroupDTO> MarketTemplateGroups { get; set; }
    }

    public class ClientMarketTemplateGroupDTO
    {
        public int FiStId { get; set; }
        public int SportId { get; set; }
        public string MaTeIds { get; set; }
    }

    public class ClientMarketSortOrder
    {
        public ClientMarketSortOrder()
        {
            MaTeIds = new List<string>();
        }
        public List<string> MaTeIds { get; set; }
    }

    public class ClientFixtureSortOrderResponseDTO
    {
        public ClientFixtureSortOrderResponseDTO()
        {
            Fixtures = new List<ClientFixtureSortOrderDTO>();
        }
        public List<ClientFixtureSortOrderDTO> Fixtures { get; set; }
    }

    public class MarketTemplatePlayerPropsResponseDTO
    {
        public int SportId { get; set; }
        public int FiStId { get; set; }
        public string MarketTemplates { get; set; }
    }

    public class ClientFixtureSortOrderDTO
    {
        public int SportId { get; set; }
        public int SegmentId { get; set; }
        public int LeagueId { get; set; }
        public int SortOrder { get; set; }
    }

    public class FixtureSortOrder
    {
        public int SortOrder { get; set; }
    }

    public class BetBuilderMarketTemplatesDTO
    {
        public BetBuilderMarketTemplatesDTO()
        {
            BetBuilderMarketTemplates = new List<BetBuilderMarketTemplateDTO>();
        }
        public List<BetBuilderMarketTemplateDTO> BetBuilderMarketTemplates { get; set; }
    }

    public class BetBuilderMarketTemplateDTO
    {
        public int SportId { get; set; }
        public string MaTeId { get; set; }
        public short Prematch { get; set; }
        public short Live { get; set; }
    }

    #endregion
}
