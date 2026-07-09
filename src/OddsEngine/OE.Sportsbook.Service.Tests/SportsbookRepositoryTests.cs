using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using OE.Sportsbook.Service.Telemetry;

namespace OE.Sportsbook.Service.Tests
{
    // Covers the CompId/SeNaTr mapping (2026-07-09): Co:/SeTe: are mutually exclusive lookup keys
    // that both resolve into the single SeNaTr field, across the Mongo write path and both
    // ON_ODDS_CHANGE builders (live market-change and fixture-triggered).
    public class SportsbookRepositoryTests
    {
        private static SportsbookRepository CreateRepository()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Never connected to by these tests — MongoClient/GetCollection are lazy.
                    ["Custom:MongoDB-Sportsbook"] = "mongodb://localhost:27017"
                })
                .Build();

            return new SportsbookRepository(
                configuration,
                new FakeMemCache(),
                new Helper(),
                new Instrumentation(),
                NullLogger<SportsbookRepository>.Instance);
        }

        private static OnMarketChangeMessage MarketChangeMessage(int? compId)
        {
            return new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                FiId = 1,
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = "1234567890",
                        PrMaId = "M1",
                        MaId = 1,
                        MaTeId = "16",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                PrFiId = "1234567890",
                                PrMaId = "M1",
                                PrSeId = "S1",
                                SeId = 1,
                                MaTeId = "16",
                                SeTeId = "1",
                                SeTeNa = "Home",
                                SeNa = "Arsenal",
                                CompId = compId,
                                // Enrichment already ran (OE.Adapter.Service) by the time this
                                // reaches OE.Sportsbook.Service — SeNaTr is whatever it resolved
                                // (via Co:{CompId} or SeTe:{MaTeId}:{SeTeId}, mutually exclusive).
                                SeNaTr = new List<TranslationMessage>
                                {
                                    new TranslationMessage { LaCo = "ZH", Tx = compId != null ? "阿森纳" : "主胜" }
                                },
                            }
                        }
                    }
                }
            };
        }

        [Fact]
        public void ToMarketAndSelectionChange_MapsCompIdOntoTheSelectionMongoUpdate()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: 810068);

            var (_, selectionWrites) = repository.ToMarketAndSelectionChange(message, "cid");

            var update = Assert.IsType<UpdateOneModel<Selection>>(Assert.Single(selectionWrites));
            var rendered = update.Update.Render(new RenderArgs<Selection>(
                BsonSerializer.SerializerRegistry.GetSerializer<Selection>(),
                BsonSerializer.SerializerRegistry));

            Assert.Equal(810068, rendered["$set"]["CompId"].AsInt32);
        }

        [Fact]
        public void ToMarketAndSelectionChange_NonCompetitorSelection_SetsCompIdNull()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);

            var (_, selectionWrites) = repository.ToMarketAndSelectionChange(message, "cid");

            var update = Assert.IsType<UpdateOneModel<Selection>>(Assert.Single(selectionWrites));
            var rendered = update.Update.Render(new RenderArgs<Selection>(
                BsonSerializer.SerializerRegistry.GetSerializer<Selection>(),
                BsonSerializer.SerializerRegistry));

            Assert.True(rendered["$set"]["CompId"].IsBsonNull);
        }

        [Fact]
        public void PrepareInternalOddsChangeMessage_MarketChange_CompetitorSelection_CarriesSeNaTrAndCompId()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: 810068);

            var payload = repository.PrepareInternalOddsChangeMessage(message, "cid");

            var selection = payload.Ma.Single().Se.Single();
            Assert.Equal(810068, selection.CompId);
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("阿森纳", selection.SeNaTr!.Single().Tx);
        }

        [Fact]
        public void PrepareInternalOddsChangeMessage_MarketChange_NonCompetitorSelection_CarriesSeNaTrWithNullCompId()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);

            var payload = repository.PrepareInternalOddsChangeMessage(message, "cid");

            var selection = payload.Ma.Single().Se.Single();
            Assert.Null(selection.CompId);
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("主胜", selection.SeNaTr!.Single().Tx);
        }

        private static Selection MongoSelection(int? compId)
        {
            return new Selection
            {
                Id = "0:1:1:1",
                FiId = 1,
                MaId = 1,
                MaTeId = "16",
                SeTeId = "1",
                SeNa = "Arsenal",
                CompId = compId,
                // Persisted display-name translation — same field regardless of Co:/SeTe: source
                // (see MongoTranslationRepository.WriteMarketTranslations).
                SeNaTr = new List<SelectionTranslationMDB>
                {
                    new SelectionTranslationMDB { LaCo = "ZH", SeNa = compId != null ? "阿森纳" : "主胜" }
                }
            };
        }

        private static Market MongoMarket()
        {
            return new Market
            {
                Id = "0:1:1",
                FiId = 1,
                MaId = 1,
                MaTeId = "16",
                PrMaId = "M1"
            };
        }

        private static OnFixtureChangeMessage FixtureChangeMessage()
        {
            return new OnFixtureChangeMessage { ChTyId = 0, PrFiId = "1234567890", FiId = 1 };
        }

        // This is the fixture-status-triggered ON_ODDS_CHANGE rebuild (ProcessOnFixtureChangeMessage)
        // — a real gap existed here (fixed 2026-07-09, then simplified further when SeTeTr/SeTr
        // were unified into SeNaTr): it used to dump the persisted SeNaTr into SeTeTr
        // unconditionally, ignoring CompId. Now it's a straight passthrough — no branch needed.
        [Fact]
        public void PrepareInternalOddsChangeMessage_FixtureTriggered_CompetitorSelection_CarriesSeNaTrAndCompId()
        {
            var repository = CreateRepository();

            var payload = repository.PrepareInternalOddsChangeMessage(
                FixtureChangeMessage(),
                new List<Market> { MongoMarket() },
                new List<Selection> { MongoSelection(compId: 810068) },
                "cid");

            var selection = payload.Ma.Single().Se.Single();
            Assert.Equal(810068, selection.CompId);
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("阿森纳", selection.SeNaTr!.Single().Tx);
        }

        [Fact]
        public void PrepareInternalOddsChangeMessage_FixtureTriggered_NonCompetitorSelection_CarriesSeNaTrWithNullCompId()
        {
            var repository = CreateRepository();

            var payload = repository.PrepareInternalOddsChangeMessage(
                FixtureChangeMessage(),
                new List<Market> { MongoMarket() },
                new List<Selection> { MongoSelection(compId: null) },
                "cid");

            var selection = payload.Ma.Single().Se.Single();
            Assert.Null(selection.CompId);
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("主胜", selection.SeNaTr!.Single().Tx);
        }
    }
}
