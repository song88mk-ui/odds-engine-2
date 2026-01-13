using Microsoft.AspNetCore.SignalR.Client;
using OE.WebSocket.Service.Messages;
using OE.Shared.Enums;
using OE.Shared.Extensions;
using OE.Shared.Utilities;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.WebSocket.Service.Telemetry;
using System.Text.Json.Serialization;

namespace OE.WebSocket.Service.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {
        #region Fields

        private static string _clientCode = string.Empty;
        private readonly bool _isOEEnabled;

        private readonly IConfiguration _configuration;
        private readonly IMemCache _memCache;
        private readonly HttpClient _httpClient;
        private readonly ILogger<SportsbookRepository> _logger;

        private readonly ActivitySource _activitySource;
        private readonly Counter<long> _publishedToWebSocketCounter;

        private List<Tuple<int, List<string>>> _mainMarketTemplates = new List<Tuple<int, List<string>>>();
        private readonly List<int> _fixtureStatus = new List<int> { (int)FixtureStatus.Prematch, (int)FixtureStatus.Live };
        private static List<string> _asianClCo = new List<string>();
        // TODO: doc: what are these numbers?
        private static List<string> _MaTeIds = new List<string> { "16", "18", "66", "68" };

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

            _asianClCo = _configuration["Custom:AsianClientCodes"].ToString().Split(',').ToList();

            if (_httpClient == null)
            {
                _httpClient = new HttpClient
                {
                    BaseAddress = new Uri(_configuration["Custom:PlatformAPI"]),
                    Timeout = TimeSpan.FromSeconds(5)
                };

                _httpClient.DefaultRequestHeaders.Add("x-client-id", _configuration["Custom:ClientId"]);
                _httpClient.DefaultRequestHeaders.Add("x-api-key", _configuration["Custom:APIKey"]);
            }

            if (string.IsNullOrEmpty(_clientCode))
                _clientCode = _configuration["Custom:ClientCode"].ToLower();

            _isOEEnabled = Convert.ToBoolean(_configuration["Custom:IsOEEnabled"]);
        }

        #endregion

        #region Methods

        public async Task ProcessOddsChangeForMainMarkets(OnMarketChangeMessage message, HubConnection hubConnection)
        {
            try
            {
                var mainMarkets = _mainMarketTemplates.Where(c => c.Item1 == message.SpId).FirstOrDefault() ?? new Tuple<int, List<string>>(message.SpId, new List<string>());

                var finalMarkets = new List<MarketMessage>();

                if (message.FiTyId == (int)FixtureType.Match || (message.SpId.IsVirtualSportId() && message.FiTyId == (int)FixtureType.Outright))
                    finalMarkets = message.Ma.Where(c => mainMarkets.Item2.Contains(c.MaTeId)).ToList();
                else
                 finalMarkets = message.Ma;
                
                _logger.LogInformation($"ProcessOddsChangeForMainMarkets: {JsonSerializer.Serialize(finalMarkets)}");
                _logger.LogInformation($"ProcessOddsChangeForMainMarkets: {JsonSerializer.Serialize(message.Ma)}");

                if (message.SpId.IsVirtualSportId() || message.FiTyId == (int)FixtureType.Outright)
                {
                    await ProcessMatchOddsChangeForMainMarkets(message, hubConnection, finalMarkets);
                }
                else
                {
                    if (_isOEEnabled)
                        await ProcessMatchOddsChangeForMainMarketsOE(message, hubConnection, finalMarkets);
                    else
                        await ProcessMatchOddsChangeForMainMarkets(message, hubConnection, finalMarkets);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing odds change for main markets");
            }
        }

        public async Task ProcessOddsChangeForAllMarkets(OnMarketChangeMessage message, HubConnection hubConnection)
        {
            try
            {
                if (message.SpId.IsVirtualSportId() || message.FiTyId == (int)FixtureType.Outright)
                {
                    await ProcessMatchOddsChangeForAllMarkets(message, hubConnection);
                }
                else
                {
                    if (_isOEEnabled)
                        await ProcessMatchOddsChangeForAllMarketsOE(message, hubConnection);
                    else
                        await ProcessMatchOddsChangeForAllMarkets(message, hubConnection);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing oddschange for all markets");
            }
        }

        public async Task ProcessBetStopGlobal(OnBetStopGlobalMessage message, HubConnection hubConnection)
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
                            new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
                await PublishToWebSocket("PublishBetStopGlobal", hubConnection, payload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing bet stop global");
            }
        }

        public async Task ProcessBetStopFixture(OnBetStopFixtureMessage message, HubConnection hubConnection)
        {
            try
            {
                var payload = new OutgoingBetStopFixture
                {
                    Gr = GetGroups(_clientCode, message),
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
                            new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
                await PublishToWebSocket("PublishBetStopFixture", hubConnection, payload);

            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing bet stop fixture");
            }
        }

        public async Task ProcessBetStatus(IncomingBetStatus message, HubConnection hubConnection)
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
                            new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
                await PublishToWebSocket("PublishBetStatus", hubConnection, payload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error processing bet status");
            }
        }

        public async Task ProcessCashOutChange(IncomingCashOutChange message, HubConnection hubConnection)
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
                            new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
                await PublishToWebSocket("PublishCashOutChange", hubConnection, payload);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessCashOutChange");
            }
        }

        public async Task ProcessWalletChange(IncomingWalletChange message, HubConnection hubConnection)
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
                            new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
                await PublishToWebSocket("PublishWalletChange", hubConnection, payload);
            }
            catch (Exception)
            {
            }
        }

        public async Task ProcessTopFixtures(OnMarketChangeMessage message)
        {
            try
            {
                if (message.FiTyId == (int)FixtureType.Outright) return;
                if (message.SpId.IsVirtualSportId()) return;

                var isTopFixtureExisting = _memCache.GetCache<TopCache>("TF");

                if (isTopFixtureExisting == null)
                {
                    _memCache.AddOrUpdateCache("TF", new TopCache { Id = 0 }, DateTimeOffset.UtcNow.AddMinutes(5));

                    var result = await GetTopFixtures();

                    if (result.StatusCode == StatusCode.Success)
                    {
                        if (result.Result.Fixtures.Count > 0)
                            _memCache.AddOrUpdateCache("TFL", new TopFixture { FiIds = result.Result.Fixtures.Select(s => Convert.ToInt64(s.FiId)).ToList() }, DateTimeOffset.UtcNow.AddHours(1));
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public async Task ProcessTopLeagues(OnMarketChangeMessage message)
        {
            try
            {
                if (message.FiTyId == (int)FixtureType.Outright) return;
                if (message.SpId.IsVirtualSportId()) return;

                var isTopFixtureExisting = _memCache.GetCache<TopCache>($"TL:{message.SpId}");

                if (isTopFixtureExisting == null)
                {
                    _memCache.AddOrUpdateCache($"TL:{message.SpId}", new TopCache { Id = 0 }, DateTimeOffset.UtcNow.AddMinutes(5));

                    var result = await GetTopLeagues(message.SpId);

                    if (result.StatusCode == StatusCode.Success)
                    {
                        if (result.Result.TopLeagues.Count > 0)
                            _memCache.AddOrUpdateCache($"TLL:{message.SpId}", new TopLeague { SegmentLeagueIds = result.Result.TopLeagues.Select(s => new Tuple<int, int>(s.SegmentId, s.LeagueId)).ToList() }, DateTimeOffset.UtcNow.AddHours(1));
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public async Task ProcessMarketCount(OnMarketChangeMessage message)
        {
            try
            {
                var key = $"mc:{message.FiId}";

                var cache = _memCache.GetCache<MarketCount>(key);

                if (cache == null)
                {
                    if (_fixtureStatus.Contains(message.FiStId) == false)
                        return;

                    var result = await GetFixtureDetails(message.FiId);

                    if (result.StatusCode == StatusCode.Success)
                    {
                        var fixtureDetails = result.Result.Fixtures.FirstOrDefault();

                        if (fixtureDetails != null)
                        {
                            var marketToRemove = message.Ma.Where(c => _marketStatusToRemove.Contains(c.MaStId)).Select(s => s.MaId).ToList();
                            var marketToAdd = message.SpId.IsVirtualSportId() || message.FiTyId == (int)FixtureType.Outright ? message.Ma.Where(c => _marketStatusToAdd.Contains(c.MaStId)).Select(s => s.MaId).ToList() : message.Ma.Where(c => _marketStatusToAdd.Contains(c.MaStId) && c.ClCo.Contains(_clientCode.ToUpper())).Select(s => s.MaId).ToList();                     
                            var marketToAddFromAPI = fixtureDetails.Markets.Select(s => Convert.ToInt64(s.MarketId)).ToList();

                            var currentMarketIds = marketToAdd;

                            foreach (var marketId in marketToAddFromAPI)
                            {
                                if (currentMarketIds.Contains(marketId) == false)
                                    currentMarketIds.Add(marketId);
                            }

                            if (marketToRemove.Count > 0)
                                currentMarketIds = currentMarketIds.Except(marketToRemove).ToList();

                            _memCache.AddOrUpdateCache(key, new MarketCount { FiId = message.FiId, MarketIds = currentMarketIds }, DateTimeOffset.UtcNow.AddDays(7));
                        }
                    }
                }
                else
                {
                    if (_fixtureStatus.Contains(message.FiStId) == false)
                    {
                        _memCache.DeleteCache(key);
                        return;
                    }

                    var marketToRemove = message.Ma.Where(c => _marketStatusToRemove.Contains(c.MaStId)).Select(s => s.MaId).ToList();
                    var marketToAdd = message.SpId.IsVirtualSportId() || message.FiTyId == (int)FixtureType.Outright ? message.Ma.Where(c => _marketStatusToAdd.Contains(c.MaStId)).Select(s => s.MaId).ToList() : message.Ma.Where(c => _marketStatusToAdd.Contains(c.MaStId) && c.ClCo.Contains(_clientCode.ToUpper())).Select(s => s.MaId).ToList();

                    var currentMarketIds = cache.MarketIds;

                    foreach (var marketId in marketToAdd)
                    {
                        if (currentMarketIds.Contains(marketId) == false)
                            currentMarketIds.Add(marketId);
                    }

                    if (marketToRemove.Count > 0)
                        currentMarketIds = currentMarketIds.Except(marketToRemove).ToList();

                    _memCache.AddOrUpdateCache(key, new MarketCount { FiId = message.FiId, MarketIds = currentMarketIds }, DateTimeOffset.UtcNow.AddDays(7));
                }
            }
            catch (Exception)
            {
            }
        }

        public async Task ProcessFavorites(IncomingVisitFavorites message)
        {
            try
            {
                foreach (var favorite in message.Favorites)
                {
                    string key;

                    if (favorite.HierarchyId == 2)
                        key = $"fli:{favorite.SportId}:{favorite.Id}";
                    else
                        key = $"ffi:{favorite.SportId}:{favorite.Id}";

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
            catch (Exception)
            {
            }
        }

        public async Task ProcessRemoveFavorites(IncomingVisitFavorites message)
        {
            try
            {
                foreach (var favorite in message.Favorites)
                {
                    string key;

                    if (favorite.HierarchyId == 2)
                        key = $"fli:{favorite.SportId}:{favorite.Id}";
                    else
                        key = $"ffi:{favorite.SportId}:{favorite.Id}";

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
            catch (Exception)
            {
            }
        }

        public async Task ProcessBoostedFixtures(IncomingVisitBoostedFixtures message)
        {
            try
            {
                _memCache.AddOrUpdateCache($"bf", message.FixtureIds, DateTimeOffset.UtcNow.AddDays(1));

                await Task.CompletedTask;
            }
            catch (Exception)
            {
            }
        }

        public async Task ProcessMainMarketTemplates()
        {
            try
            {
                var result = await GetSelectorMarketTemplates();

                foreach (var item in result.Result.GroupBy(g => g.SportId).ToList())
                {
                    var marketTemplates = item.SelectMany(s => s.MarketTemplates).Select(s => s.MaTeId).Distinct().ToList();
                    _mainMarketTemplates.Add(new Tuple<int, List<string>>(item.Key, marketTemplates));
                }
                _logger.LogInformation($"ProcessMainMarketTemplates: {JsonSerializer.Serialize(_mainMarketTemplates)}");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessMainMarketTemplates");
            }
        }

        public async Task ProcessMarketSortOrder()
        {
            try
            {
                var result = await GetMarketSortOrder();

                foreach (var item in result.Result.MarketTemplateGroups)
                {
                    var key = string.Empty;

                    if (item.FiStId == 0)
                        key = $"pmso:{item.SportId}";
                    else
                        key = $"lmso:{item.SportId}";

                    _memCache.AddOrUpdateCache(key, new ClientMarketSortOrder { MaTeIds = item.MaTeIds.Split(',').ToList() }, DateTimeOffset.UtcNow.AddYears(1));
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessMarketSortOrder");
            }
        }

        public async Task ProcessFixtureSortOrder()
        {
            try
            {
                var result = await GetFixtureSortOrder();

                foreach (var item in result.Result.Fixtures)
                    _memCache.AddOrUpdateCache($"fso:{item.SportId}:{item.SegmentId}:{item.LeagueId}", new FixtureSortOrder { SortOrder = item.SortOrder }, DateTimeOffset.UtcNow.AddYears(1));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessFixtureSortOrder");
            }
        }

        public async Task ProcessBetBuilderMarketTemplates()
        {
            try
            {
                var result = await GetBetBuilderMarketTemplatesDTO();

                var key = $"bbmt";

                _memCache.AddOrUpdateCache(key, result.Result, DateTimeOffset.UtcNow.AddYears(1));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessBetBuilderMarketTemplates");
            }
        }

        public async Task ProcessMarketTemplatePlayerProps()
        {
            try
            {
                var result = await GetMarketTemplatePlayerPropsDTO();

                var key = $"mtpp";

                _memCache.AddOrUpdateCache(key, result.Result, DateTimeOffset.UtcNow.AddYears(1));
            }
            catch (Exception e)
            {
                _logger.LogError(e, "error ProcessMarketTemplatePlayerProps");
            }
        }

        public async Task ProcessPayableChange(IncomingPayableChange message, HubConnection hubConnection)
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
                            new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
                await PublishToWebSocket("PublishPayableChange", hubConnection, payload);
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Utilities

        private static async Task PublishToWebSocket<T>(string method, HubConnection hubConnection, T payload)
        {
            try
            {
                await hubConnection.SendAsync(method, _clientCode, payload);
            }
            catch (Exception e)
            {
            }
        }

        private async Task<BaseResponseDTO<TWResponseDTO>> GetTopFixtures()
        {
            try
            {
                var response = await _httpClient.GetAsync($"v2/sportsbook/topfixtures");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private async Task<BaseResponseDTO<TopLeaguesResponseDTO>> GetTopLeagues(int sportId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"v1/sportsbook/sports/topleagues?sportId={sportId}");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<TopLeaguesResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<TopLeaguesResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private async Task<BaseResponseDTO<TWResponseDTO>> GetFixtureDetails(long FiId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"v1/sportsbook/fixturedetails?FiId={FiId}");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<TWResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private async Task<BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>> GetSelectorMarketTemplates()
        {
            try
            {
                var response = await _httpClient.GetAsync($"v1/sportsbook/selectormarkettemplates");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<List<SportSelectorMarketTemplateDTO>>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private BetBuilderMarketTemplatesDTO GetBetBuilderMarketTemplates()
        {
            return _memCache.GetCache<BetBuilderMarketTemplatesDTO>($"bbmt") ?? new BetBuilderMarketTemplatesDTO();
        }

        private List<MarketTemplatePlayerPropsResponseDTO> GetMarketTemplatePlayerProps()
        {
            return _memCache.GetCache<List<MarketTemplatePlayerPropsResponseDTO>>($"mtpp") ?? new List<MarketTemplatePlayerPropsResponseDTO>();
        }

        private async Task<BaseResponseDTO<BetBuilderMarketTemplatesDTO>> GetBetBuilderMarketTemplatesDTO()
        {
            try
            {
                var response = await _httpClient.PostAsync($"v1/sportsbook/betbuilder/markettemplates", new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<BetBuilderMarketTemplatesDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<BetBuilderMarketTemplatesDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private async Task<BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>> GetMarketSortOrder()
        {
            try
            {
                var response = await _httpClient.GetAsync($"v1/sportsbook/marketsortorder");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<ClientMarketTemplateGroupResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private async Task<BaseResponseDTO<ClientFixtureSortOrderResponseDTO>> GetFixtureSortOrder()
        {
            try
            {
                var response = await _httpClient.GetAsync($"v1/sportsbook/fixturesortorder");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<ClientFixtureSortOrderResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<ClientFixtureSortOrderResponseDTO>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private async Task<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>> GetMarketTemplatePlayerPropsDTO()
        {
            try
            {
                var response = await _httpClient.GetAsync($"v1/sportsbook/client/markettemplateplayerprops");

                if (response.IsSuccessStatusCode)
                    return JsonSerializer.Deserialize<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                else
                {
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                        return JsonSerializer.Deserialize<BaseResponseDTO<List<MarketTemplatePlayerPropsResponseDTO>>>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
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

        private int GetMarketCount(long FiId)
        {
            var key = $"mc:{FiId}";

            var cache = _memCache.GetCache<MarketCount>(key);

            if (cache == null)
                return 0;
            else
                return cache.MarketIds.Count();
        }

        private List<Tuple<string, int>> GetMarketSortOrder(List<MarketMessage> markets, int sportId, int FiStId)
        {
            var result = new List<Tuple<string, int>>();

            if (markets.Count == 0)
                return result;

            ClientMarketSortOrder cache = null;

            if (FiStId == 0)
                cache = _memCache.GetCache<ClientMarketSortOrder>($"pmso:{sportId}");

            if (FiStId == 1)
                cache = _memCache.GetCache<ClientMarketSortOrder>($"lmso:{sportId}");

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

        private int GetFixtureSortOrder(int sportId, int segmentId, int leagueId)
        {
            var result = _memCache.GetCache<FixtureSortOrder>($"fso:{sportId}:{segmentId}:{leagueId}");

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
                    if (sportId == 1 && _MaTeIds.Contains(MaTeId) && _asianClCo.Contains(_clientCode.ToUpper()))
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

                    if (activeSelectionCount == 1)
                        activeSelectionCount = selections.Count;

                    if (selection.IsAc == true && selection.Od > 0m)
                    {
                        var targetMargin = client.Item2;

                        if (playerPropMaTeIds.Contains(MaTeId))
                        {
                            if (selection.SeTeId != "12" || selection.SeTeId != "13")
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

        private List<string> GetGroupsForMainMarkets(string clientCode, OnMarketChangeMessage message)
        {
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

            var topFixtures = _memCache.GetCache<TopFixture>("TFL");

            if (topFixtures != null)
            {
                if (topFixtures.FiIds.Any(c => c == message.FiId))
                    result.Add($"endpoint:{clientCode}:topfixtures");
            }

            var topLeagues = _memCache.GetCache<TopLeague>($"TLL:{message.SpId}");

            if (topLeagues != null)
            {
                var temp = topLeagues.SegmentLeagueIds.Where(c => c.Item1 == message.SegId && c.Item2 == message.LeId).FirstOrDefault();

                if (temp != null)
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:topleaguefixtures");
            }

            var leagueFavorites = _memCache.GetCache<Favorites>($"fli:{message.SpId}:{message.LeId}");

            if (leagueFavorites != null)
            {
                Parallel.ForEach(leagueFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            var fixtureFavorites = _memCache.GetCache<Favorites>($"ffi:{message.SpId}:{message.FiId}");

            if (fixtureFavorites != null)
            {
                Parallel.ForEach(fixtureFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            return result;
        }

        private List<string> GetGroupsForAllMarkets(string clientCode, OnMarketChangeMessage message)
        {
            var result = new List<string>
            {
                $"endpoint:{clientCode}:fixturedetails:{message.FiId}"
            };

            var boostedFixtures = _memCache.GetCache<List<long>>("bf");

            if (boostedFixtures != null && boostedFixtures.Contains(message.FiId))
                result.Add($"endpoint:{clientCode}:boostedfixtures");

            result.Add($"ontopmarketsoddschange:{clientCode}:{message.FiId}");

            return result;
        }

        private List<string> GetGroups(string clientCode, OnBetStopFixtureMessage message)
        {
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
            {
                result.Add($"endpoint:{clientCode}:sports:{message.SpId}:outrightfixtures");
            }

            result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:fixtures");
            result.Add($"endpoint:{clientCode}:sports:{message.SpId}:segments:{message.SegId}:leagues:{message.LeId}:fixtures");

            var topLeagues = _memCache.GetCache<TopLeague>($"TLL:{message.SpId}");

            if (topLeagues != null)
            {
                var temp = topLeagues.SegmentLeagueIds.Where(c => c.Item1 == message.SegId && c.Item2 == message.LeId).FirstOrDefault();

                if (temp != null)
                    result.Add($"endpoint:{clientCode}:sports:{message.SpId}:topleaguefixtures");
            }

            var topFixtures = _memCache.GetCache<TopFixture>("TFL");

            if (topFixtures != null)
            {
                if (topFixtures.FiIds.Any(c => c == message.FiId))
                    result.Add($"endpoint:{clientCode}:topfixtures");
            }

            var leagueFavorites = _memCache.GetCache<Favorites>($"fli:{message.SpId}:{message.LeId}");

            if (leagueFavorites != null)
            {
                Parallel.ForEach(leagueFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            var fixtureFavorites = _memCache.GetCache<Favorites>($"ffi:{message.SpId}:{message.FiId}");

            if (fixtureFavorites != null)
            {
                Parallel.ForEach(fixtureFavorites.UserIds, userId =>
                {
                    result.Add($"onfavorites:{clientCode}:{userId.ToLower()}");
                });
            }

            var boostedFixtures = _memCache.GetCache<List<long>>("bf");

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

        private async Task ProcessMatchOddsChangeForMainMarketsOE(OnMarketChangeMessage message, HubConnection hubConnection, List<MarketMessage> finalMarkets)
        {
            if (message.ClCo == null || message.ClCo.Contains(_clientCode.ToUpper()) == false)
                return;

            var betBuilderMarketTemplates = GetBetBuilderMarketTemplates();
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
            var marketTemplateGroup = GetMarketTemplatePlayerProps();

            foreach (var market in finalMarkets)
            {
                if (market.ClCo == null || !market.ClCo.Contains(_clientCode.ToUpper()))
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

                    clientOdds = GetClientOdds(message.SpId, activeSelections.Count, margin, market.Se, market.ClCoMa, _clientCode.ToUpper(), market.MaTeId, marketTemplateGroup.Where(c => c.SportId == message.SpId).ToList());
                }

                foreach (var selection in market.Se)
                {
                    var odds = clientOdds.FirstOrDefault(c => c.ClientCode == _clientCode.ToUpper() && c.SelectionId == selection.SeId);

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
                        //TODO:
                        //SeNaTr = selection.SelectionTranslations.Select(ss => new OutgoingSelectionTranslation
                        //{
                        //    LaCo = ss.LanguageCode,
                        //    SeNa = message.SpId != 1 ? ss.SelectionName : PrepareSelectionName(market.MaTeId, selection.SeTeId, ss.SelectionName, market.Line)
                        //}).ToList()
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
                    MaStId = market.ClCo.Contains(_clientCode.ToUpper()) ? market.MaStId : (int)MarketStatus.Inactive,
                    MaStNa = market.ClCo.Contains(_clientCode.ToUpper()) ? market.MaStNa : MarketStatus.Inactive.ToString(),
                    SoOr = market.SoOr,
                    //IsIn = market.IsInsert,
                    IsBeBu = isMarketBetBuilder,
                    //MaNaTr = market.MarketTranslations.Select(mt => new OutgoingMarketTranslation
                    //{
                    //    LaCo = mt.LanguageCode,
                    //    MaNa = mt.MarketName
                    //}).ToList(),
                    Se = selections
                });
            }

            markets = markets.OrderBy(o => o.SoOr).ToList();

            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForMainMarkets(_clientCode, message),
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
                    CuPe = message.CuPe,
                    CuMi = message.CuMi,
                    SoOr = GetFixtureSortOrder(message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(message.FiId),
                    IsBeBu = isBetBuilder,
                    //TODO:
                    //SpNaTr = message.SportTranslations.Select(f => new OutgoingSportTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SpNa = f.SportName
                    //}).ToList(),
                    //SegNaTr = message.SegmentTranslations.Select(f => new OutgoingSegmentTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SegNa = f.SegmentName
                    //}).ToList(),
                    //LeNaTr = message.LeagueTranslations.Select(f => new OutgoingLeagueTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    LeNa = f.LeagueName
                    //}).ToList(),
                    //FiNaTr = message.FixtureTranslations.Select(f => new OutgoingFixtureTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    FiNa = f.FixtureName,
                    //    HoNa = GetHomeName(f.FixtureName, (FixtureType)message.FiTyId),
                    //    AwNa = GetAwayName(f.FixtureName, (FixtureType)message.FiTyId),
                    //}).ToList(),
                    Ma = markets
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
            {
                _publishedToWebSocketCounter.Add(1, new KeyValuePair<string, object?>("method", "PublishOddsChangeForMainMarkets"), new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
            }

            await PublishToWebSocket("PublishOddsChangeForMainMarkets", hubConnection, payload);
        }

        private async Task ProcessMatchOddsChangeForMainMarkets(OnMarketChangeMessage message, HubConnection hubConnection, List<MarketMessage> finalMarkets)
        {
            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForMainMarkets(_clientCode, message),
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
                    CuPe = message.CuPe,
                    CuMi = message.CuMi,
                    SoOr = GetFixtureSortOrder(message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(message.FiId),
                    IsBeBu = false,
                    //TODO:
                    //SpNaTr = message.SportTranslations.Select(f => new OutgoingSportTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SpNa = f.SportName
                    //}).ToList(),
                    //SegNaTr = message.SegmentTranslations.Select(f => new OutgoingSegmentTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SegNa = f.SegmentName
                    //}).ToList(),
                    //LeNaTr = message.LeagueTranslations.Select(f => new OutgoingLeagueTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    LeNa = f.LeagueName
                    //}).ToList(),
                    //FiNaTr = message.FixtureTranslations.Select(f => new OutgoingFixtureTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    FiNa = f.FixtureName,
                    //    HoNa = GetHomeName(f.FixtureName, (FixtureType)message.FiTyId),
                    //    AwNa = GetAwayName(f.FixtureName, (FixtureType)message.FiTyId),
                    //}).ToList(),
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
                        //TODO
                        //MaNaTr = m.MarketTranslations.Select(mt => new OutgoingMarketTranslation
                        //{
                        //    LaCo = mt.LanguageCode,
                        //    MaNa = mt.MarketName
                        //}).ToList(),
                        Se = m.Se.Select(s => new OutgoingSelection
                        {
                            SeTeId = s.SeTeId,
                            SeId = s.SeTeId.ToString(),
                            SeNa = s.SeNa,
                            DeOd = s.Od.ToString("0.00"),
                            MaOd = GetMalayOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            HoOd = GetHongkongOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            InOd = GetIndoOdds(s.Od, m.Se.Count > 2 ? false : true).ToString("0.00"),
                            IsAc = s.IsAc,
                            SoOr = s.SoOr,
                            //SeNaTr = s.SelectionTranslations.Select(ss => new OutgoingSelectionTranslation
                            //{
                            //    LaCo = ss.LanguageCode,
                            //    SeNa = ss.SelectionName
                            //}).ToList()
                        }).OrderBy(o => o.SoOr).ToList()
                    }).OrderBy(o => o.SoOr).ToList()
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
            {
                _publishedToWebSocketCounter.Add(1, new KeyValuePair<string, object?>("method", "PublishOddsChangeForMainMarkets"), new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
            }

            await PublishToWebSocket("PublishOddsChangeForMainMarkets", hubConnection, payload);
        }

        private async Task ProcessMatchOddsChangeForAllMarkets(OnMarketChangeMessage message, HubConnection hubConnection)
        {
            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForAllMarkets(_clientCode, message),
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
                    CuPe = message.CuPe,
                    CuMi = message.CuMi,
                    SoOr = GetFixtureSortOrder(message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(message.FiId),
                    IsBeBu = false,
                    //TODO:
                    //SpNaTr = message.SportTranslations.Select(f => new OutgoingSportTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SpNa = f.SportName
                    //}).ToList(),
                    //SegNaTr = message.SegmentTranslations.Select(f => new OutgoingSegmentTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SegNa = f.SegmentName
                    //}).ToList(),
                    //LeNaTr = message.LeagueTranslations.Select(f => new OutgoingLeagueTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    LeNa = f.LeagueName
                    //}).ToList(),
                    //FiNaTr = message.FixtureTranslations.Select(f => new OutgoingFixtureTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    FiNa = f.FixtureName,
                    //    HoNa = GetHomeName(f.FixtureName, (FixtureType)message.FiTyId),
                    //    AwNa = GetAwayName(f.FixtureName, (FixtureType)message.FiTyId),
                    //}).ToList(),
                    Ma = message.Ma.Select(m => new OutgoingMarket
                    {
                        MaTeId = m.MaTeId,
                        MaId = m.MaId.ToString(),
                        MaNa = m.MaNa,
                        Li = m.Li,
                        MaStId = m.MaStId,
                        MaStNa = m.MaStNa,
                        SoOr = m.SoOr,
                        //MaNaTr = m.MarketTranslations.Select(mt => new OutgoingMarketTranslation
                        //{
                        //    LaCo = mt.LanguageCode,
                        //    MaNa = mt.MarketName
                        //}).ToList(),
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
                            //SeNaTr = s.SelectionTranslations.Select(ss => new OutgoingSelectionTranslation
                            //{
                            //    LaCo = ss.LanguageCode,
                            //    SeNa = ss.SelectionName
                            //}).ToList()
                        }).OrderBy(o => o.SoOr).ToList()
                    }).OrderBy(o => o.SoOr).ToList()
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
                _publishedToWebSocketCounter.Add(1,
                        new KeyValuePair<string, object?>("method", "PublishOddsChangeForAllMarkets"),
                        new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
            await PublishToWebSocket("PublishOddsChangeForAllMarkets", hubConnection, payload);
        }

        private async Task ProcessMatchOddsChangeForAllMarketsOE(OnMarketChangeMessage message, HubConnection hubConnection)
        {
            if (message.ClCo == null || message.ClCo.Contains(_clientCode.ToUpper()) == false)
                return;

            var betBuilderMarketTemplates = GetBetBuilderMarketTemplates();
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

            var marketSortOrder = GetMarketSortOrder(message.Ma, message.SpId, message.FiStId);
            var marketTemplateGroup = GetMarketTemplatePlayerProps();

            foreach (var market in message.Ma)
            {
                if (market.ClCo == null || !market.ClCo.Contains(_clientCode.ToUpper()))
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

                    clientOdds = GetClientOdds(message.SpId, activeSelections.Count, margin, market.Se, market.ClCoMa, _clientCode.ToUpper(), market.MaTeId, marketTemplateGroup.Where(c => c.SportId == message.SpId).ToList());
                }

                foreach (var selection in market.Se)
                {
                    var odds = clientOdds.FirstOrDefault(c => c.ClientCode == _clientCode.ToUpper() && c.SelectionId == selection.SeId);

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
                        //SeNaTr = selection.SelectionTranslations.Select(ss => new OutgoingSelectionTranslation
                        //{
                        //    LaCo = ss.LanguageCode,
                        //    SeNa = message.SpId != 1 ? ss.SelectionName : PrepareSelectionName(market.MaTeId, selection.SeTeId, ss.SelectionName, market.Li)
                        //}).ToList()
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
                    MaStId = market.ClCo.Contains(_clientCode.ToUpper()) ? market.MaStId : (int)MarketStatus.Inactive,
                    MaStNa = market.ClCo.Contains(_clientCode.ToUpper()) ? market.MaStNa : MarketStatus.Inactive.ToString(),
                    SoOr = marketSortOrder.Where(c => c.Item1 == market.MaTeId).Select(s => s.Item2).FirstOrDefault(),
                    //IsIn = market.IsInsert,
                    IsBeBu = isMarketBetBuilder,
                    //TODO
                    //MaNaTr = market.MarketTranslations.Select(mt => new OutgoingMarketTranslation
                    //{
                    //    LaCo = mt.LanguageCode,
                    //    MaNa = mt.MarketName
                    //}).ToList(),
                    Se = selections
                });
            }

            markets = markets.OrderBy(o => o.SoOr).ToList();

            var payload = new OutgoingOddsChange
            {
                Gr = GetGroupsForAllMarkets(_clientCode, message),
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
                    CuPe = message.CuPe,
                    CuMi = message.CuMi,
                    SoOr = GetFixtureSortOrder(message.SpId, message.SegId, message.LeId),
                    MaCo = GetMarketCount(message.FiId),
                    IsBeBu = isBetBuilder,
                    //SpNaTr = message.SportTranslations.Select(f => new OutgoingSportTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SpNa = f.SportName
                    //}).ToList(),
                    //SegNaTr = message.SegmentTranslations.Select(f => new OutgoingSegmentTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    SegNa = f.SegmentName
                    //}).ToList(),
                    //LeNaTr = message.LeagueTranslations.Select(f => new OutgoingLeagueTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    LeNa = f.LeagueName
                    //}).ToList(),
                    //FiNaTr = message.FixtureTranslations.Select(f => new OutgoingFixtureTranslation
                    //{
                    //    LaCo = f.LanguageCode,
                    //    FiNa = f.FixtureName,
                    //    HoNa = GetHomeName(f.FixtureName, (FixtureType)message.FiTyId),
                    //    AwNa = GetAwayName(f.FixtureName, (FixtureType)message.FiTyId),
                    //}).ToList(),
                    Ma = markets
                }
            };

            if (_publishedToWebSocketCounter.Enabled)
                _publishedToWebSocketCounter.Add(1,
                        new KeyValuePair<string, object?>("method", "PublishOddsChangeForAllMarkets"),
                        new KeyValuePair<string, object?>("client.code", _configuration["Custom:ClientCode"] ?? "None"));
            await PublishToWebSocket("PublishOddsChangeForAllMarkets", hubConnection, payload);
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
