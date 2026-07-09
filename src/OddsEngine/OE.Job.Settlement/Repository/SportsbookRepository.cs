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
                var fixtureFilter = Builders<Fixture>.Filter.Where(c => fixtureStatus.Contains(c.FiStId) && c.FiTyId == (int)FixtureType.Match && c.FiStDa <= DateTime.UtcNow);
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
                        || (fixture.FiTyId == (int)FixtureType.Match && fixture.FiStDa.AddHours(6) < DateTime.UtcNow))
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
                            HoNa = fixture.HoNa,
                            AwNa = fixture.AwNa,
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
