using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
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

        #region ApplyTranslationDeltas

        private static Fixture MakeFixture(int spId = 1, int segId = 2, int leId = 3, string hoId = "H1", string awId = "A1")
        {
            return new Fixture { SpId = spId, SegId = segId, LeId = leId, HoId = hoId, AwId = awId };
        }

        private static OnInternalOddsChangeMessage MakePayload()
        {
            return new OnInternalOddsChangeMessage
            {
                Ma = new List<InternalMarketMessage>
                {
                    new InternalMarketMessage
                    {
                        MaTeId = "MT1",
                        Se = new List<InternalSelectionMessage>
                        {
                            new InternalSelectionMessage { MaTeId = "MT1", SeTeId = "ST1" }
                        }
                    }
                }
            };
        }

        private static List<TranslationMessage> Delta(string text) =>
            new List<TranslationMessage> { new TranslationMessage { LaCo = "ZH", Tx = text } };

        [Fact]
        public void ApplyTranslationDeltas_HomeOnlyDelta_SetsHoTrAndLeavesAwTrNull()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            var payload = MakePayload();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["H1"] = Delta("主队") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Same(teamDeltas["H1"], payload.HoTr);
            Assert.Null(payload.AwTr);
            Assert.Null(payload.SpTr);
            Assert.Null(payload.SegTr);
            Assert.Null(payload.LeTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_AwayOnlyDelta_SetsAwTrAndLeavesHoTrNull()
        {
            var fixture = MakeFixture(hoId: "H1", awId: "A1");
            var payload = MakePayload();
            var teamDeltas = new Dictionary<string, List<TranslationMessage>> { ["A1"] = Delta("客队") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(),
                teamDeltas: teamDeltas,
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Null(payload.HoTr);
            Assert.Same(teamDeltas["A1"], payload.AwTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_LeagueDeltaForDifferentFixture_LeavesLeTrNull()
        {
            var fixture = MakeFixture(leId: 3);
            var payload = MakePayload();
            var leagueDeltas = new Dictionary<int, List<TranslationMessage>> { [999] = Delta("Some League") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: leagueDeltas,
                teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Null(payload.LeTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_MarketTemplateDelta_SetsMaTeTrOnMatchingMarketOnly()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            payload.Ma.Add(new InternalMarketMessage { MaTeId = "MT2", Se = new List<InternalSelectionMessage>() });
            var marketTemplateDeltas = new Dictionary<string, List<TranslationMessage>> { ["MT1"] = Delta("Over/Under") };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: marketTemplateDeltas,
                selectionTemplateDeltas: new());

            Assert.Same(marketTemplateDeltas["MT1"], payload.Ma[0].MaTeTr);
            Assert.Null(payload.Ma[1].MaTeTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_SelectionTemplateDelta_MatchesByMarketAndSelectionTemplateIdPair()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();
            // Same SeTeId under a different MaTeId must NOT match — the key is the (MaTeId, SeTeId) pair.
            var selectionTemplateDeltas = new Dictionary<(string, string), List<TranslationMessage>>
            {
                [("MT-OTHER", "ST1")] = Delta("Over"),
                [("MT1", "ST1")] = Delta("大于")
            };

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(),
                selectionTemplateDeltas: selectionTemplateDeltas);

            Assert.Same(selectionTemplateDeltas[("MT1", "ST1")], payload.Ma[0].Se[0].SeTeTr);
        }

        [Fact]
        public void ApplyTranslationDeltas_NoMatchingDeltas_LeavesEverythingNull()
        {
            var fixture = MakeFixture();
            var payload = MakePayload();

            MongoTranslationRepository.ApplyTranslationDeltas(
                payload, fixture,
                sportDeltas: new(), segmentDeltas: new(), leagueDeltas: new(), teamDeltas: new(),
                marketTemplateDeltas: new(), selectionTemplateDeltas: new());

            Assert.Null(payload.SpTr);
            Assert.Null(payload.SegTr);
            Assert.Null(payload.LeTr);
            Assert.Null(payload.HoTr);
            Assert.Null(payload.AwTr);
            Assert.Null(payload.Ma[0].MaTeTr);
            Assert.Null(payload.Ma[0].Se[0].SeTeTr);
        }

        #endregion
    }
}
