using MongoDB.Driver;
using OE.Job.Settlement.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Enums;
using OE.Shared.Extensions;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.Settlement.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {
        #region Fields

        private readonly ILogger<SportsbookRepository> _logger;
        private readonly IMongoDatabase _db;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        private readonly string _apiKey;
        private readonly bool _editSportsbook;

        private readonly Counter<long> _jobRunProcessedTotal;

        private IMongoCollection<Fixture> fixtureCollection;

        #endregion

        #region Ctor

        public SportsbookRepository(ILogger<SportsbookRepository> logger, HttpClient httpClient, IConfiguration configuration, Instrumentation instrumentation)
        {
            _logger = logger;
            _configuration = configuration;
            _jobRunProcessedTotal = instrumentation.JobRunProcessedTotal;

            var client = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            _db = client.GetDatabase("Sportsbook");
            fixtureCollection = _db.GetCollection<Fixture>("Fixture");

            _httpClient = httpClient;

            _apiKey = _configuration["Custom:ApiKey"];
            _editSportsbook = Convert.ToBoolean(_configuration["Custom:EditSportsbook"]);
        }

        #endregion

        #region Methods

        public async Task ProcessSettlement(IMessageBroker messageBroker)
        {
            var fixtureStatus = new List<int> { 0, 1 };

            if (_editSportsbook)
            {
                var fixtureFilter = Builders<Fixture>.Filter.Where(c => fixtureStatus.Contains(c.FiStId) && c.FiTyId == (int)FixtureType.Match && c.FiStDa < DateTime.UtcNow);
                var fixtures = await fixtureCollection.Find(fixtureFilter).ToListAsync();
                await ProcessSportsbookOnFixtureChange(messageBroker, fixtures, fixtureStatus);

            }
        }

        #endregion

        #region Utilities

        private async Task ProcessSportsbookOnFixtureChange(IMessageBroker messageBroker, List<Fixture> fixtures, List<int> fixtureStatus)
        {
            foreach (var fixture in fixtures)
            {
                var statistics = string.Empty;
                var timestampDelivered = DateTime.UtcNow.ToEpochTimestamp();

                try
                {
                    if ((fixture.FiStDa < DateTime.UtcNow && fixture.IsOfLi == false && fixture.FiStId == 0) //Prematch fixture that is already started
                        || (fixture.FiTyId == (int)FixtureType.Match && fixture.FiStDa < DateTime.UtcNow.AddHours(6)))
                    {
                        var payload = new OnFixtureChangeMessage
                        {
                            ChTyId = fixture.ChTyId,
                            PrFiId = fixture.PrFiId,
                            SpId = fixture.SpId,
                            SpNa = fixture.SpNa,
                            SegId = fixture.SegId,
                            SegNa = fixture.SegNa,
                            LeId = fixture.LeId,
                            LeNa = fixture.LeNa,
                            HoId = fixture.HoId,
                            AwId = fixture.AwId,
                            FiId = fixture.FiId,
                            FiSp = fixture.FiSp,
                            FiNa = fixture.FiNa,
                            FiStDa = fixture.FiStDa,
                            FiTyId = fixture.FiTyId,
                            FiTyNa = fixture.FiTyNa,
                            FiStId = (int)FixtureStatus.Closed,
                            FiStNa = FixtureStatus.Closed.ToString(),
                            Sc = fixture.Sc,
                            IsSu = fixture.IsSu,
                            ClCo = fixture.ClCo,
                            IsOfLi = fixture.IsOfLi,
                            TiSe = timestampDelivered,
                            TiRe = timestampDelivered,
                            CuPe = fixture.CuPe,
                            AwReCa = fixture.AwReCa,
                            CuMi = fixture.CuMi,
                            CuSe = fixture.CuSe,
                            SoOr = fixture.FiSoOr,
                            ToTi = fixture.ToTi,
                            StUr = fixture.StUr,
                            IsBo = fixture.IsBo
                        };

                        messageBroker.Publish(payload, MessageType.ON_FIXTURE_CHANGE, $"r.bg.receiver.{MessageType.ON_FIXTURE_CHANGE.ToLower()}", Guid.NewGuid().ToString());

                        if (_jobRunProcessedTotal.Enabled)
                            _jobRunProcessedTotal.Add(1);

                        _logger.LogInformationEx($"{DateTime.UtcNow} : {fixture.SpNa} - {fixture.FiNa} - {fixture.FiStDa}");

                        continue;
                    }

                    //TODO: recovery from provider to check the actual status

                    //var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.betradar.com/v1/sports/en/sport_events/{fixture.FiSp}/fixture.xml");
                    //request.Headers.Add("x-access-token", _apiKey);

                    //var response = await _httpClient.SendAsync(request);

                    //if (response.IsSuccessStatusCode)
                    //{
                    //    var result = await response.Content.ReadAsStringAsync();

                    //    XmlDocument doc = new XmlDocument();

                    //    doc.LoadXml(result);

                    //    var node = doc.ChildNodes[1].ChildNodes[0];

                    //    var startDate = Convert.ToDateTime(node.Attributes["start_time"].Value).ToUniversalTime();
                    //    var status = node.Attributes["status"].Value;
                    //    var isOfferedLive = node.Attributes["liveodds"].Value == "not_available" ? false : true;
                    //    var isBuyable = node.Attributes["liveodds"].Value == "buyable" ? true : false;

                    //    if (fixture.SpId != 21 && (status == "suspended" || status == "ended" || status == "closed" || status == "cancelled" || status == "abandoned" || status == "delayed" || status == "unknown" || status == "postponed" || status == "interrupted" || status == "not_started"))
                    //    {

                    //        var payload = new OnFixtureChangeMessage
                    //        {
                    //            ChangeType = FixtureChangeType.Other,
                    //            ProviderFixtureId = fixture.PrFiId,
                    //            FixtureSpecifier = fixture.FiSp,
                    //            SportId = fixture.SpId,
                    //            TimestampReceived = timestampDelivered,
                    //            TimestampSent = timestampDelivered
                    //        };

                    //        messageBroker.Publish(payload, "OnFixtureChange", $"r.uof.onfixturechange.{fixture.SpId}", Guid.NewGuid().ToString());
                    //        Console.WriteLine($"{DateTime.UtcNow} : {fixture.SpNa} - {fixture.FiNa} - {fixture.FiStDa}");

                    //        continue;
                    //    }

                    //    if ((isOfferedLive == false || isBuyable == true) && fixture.FiStId == (int)FixtureStatus.Prematch && startDate < DateTime.UtcNow && fixture.SpId != 21)
                    //    {
                    //        var payload = new OnFixtureChangeMessage
                    //        {
                    //            ChangeType = FixtureChangeType.Other,
                    //            ProviderFixtureId = fixture.PrFiId,
                    //            FixtureSpecifier = fixture.FiSp,
                    //            SportId = fixture.SpId,
                    //            TimestampReceived = timestampDelivered,
                    //            TimestampSent = timestampDelivered
                    //        };

                    //        messageBroker.Publish(payload, "OnFixtureChange", $"r.uof.onfixturechange.{fixture.SpId}", Guid.NewGuid().ToString());
                    //        Console.WriteLine($"{DateTime.UtcNow} : {fixture.SpNa} - {fixture.FiNa} - {fixture.FiStDa}");
                    //    }
                    //}
                    //else
                    //{
                    //    if (vs.Contains(fixture.SpId) && fixture.FiStDa < DateTime.UtcNow)
                    //    {
                    //        var payload = new OnFixtureChangeMessage
                    //        {
                    //            ChangeType = FixtureChangeType.Other,
                    //            ProviderFixtureId = fixture.PrFiId,
                    //            FixtureSpecifier = fixture.FiSp,
                    //            SportId = fixture.SpId,
                    //            TimestampReceived = timestampDelivered,
                    //            TimestampSent = timestampDelivered
                    //        };

                    //        messageBroker.Publish(payload, "OnFixtureChange", $"r.uof.onfixturechange.{fixture.SpId}", Guid.NewGuid().ToString());
                    //        Console.WriteLine($"{DateTime.UtcNow} : {fixture.SpNa} - {fixture.FiNa} - {fixture.FiStDa}");
                    //    }
                    //}
                }
                catch (Exception ex)
                {
                    _logger.LogErrorEx($"Error while processing onFixtureChange", ex);

                    //if (vs.Contains(fixture.SpId) && fixture.FiStDa < DateTime.UtcNow)
                    //{
                    //    var payload = new OnFixtureChangeMessage
                    //    {
                    //        ChangeType = FixtureChangeType.Other,
                    //        ProviderFixtureId = fixture.PrFiId,
                    //        FixtureSpecifier = fixture.FiSp,
                    //        SportId = fixture.SpId,
                    //        TimestampReceived = timestampDelivered,
                    //        TimestampSent = timestampDelivered
                    //    };

                    //    messageBroker.Publish(payload, "OnFixtureChange", $"r.oe.adapter.onfixturechange", Guid.NewGuid().ToString());
                    //    Console.WriteLine($"{DateTime.UtcNow} : {fixture.SpNa} - {fixture.FiNa} - {fixture.FiStDa}");
                    //}
                }
            }
        }

        #endregion
    }
}
