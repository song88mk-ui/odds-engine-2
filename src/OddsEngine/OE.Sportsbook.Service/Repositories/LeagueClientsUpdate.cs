using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;

namespace OE.Sportsbook.Service.Repositories
{
    // The Mongo queries of OnLeagueClientsChange: stamp the clients allowed to see a league (ClCo) onto its stored
    // fixtures and markets. Only fixtures carry the league ids that are indexed with it, and neither collection has an
    // index on them, so the fixtures of the league are found first (a small collection) and the markets are then
    // reached by fixture id, an index the markets already have (idx_fiid_mastid_mateid). That avoids a scan of every
    // market and a new index on a production collection.
    internal static class LeagueClientsUpdate
    {
        // fixtures per update, so that no single filter or write grows with the size of the league
        public const int BatchSize = 500;

        public static FilterDefinition<Fixture> FixturesOfLeague(OnLeagueClientsChangeMessage message)
        {
            return Builders<Fixture>.Filter.Where(f => f.SpId == message.SportId && f.SegId == message.SegmentId && f.LeId == message.LeagueId);
        }

        public static FilterDefinition<Fixture> FixtureFilter(IEnumerable<long> fixtureIds)
        {
            return Builders<Fixture>.Filter.In(f => f.FiId, fixtureIds);
        }

        public static FilterDefinition<Market> MarketFilter(IEnumerable<long> fixtureIds)
        {
            return Builders<Market>.Filter.In(m => m.FiId, fixtureIds);
        }

        public static UpdateDefinition<Fixture> FixtureUpdate(string clientCodes)
        {
            return Builders<Fixture>.Update.Set(f => f.ClCo, clientCodes);
        }

        public static UpdateDefinition<Market> MarketUpdate(string clientCodes)
        {
            return Builders<Market>.Update.Set(m => m.ClCo, clientCodes);
        }
    }
}
