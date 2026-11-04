using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Sportsbook.Service.Repositories;

namespace OE.Sportsbook.Service.Tests
{
    // The Mongo queries of OnLeagueClientsChange (BO-145), rendered the way the driver sends them. The find and the
    // writes against a real Mongo are not covered here (this repo has no Mongo fixture) and are checked on DEV.
    public class LeagueClientsUpdateTests
    {
        private static BsonDocument Render<T>(UpdateDefinition<T> update) =>
            update.Render(new RenderArgs<T>(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

        private static BsonDocument Render<T>(FilterDefinition<T> filter) =>
            filter.Render(new RenderArgs<T>(BsonSerializer.SerializerRegistry.GetSerializer<T>(), BsonSerializer.SerializerRegistry));

        // property order and spacing of the rendered JSON are the driver's business, the names and values are ours
        private static string Compact(BsonDocument document) => document.ToJson().Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);

        [Fact]
        public void FixturesOfLeague_AreFoundByTheSportSegmentAndLeagueIds()
        {
            var filter = Render(LeagueClientsUpdate.FixturesOfLeague(new OnLeagueClientsChangeMessage { SportId = 1, SegmentId = 2, LeagueId = 3, ClientCodes = "G01" }));

            var json = Compact(filter);
            Assert.Contains("\"SpId\":1", json);
            Assert.Contains("\"SegId\":2", json);
            Assert.Contains("\"LeId\":3", json);
        }

        [Fact]
        public void TheFixtureWrite_IsReachedByFixtureId()
        {
            var filter = Render(LeagueClientsUpdate.FixtureFilter(new long[] { 11, 12 }));

            Assert.Equal(new long[] { 11, 12 }, filter["FiId"]["$in"].AsBsonArray.Select(v => v.AsInt64));
        }

        [Fact]
        public void TheMarketWrite_IsReachedByFixtureId_NotByLeague()
        {
            var filter = Render(LeagueClientsUpdate.MarketFilter(new long[] { 11, 12 }));

            Assert.Equal(new long[] { 11, 12 }, filter["FiId"]["$in"].AsBsonArray.Select(v => v.AsInt64));
            Assert.False(filter.Contains("LeId")); // no index on the league ids, so a league filter would scan every market
        }

        [Fact]
        public void TheUpdates_SetOnlyTheClientList()
        {
            var fixtureSet = Render(LeagueClientsUpdate.FixtureUpdate("G01|TSO"))["$set"].AsBsonDocument;
            var marketSet = Render(LeagueClientsUpdate.MarketUpdate("G01|TSO"))["$set"].AsBsonDocument;

            Assert.Equal(new[] { "ClCo" }, fixtureSet.Names);
            Assert.Equal("G01|TSO", fixtureSet["ClCo"].AsString);
            Assert.Equal(new[] { "ClCo" }, marketSet.Names);
            Assert.Equal("G01|TSO", marketSet["ClCo"].AsString);
        }

        [Fact]
        public void AnEmptyList_IsWrittenAsEmpty_NotRemovedOrNull()
        {
            Assert.Equal(string.Empty, Render(LeagueClientsUpdate.FixtureUpdate(string.Empty))["$set"]["ClCo"].AsString);
            Assert.Equal(string.Empty, Render(LeagueClientsUpdate.MarketUpdate(string.Empty))["$set"]["ClCo"].AsString);
        }

        [Fact]
        public void ABatch_IsBoundedSoNoSingleWriteGrowsWithTheLeague()
        {
            Assert.InRange(LeagueClientsUpdate.BatchSize, 1, 1000);
        }
    }
}
