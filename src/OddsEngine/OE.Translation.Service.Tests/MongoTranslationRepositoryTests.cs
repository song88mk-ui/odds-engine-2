using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Translation.Service.Repositories;

namespace OE.Translation.Service.Tests
{
    public class MongoTranslationRepositoryTests
    {
        // Moved from OE.Sportsbook.Service.Tests together with the change path: the translation-only
        // ON_ODDS_CHANGE payload is synthesized from the Mongo fixture document.
        [Fact]
        public void ToFixtureChangeMessage_MapsTheFieldsTheWebsocketPathNeeds()
        {
            var fixture = new Fixture
            {
                Id = "0:1234567890",
                ChTyId = 0,
                PrFiId = "1234567890",
                FiId = 42,
                SpId = 1,
                SpNa = "Soccer",
                SegId = 2,
                SegNa = "England",
                LeId = 3,
                LeNa = "Premier League",
                FiNa = "Arsenal vs. Chelsea",
                FiStId = 1,
                FiSoOr = 7,
                HoId = "H1",
                HoNa = "Arsenal",
                AwId = "A1",
                AwNa = "Chelsea",
                ClCo = "CL1"
            };

            var message = MongoTranslationRepository.ToFixtureChangeMessage(fixture);

            Assert.Equal("0:1234567890", message.Id); // recomputed from ChTyId + PrFiId
            Assert.Equal(42, message.FiId);
            Assert.Equal(1, message.SpId);
            Assert.Equal("Arsenal", message.HoNa);
            Assert.Equal("Chelsea", message.AwNa);
            Assert.Equal(7, message.SoOr); // FiSoOr -> SoOr
            Assert.Equal("None", message.Fa);
        }
    }
}
