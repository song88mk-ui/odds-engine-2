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
        internal static SportsbookRepository CreateRepository()
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

        private static OnFixtureChangeMessage FixtureChangeMessage(string hoNa, string awNa)
        {
            return new OnFixtureChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                FiId = 1,
                HoId = "H1",
                AwId = "A1",
                HoNa = hoNa,
                AwNa = awNa
            };
        }

        // 2026-07-09 fix: some producers (e.g. OE.Job.Settlement's force-close path) build a
        // fixture-change message without HoNa/AwNa — an unconditional Set was overwriting an
        // already-correct name with null/blank. Blank now means "no information," matching the
        // existing Sc/Fa fields in this same update, not "clear the field."
        [Fact]
        public void ToFixture_BlankHoNaAwNa_OmitsThemFromTheUpdate()
        {
            var repository = CreateRepository();
            var message = FixtureChangeMessage(hoNa: null!, awNa: string.Empty);

            var update = Assert.IsType<UpdateOneModel<Fixture>>(Assert.Single(repository.ToFixture(message)));
            var rendered = update.Update.Render(new RenderArgs<Fixture>(
                BsonSerializer.SerializerRegistry.GetSerializer<Fixture>(),
                BsonSerializer.SerializerRegistry));

            Assert.False(rendered["$set"].AsBsonDocument.Contains("HoNa"));
            Assert.False(rendered["$set"].AsBsonDocument.Contains("AwNa"));
            // HoId/AwId are unaffected — they're always carried, even on messages missing names.
            Assert.Equal("H1", rendered["$set"]["HoId"].AsString);
            Assert.Equal("A1", rendered["$set"]["AwId"].AsString);
        }

        [Fact]
        public void ToFixture_RealHoNaAwNa_IncludesThemInTheUpdate()
        {
            var repository = CreateRepository();
            var message = FixtureChangeMessage(hoNa: "Arsenal", awNa: "Chelsea");

            var update = Assert.IsType<UpdateOneModel<Fixture>>(Assert.Single(repository.ToFixture(message)));
            var rendered = update.Update.Render(new RenderArgs<Fixture>(
                BsonSerializer.SerializerRegistry.GetSerializer<Fixture>(),
                BsonSerializer.SerializerRegistry));

            Assert.Equal("Arsenal", rendered["$set"]["HoNa"].AsString);
            Assert.Equal("Chelsea", rendered["$set"]["AwNa"].AsString);
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

        // Player markets (GM-528): the fixture roster (OnFixtureChangeMessage.Pl) persists to
        // Fixture.Pl (names only — PlNaTr stays owned by OE.Translation.Service) and passes
        // through to the websocket payload; the market-level CompetitorOne persists to
        // Market.CompId and rides both ON_ODDS_CHANGE builders. Null roster / absent CompId must
        // never wipe stored state.
        [Fact]
        public void ToFixture_RosterPresent_PersistsPlayerDocs_NamesOnly()
        {
            var repository = CreateRepository();
            var message = FixtureChangeMessage();
            message.Pl = new List<PlayerMessage>
            {
                new PlayerMessage { PlId = 150762, PlNa = "Sharma, Rohit" },
                new PlayerMessage { PlId = 150763, PlNa = "Kohli, Virat" }
            };

            var update = Assert.IsType<UpdateOneModel<Fixture>>(Assert.Single(repository.ToFixture(message)));
            var rendered = update.Update.Render(new RenderArgs<Fixture>(
                BsonSerializer.SerializerRegistry.GetSerializer<Fixture>(),
                BsonSerializer.SerializerRegistry));

            var players = rendered["$set"]["Pl"].AsBsonArray;
            Assert.Equal(2, players.Count);
            // PlId is the [BsonId] of the embedded doc, so the driver renders it as _id
            Assert.Equal(150762, players[0]["_id"].AsInt32);
            Assert.Equal("Sharma, Rohit", players[0]["PlNa"].AsString);
        }

        [Fact]
        public void ToFixture_NoRoster_DoesNotTouchStoredRoster()
        {
            var repository = CreateRepository();

            var update = Assert.IsType<UpdateOneModel<Fixture>>(Assert.Single(repository.ToFixture(FixtureChangeMessage())));
            var rendered = update.Update.Render(new RenderArgs<Fixture>(
                BsonSerializer.SerializerRegistry.GetSerializer<Fixture>(),
                BsonSerializer.SerializerRegistry));

            Assert.False(rendered["$set"].AsBsonDocument.Contains("Pl"));
        }

        [Fact]
        public void ToMarketAndSelectionChange_MarketCompIdPresent_PersistsIt()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);
            message.Ma[0].CompId = 150762;

            var (marketWrites, _) = repository.ToMarketAndSelectionChange(message, "cid");

            var update = Assert.IsType<UpdateOneModel<Market>>(Assert.Single(marketWrites));
            var rendered = update.Update.Render(new RenderArgs<Market>(
                BsonSerializer.SerializerRegistry.GetSerializer<Market>(),
                BsonSerializer.SerializerRegistry));

            Assert.Equal(150762, rendered["$set"]["CompId"].AsInt32);
        }

        [Fact]
        public void ToMarketAndSelectionChange_MarketCompIdAbsent_DoesNotTouchStoredValue()
        {
            var repository = CreateRepository();

            var (marketWrites, _) = repository.ToMarketAndSelectionChange(MarketChangeMessage(compId: null), "cid");

            var update = Assert.IsType<UpdateOneModel<Market>>(Assert.Single(marketWrites));
            var rendered = update.Update.Render(new RenderArgs<Market>(
                BsonSerializer.SerializerRegistry.GetSerializer<Market>(),
                BsonSerializer.SerializerRegistry));

            Assert.False(rendered["$set"].AsBsonDocument.Contains("CompId"),
                "a message without CompId must not add it to the $set (it would wipe the stored id)");
        }

        // The market-level CompId rides both ON_ODDS_CHANGE builders so the translation-only push
        // path can recompose player-market names; the roster itself never leaves this service
        // (the player label reaches clients through MaNa/MaTeTr).
        [Fact]
        public void PrepareInternalOddsChangeMessage_MarketChange_CarriesMarketCompId()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);
            message.Ma[0].CompId = 150762;

            var payload = repository.PrepareInternalOddsChangeMessage(message, "cid");

            Assert.Equal(150762, payload.Ma.Single().CompId);
        }

        [Fact]
        public void PrepareInternalOddsChangeMessage_FixtureTriggered_CarriesStoredMarketCompId()
        {
            var repository = CreateRepository();
            var market = MongoMarket();
            market.CompId = 150762;

            var payload = repository.PrepareInternalOddsChangeMessage(
                FixtureChangeMessage(),
                new List<Market> { market },
                new List<Selection> { MongoSelection(compId: null) },
                "cid");

            Assert.Equal(150762, payload.Ma.Single().CompId);
        }

        [Fact]
        public void ToMarketAndSelectionChange_EmptyMarketList_ReturnsNoWrites()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);
            message.Ma = new List<MarketMessage>();

            var (marketWrites, selectionWrites) = repository.ToMarketAndSelectionChange(message, "cid");

            Assert.Empty(marketWrites);
            Assert.Empty(selectionWrites);
        }

        [Fact]
        public void ToMarketAndSelectionChange_MarketWithNoSelections_WritesMarketButNoSelections()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);
            message.Ma[0].Se = new List<SelectionMessage>();

            var (marketWrites, selectionWrites) = repository.ToMarketAndSelectionChange(message, "cid");

            Assert.Single(marketWrites);
            Assert.Empty(selectionWrites);
        }

        // A selection-less OPEN market is junk and stays out of the published payload...
        [Fact]
        public void PrepareInternalOddsChangeMessage_MarketChange_OpenMarketWithNoSelections_OmitsMarketFromPayload()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);
            message.Ma[0].MaStId = (int)OE.Shared.Enums.MarketStatus.Active;
            message.Ma[0].Se = new List<SelectionMessage>();

            var payload = repository.PrepareInternalOddsChangeMessage(message, "cid");

            Assert.Empty(payload.Ma);
        }

        // ...but a selection-less CLOSED market is a removal signal and must be published:
        // clients only delete a market when it arrives with a non-open MaStId (INC-21).
        [Fact]
        public void PrepareInternalOddsChangeMessage_MarketChange_ClosedMarketWithNoSelections_PublishesMarket()
        {
            var repository = CreateRepository();
            var message = MarketChangeMessage(compId: null);
            message.Ma[0].MaStId = (int)OE.Shared.Enums.MarketStatus.Inactive;
            message.Ma[0].Se = new List<SelectionMessage>();

            var payload = repository.PrepareInternalOddsChangeMessage(message, "cid");

            var market = Assert.Single(payload.Ma);
            Assert.Equal((int)OE.Shared.Enums.MarketStatus.Inactive, market.MaStId);
            Assert.Empty(market.Se);
        }
    }
}
