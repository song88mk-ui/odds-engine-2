using OE.Shared.Enums;
using OE.Shared.Extensions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Utilities;
using OE.UOF.Client.Models;
using System.Diagnostics;
using System.Text.Json;

namespace OE.UOF.Client.Repository
{
    public class ClientRepository : IClientRepository
    {
        #region Fields

        private readonly IMemCache _memCache;
        private readonly IConfiguration _configuration;
        private readonly IPlatformRepository _platformRepository;
        private readonly HttpClient _httpClient;
        private readonly string _clientCode = string.Empty;
        private readonly bool _asianClient = false;
        private readonly List<string> _languageCodes = new List<string>();
        private readonly List<string> _MaTeIds = new List<string> { "16", "18", "66", "68" };

        private readonly JsonSerializerOptions _options;

        #endregion

        #region Ctor

        public ClientRepository(IConfiguration configuration, IMemCache memCache, IPlatformRepository platformRepository)
        {
            _configuration = configuration;
            _memCache = memCache;
            _platformRepository = platformRepository;

            _clientCode = _configuration["Custom:ClientCode"];
            _asianClient = Convert.ToBoolean(_configuration["Custom:AsianClient"]);
            _languageCodes = _configuration["Custom:LanguageCodes"].Split(',').Select(s => s).ToList();

            if (_httpClient == null)
            {
                _httpClient = new HttpClient
                {
                    BaseAddress = new Uri(_configuration["API:PlatformAPI"]),
                    Timeout = TimeSpan.FromSeconds(5)
                };

                _httpClient.DefaultRequestHeaders.Add("x-client-id", _configuration["Custom:ClientId"]);
                _httpClient.DefaultRequestHeaders.Add("x-api-key", _configuration["Custom:APIKey"]);
            }

            _options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        #endregion

        #region Methods
        
        public void ProcessFixtureChange(OnFixtureChangeMessage message, IMessageBroker messageBroker, string correlationId)
        {
            //CheckDelay(message, "START");

            var payloadStr = string.Empty;

            var payload = new
            {
                SportId = message.SpId,
                FixtureSpecifier = message.FiSp,
                ChangeTypeId = message.ChTyId,
                ChangeTypeName = ((ChannelType)message.ChTyId).ToString(),
                FixtureStatusId = message.FiStId,
                FixtureStatusName = ((FixtureStatus)message.FiStId).ToString(),
                TimeStampSent = message.TiSe,
                TimeStampReceived = message.TiRe,
                Raw = string.Empty
            };

            PublishMessage(messageBroker, payload, "onoddschange", $"uof.onoddschange.{GetSportName(message.SpId)}", $"{DateTime.UtcNow.ToEpochTimestamp()}");
        }
        public void ProcessMarketChange(OnMarketChangeMessage message, IMessageBroker messageBroker, string correlationId, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations)
        {
            //CheckDelay(message, "START");

            var payloadStr = string.Empty;

            var finalPayload = PrepareOddsChangeMessage(message);

            finalPayload.Markets = message.Ma.Select(s => new OFMarketMessage
            {
                MarketTemplateId = s.MaTeId,
                MarketName = s.MaNa,
                Line = s.Li,
                MarketStatusId = s.MaStId,
                MarketStatusName = ((MarketStatus)s.MaStId).ToString(),
                MarketSpecifier = s.MaSp,
                //TODO: check the comment below
                //NextBetStop = s.NextBetStop,
                //StartTime = s.StartTime,
                //EndTime = s.EndTime,
                //MarketNameTranslations = s.MarketTranslations.Where(c => _languageCodes.Contains(c.LanguageCode))
                //    .Select(market => new OFMarketTranslation
                //    {
                //        LanguageCode = market.LanguageCode,
                //        MarketName = market.MarketName
                //    }).ToList(),
                Selections = s.Se.Select(se => new OFSelectionMessage
                {
                    SelectionTemplateId = se.SeTeId,
                    SelectionName = se.SeNa,
                    Odds = se.Od,
                    IsActive = se.IsAc,
                    Probability = se.Pr,
                    VoidProbability = se.VoPr,
                    WonProbability = se.WoPr,
                    LostProbability = se.LoPr,
                    HalfWonProbability = se.HaWoPr,
                    HalfLostProbability = se.HaLoPr,
                    SelectionSpecifier = se.SeSp,
                    //TODO: check the comment below
                    //SelectionNameTranslations = se.SelectionTranslations.Where(c => _languageCodes.Contains(c.LanguageCode))
                    //    .Select(s => new OFSelectionTranslation
                    //    {
                    //        LanguageCode = s.LanguageCode,
                    //        SelectionName = s.SelectionName
                    //    }).ToList(),
                }).ToList()
            }).ToList();

            if (message.Ma.Count > 0)
            {
                PublishBetStopMessage(messageBroker, message, correlationId);
            }

            payloadStr = JsonSerializer.Serialize(finalPayload, _options);

            PublishMessage(messageBroker, payloadStr, "onoddschange", $"uof.onoddschange.{GetSportName(message.SpId)}", $"{DateTime.UtcNow.ToEpochTimestamp()}");
        }
        public void ProcessBetStop(string payload, IMessageBroker messageBroker, string correlationId)
        {

            //TODO: see comments below
            //var message = JsonSerializer.Deserialize<OnBetStopFixtureMessage>(payload);

            //if (message.SportId > 1000)
            //    return;

            //var finalPayload = new
            //{
            //    fixtureId = message.FixtureId,
            //    sportId = message.SportId,
            //    fixtureSpecifier = message.FixtureSpecifier,
            //    isBetStop = message.IsBetStop,
            //    setActiveMarketToInactive = message.SetActiveMarketsToInactive,
            //    timeStampSent = message.TimestampSent,
            //    timeStampReceived = message.TimestampReceived,
            //    timeStampDispatched = message.TimestampDispatched,
            //    raw = message.Raw
            //};

            //var betStopFixture = new BetStopFixtureModel
            //{
            //    FixtureId = message.FixtureId,
            //    FixtureSpecifier = message.FixtureSpecifier,
            //    IsBetStop = message.IsBetStop,
            //    CorrelationId = correlationId,
            //};

            //var payloadStr = JsonSerializer.Serialize(finalPayload, _options);

            //PublishMessage(messageBroker, payloadStr, "onbetstopfixture", $"uof.onbetstop.{GetSportName(message.SportId)}", $"{DateTime.UtcNow.ToEpochTimestamp()}");

            //_memCache.AddOrUpdateCache($"BetStopFixture:{message.FixtureSpecifier}", betStopFixture, 24);
        }

        public void ProcessBetStopGlobal(string payload, IMessageBroker messageBroker, string correlationId)
        {

            //TODO: see comments below
            var message = JsonSerializer.Deserialize<OnBetStopGlobalMessage>(payload);

            var finalPayload = new
            {
                isBetStop = message.IsBetStop
            };

            var payloadStr = JsonSerializer.Serialize(finalPayload, _options);

            PublishMessage(messageBroker, payloadStr, "onbetstopglobal", $"uof.onbetstopglobal", $"{DateTime.UtcNow.ToEpochTimestamp()}");
        }

        public void ProcessMarketSettlement(string payload, IMessageBroker messageBroker, string correlationId)
        {

            //TODO: see comments below
            //var message = JsonSerializer.Deserialize<OnBetSettlementMessage>(payload);

            //if (message.FixtureSpecifier.Contains("s88:match"))
            //    return;

            //// TODO
            //if (message.SportId > 1000)
            //    return;

            //var finalPayload = new
            //{
            //    fixtureId = message.FixtureId,
            //    sport = new
            //    {
            //        Id = message.Sport.Id
            //    },
            //    sportId = message.SportId,
            //    fixtureSpecifier = message.FixtureSpecifier,
            //    selections = message.Selections.Select(s => new
            //    {
            //        marketTemplate = new 
            //        {
            //            id = s.MarketTemplate.Id,
            //            specifier = s.MarketTemplate.Specifier
            //        },
            //        selectionTemplate = new 
            //        {
            //            id = s.SelectionTemplate.Id
            //        },
            //        MaTeId = s.MaTeId,
            //        marketSpecifier = s.MarketSpecifier,
            //        selectionTemplateId = s.SelectionTemplateId,
            //        result = s.Result,
            //        voidFactor = s.VoidFactor,
            //        deadHeatFactor = s.DeadHeatFactor,

            //    }).ToList(),
            //    timeStampSent = message.TimestampSent,
            //    timeStampReceived = message.TimestampReceived,
            //    timeStampDispatched = message.TimestampDispatched,
            //    raw = message.Raw
            //};

            //var payloadStr = JsonSerializer.Serialize(finalPayload, _options);

            //PublishMessage(messageBroker, payloadStr, "onmarketsettlement", $"uof.onmarketsettlement.{GetSportName(message.SportId)}", $"{DateTime.UtcNow.ToEpochTimestamp()}");
        }

        public void ProcessMarketCancellation(string payload, IMessageBroker messageBroker, string correlationId)
        {
            //TODO: see comments below
            //var message = JsonSerializer.Deserialize<OnBetCancellationMessage>(payload);

            //if (message.FixtureSpecifier.Contains("s88:match"))
            //    return;

            //// TODO
            //if (message.SportId > 1000)
            //    return;

            //var finalPayload = new
            //{
            //    fixtureId = message.FixtureId,
            //    sport = new
            //    {
            //        Id = message.Sport.Id
            //    },
            //    sportId = message.SportId,
            //    fixtureSpecifier = message.FixtureSpecifier,
            //    markets = message.Markets.Select(s => new
            //    {
            //        marketTemplate = new 
            //        {
            //            Id = s.MarketTemplate.Id,
            //            specifier = s.MarketTemplate.Specifier
            //        },
            //        MaTeId = s.MaTeId,
            //        marketSpecifier = s.MarketSpecifier,
            //        startTime = s.StartTime,
            //        endTime = s.EndTime,
            //        voidReason = s.VoidReason
            //    }).ToList(),
            //    timeStampSent = message.TimestampSent,
            //    timeStampReceived = message.TimestampReceived,
            //    timeStampDispatched = message.TimestampDispatched,
            //    raw = message.Raw

            //};

            //var payloadStr = JsonSerializer.Serialize(finalPayload, _options);

            //PublishMessage(messageBroker, payloadStr, "onmarketcancellation", $"uof.onmarketcancellation.{GetSportName(message.SportId)}", $"{DateTime.UtcNow.ToEpochTimestamp()}");
        }

        public void ProcessHeartbeat(string payload, IMessageBroker messageBroker, string correlationId)
        {
            var message = JsonSerializer.Deserialize<OFHeartbeatMessage>(payload);

            var finalPayload = new
            {
                timeStamp = message.Timestamp
            };

            var payloadStr = JsonSerializer.Serialize(finalPayload, _options);

            PublishMessage(messageBroker, payloadStr, "onheartbeat", $"uof.onheartbeat", $"{DateTime.UtcNow.ToEpochTimestamp()}");
        }

        public void UpdateFixtureConfiguration(OnFixtureChangeMessage message, IMessageBroker messageBroker, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations)
        {
            //TODO: see comments below

            //var sportConfiguration = sportConfigurations.FirstOrDefault(c => c.SportId == message.SportId);

            //if (!sportConfiguration.MaxFixtures.HasValue)
            //    return;

            //var fixtureConfiguration = fixtureConfigurations.FirstOrDefault(c => c.SportId == message.SportId);

            //if (fixtureConfiguration == null)
            //{
            //    fixtureConfiguration = new ClientLiveFixtureModel
            //    {
            //        SportId = message.SpId,
            //        ClientId = _configuration["Custom:ClientId"],
            //        Fixtures = new List<FixtureModel>
            //        {
            //            new FixtureModel
            //            {
            //                FixtureSpecifier = message.FiSp,
            //                FixtureStartDate = message.FiStDa
            //            }
            //        }
            //    };

            //    fixtureConfigurations.Add(fixtureConfiguration);

            //    // push message here
            //    var configurationPayload = new OnFixtureConfigUpdateMessage
            //    {
            //        ClientId = _configuration["Custom:ClientId"],
            //        SportId = message.SpId,
            //        FixtureSpecifier = message.FiSp,
            //        FiStDa = message.FiStDa,
            //        MaxCount = sportConfiguration.MaxFixtures.Value
            //    };

            //    messageBroker.Publish(configurationPayload, "OnConfigurationUpdate", "r.oe.engine.client.onconfigurationupdate", Guid.NewGuid().ToString());
            //}
            //else
            //{
            //    var fixture = fixtureConfiguration.Fixtures.FirstOrDefault(c => c.FixtureSpecifier == message.FixtureSpecifier);

            //    if (fixture == null)
            //    {
            //        fixture = new FixtureModel
            //        {
            //            FixtureSpecifier = message.FiSp,
            //            FixtureStartDate = message.FiStDa
            //        };

            //        fixtureConfiguration.Fixtures.Add(fixture);

            //        // push message here
            //        var configurationPayload = new OnFixtureConfigUpdateMessage
            //        {
            //            ClientId = _configuration["Custom:ClientId"],
            //            SportId = message.SpId,
            //            FixtureSpecifier = message.FiSp,
            //            FiStDa = message.FiStDa,
            //            MaxCount = sportConfiguration.MaxFixtures.Value
            //        };

            //        messageBroker.Publish(configurationPayload, "OnConfigurationUpdate", "r.oe.engine.client.onconfigurationupdate", Guid.NewGuid().ToString());
            //    }
            //}
        }

        #endregion

        #region Utilities

        private string GetSportName(int sportId)
        {
            switch (sportId)
            {
                case 1:
                    return "soccer";
                case 2:
                    return "basketball";
                case 3:
                    return "baseball";
                case 4:
                    return "icehockey";
                case 5:
                    return "tennis";
                case 10:
                    return "boxing";
                case 19:
                    return "snooker";
                case 20:
                    return "tabletennis";
                case 23:
                    return "volleyball";
                case 31:
                    return "badminton";
                case 117:
                    return "mma";
                case 137:
                    return "esoccer";
                case 153:
                    return "ebasketball";
                case 195:
                    return "eicehockey";
                default:
                    return "unsupported";
            }
        }

        private List<ClientOdds> GetClientOdds(int sportId, int activeSelectionCount, decimal margin, List<SelectionMessage> selections, string clientMarketConfig, string clientCode, string MaTeId, List<MarketTemplatePlayerPropsResponseDTO> marketTemplateGroups, bool asianClient)
        {
            if (string.IsNullOrEmpty(clientMarketConfig))
                return new List<ClientOdds>();

            if (!clientMarketConfig.Contains(clientCode))
                return new List<ClientOdds>();

            List<Tuple<string, decimal>> clients;
            clients = clientMarketConfig.Split('|').Select(s => new Tuple<string, decimal>(s.Split(':').First(), Convert.ToDecimal(s.Split(':').Last()))).Where(c => c.Item1 == clientCode).ToList();

            var result = new List<ClientOdds>();
            var isBinary = selections.Count > 2 ? false : true;

            // prepare market player props
            var playerPropMaTeIds = string.Join(",", marketTemplateGroups.Select(s => s.MarketTemplates).ToList()).Split(",").ToList();

            foreach (var client in clients)
            {
                foreach (var selection in selections)
                {
                    if (asianClient && sportId == 1 && _MaTeIds.Contains(MaTeId))
                    {
                        result.Add(new ClientOdds
                        {
                            ClientCode = client.Item1,
                            DecimalOdds = selection.Od,
                            MalaysianOdds = GetMalayOdds(selection.Od, isBinary),
                            HongkongOdds = GetHongkongOdds(selection.Od, isBinary),
                            IndonesianOdds = GetIndoOdds(selection.Od, isBinary),
                            SelectionTemplateId = selection.SeTeId
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
                                    SelectionTemplateId = selection.SeTeId
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
                                SelectionTemplateId = selection.SeTeId
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
                                SelectionTemplateId = selection.SeTeId
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
                                SelectionTemplateId = selection.SeTeId
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
                                SelectionTemplateId = selection.SeTeId
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
                                SelectionTemplateId = selection.SeTeId
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
                            SelectionTemplateId = selection.SeTeId
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
                            SelectionTemplateId = selection.SeTeId
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

        private List<ReferenceMessage> GetReferences(int ldRefId)
        {
            var references = new List<ReferenceMessage>();

            if (ldRefId == 0)
                return references;

            references.Add(new ReferenceMessage
            {
                Type = "LiveData",
                Id = ldRefId
            });

            return references;
        }

        private OFOddsChangeMessage PrepareOddsChangeMessage(OnMarketChangeMessage message)
        {
            if (message.Ma.Count > 0
                && message.Ma.Count() == message.Ma.Where(
                    c => c.MaStId == (int)MarketStatus.Inactive).Count()
                    && message.FiStId == (int)FixtureStatus.Prematch && message.FiStDa < DateTime.UtcNow)
            {
                message.FiStId = (int)FixtureStatus.Closed;
            }

            var homeScore = "0";
            var awayScore = "0";

            if (message.FiTyId == (int)FixtureType.Match)
            {
                var scoreSplit = message.Sc.Split('-');

                homeScore = scoreSplit.First().Trim();
                awayScore = scoreSplit.Last().Trim();
            }
            //TODO: see comments below
            return new OFOddsChangeMessage
            {
                SportId = message.SpId,
                SportName = message.SpNa,
                SegmentId = message.SegId,
                SegmentName = message.SegNa,
                LeagueId = message.LeId,
                LeagueName = message.LeNa,
                FixtureId = message.FiId,
                //LeagueReference = GetReferences(message.LeagueBetradarId),
                //LeagueRoundId = message.LeagueBetradarId,
                //LeagueRoundName = message.LeagueBetradarName,
                IsOfferedLive = message.IsOfLi,
                FixtureSpecifier = message.FiSp,
                FixtureProviderId = message.PrFiId,
                FixtureName = message.FiNa,
                FixtureStartDate = message.FiStDa.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.fffffff"),
                FixtureStatusId = message.FiStId,
                FixtureStatusName = ((FixtureStatus)message.FiStId).ToString(),
                FixtureTypeId = message.FiTyId,
                FixtureTypeName = ((FixtureType)message.FiTyId).ToString(),
                HomeRedCards = message.HoReCa,
                AwayRedCards = message.AwReCa,
                //HomeYellowCards = message.HomeYellowCards,
                //AwayYellowCards = message.AwayYellowCards,
                //HomeCorners = message.HomeCorners,
                //AwayCorners = message.AwayCorners,
                HomeScore = homeScore,
                AwayScore = awayScore,
                HomeId = message.HoId,
                //HomeReference = GetReferences(message.HomeBetradarId),
                AwayId = message.AwId,
                //AwayReference = GetReferences(message.AwayBetradarId),
                CurrentPeriod = message.CuPeId,
                CurrentMinute = message.CuMi,
                CurrentSecond = message.CuSec,
                CurrentServer = message.CuSer,
                //PeriodScores = message.PeriodScores.Select(s => new PeriodScoreMessage
                //{
                //    Home = s.Home,
                //    Away = s.Away,
                //    MatchStatusCode = s.MatchStatusCode,
                //    PeriodType = s.PeriodType
                //}).ToList(),
                TimeStampSent = message.TiSe,
                TimeStampReceived = message.TiRe,
                //TimeStampDispatched = message.TimestampDispatched,
                //SportNameTranslations = message.SportTranslations.Where(c => _languageCodes.Contains(c.LanguageCode))
                //    .Select(sport => new OFSportTranslation
                //    {
                //        LanguageCode = sport.LanguageCode,
                //        SportName = sport.SportName
                //    }).ToList(),
                //SegmentNameTranslations = message.SegmentTranslations.Where(c => _languageCodes.Contains(c.LanguageCode))
                //    .Select(segment => new OFSegmentTranslation
                //    {
                //        LanguageCode = segment.LanguageCode,
                //        SegmentName = segment.SegmentName
                //    }).ToList(),
                //LeagueNameTranslations = message.LeagueTranslations.Where(c => _languageCodes.Contains(c.LanguageCode))
                //    .Select(league => new OFLeagueTranslation
                //    {
                //        LanguageCode = league.LanguageCode,
                //        LeagueName = league.LeagueName
                //    }).ToList(),
                //FixtureNameTranslations = message.FixtureTranslations.Where(c => _languageCodes.Contains(c.LanguageCode))
                //    .Select(fixture => new OFFixtureTranslation
                //    {
                //        LanguageCode = fixture.LanguageCode,
                //        FixtureName = fixture.FixtureName
                //    }).ToList(),
                //Raw = message.Raw
            };
        }

        private void PublishBetStopMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
        {
            //TODO:
            //var betStopFixtureKey = $"BetStopFixture:{message.FiSp}";

            //var betStopFixture = _memCache.GetCache<BetStopFixtureModel>(betStopFixtureKey);

            //if (betStopFixture != null)
            //{
            //    if (betStopFixture.IsBetStop)
            //    {
            //        var onBetStopPayload = new
            //        {
            //            sportId = message.SpId,
            //            fixtureSpecifier = message.FiSp,
            //            isBetStop = false,
            //            setActiveMarketToInactive = false,
            //            timeStampSent = message.TimestampSent,
            //            timeStampReceived = message.TimestampReceived,
            //            raw = string.Empty
            //        };

            //        betStopFixture.IsBetStop = false;
            //        betStopFixture.CorrelationId = correlationId;

            //        var betStopPayloadStr = JsonSerializer.Serialize(onBetStopPayload, _options);

            //        PublishMessage(messageBroker, betStopPayloadStr, "onbetstopfixture", $"uof.onbetstop.{GetSportName(message.SportId)}", $"{DateTime.UtcNow.ToEpochTimestamp()}");

            //        _memCache.AddOrUpdateCache(betStopFixtureKey, betStopFixture, DateTime.UtcNow.AddHours(24));
            //    }
            //}
            //else
            //{
            //    var onBetStopPayload = new
            //    {
            //        sportId = message.SpId,
            //        fixtureSpecifier = message.FiSp,
            //        isBetStop = false,
            //        setActiveMarketToInactive = false,
            //        timeStampSent = message.TimestampSent,
            //        timeStampReceived = message.TimestampReceived,
            //        raw = string.Empty
            //    };

            //    betStopFixture = new BetStopFixtureModel
            //    {
            //        FixtureSpecifier = message.FiSp,
            //        IsBetStop = false,
            //        CorrelationId = correlationId,
            //    };

            //    var betStopPayloadStr = JsonSerializer.Serialize(onBetStopPayload, _options);

            //    PublishMessage(messageBroker, betStopPayloadStr, "onbetstopfixture", $"uof.onbetstop.{GetSportName(message.SportId)}", $"{DateTime.UtcNow.ToEpochTimestamp()}");

            //    _memCache.AddOrUpdateCache(betStopFixtureKey, betStopFixture, DateTime.UtcNow.AddHours(24));
            //}
        }

        private void PublishMessage(IMessageBroker messageBroker, string payload, string messageType, string routingKey, string correlationId)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            messageBroker.ExternalPublish(payload, messageType, routingKey, correlationId);

            if (stopwatch.Elapsed.TotalMilliseconds >= 500)
            {
                Console.WriteLine($"PublishMessage: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
            }

            stopwatch.Stop();
        }
        private void PublishMessage<T>(IMessageBroker messageBroker, T payload, string messageType, string routingKey, string correlationId)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var strPayload = JsonSerializer.Serialize(payload, _options);
            messageBroker.ExternalPublish(payload, messageType, routingKey, correlationId);

            if (stopwatch.Elapsed.TotalMilliseconds >= 500)
            {
                Console.WriteLine($"PublishMessage: {stopwatch.Elapsed:hh\\:mm\\:ss\\.fff}");
            }

            stopwatch.Stop();
        }

        private void CheckDelay(OnFixtureChangeMessage message, string position)
        {
            //TODO: see comments below

            //var currentDate = DateTime.UtcNow;
            //var timestampDelivered = currentDate.ToEpochTimestamp();

            //var totalMilliseconds = (timestampDelivered.ToDateTime() - message.TimestampReceived.ToDateTime()).TotalMilliseconds;

            ////if (totalMilliseconds >= 2000)
            ////{
            //Console.WriteLine($"{currentDate:MM-dd-yyyy HH:mm:ss.ffff} - {message.SpId} - {message.FixtureSpecifier} - {message.TimestampReceived} - {timestampDelivered} - {totalMilliseconds} - {position}");
            ////}
        }

        #endregion
    }
}
