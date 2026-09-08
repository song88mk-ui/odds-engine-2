using MongoDB.Driver;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;

namespace OE.Sportsbook.Service
{
    public static class MongoIndexBootstrapper
    {
        public static async Task CreateIndexesAsync(IMongoDatabase db)
        {
            await CreateFixtureIndexes(db);
            await CreateMarketIndexes(db);
            await CreateSelectionIndexes(db);
        }

        private static async Task CreateFixtureIndexes(IMongoDatabase db)
        {
            var collection = db.GetCollection<Fixture>("Fixture");

            var indexes = new[]
            {
                new CreateIndexModel<Fixture>(
                    Builders<Fixture>.IndexKeys.Ascending(x => x.FiId),
                    new CreateIndexOptions { Name = "idx_fiid", Unique = true }
                ),
                // single compound index serving the Platform.Sportsbook.API fixture queries:
                // - cross-sport (sports tree, search): FiTyId (eq/$in) && FiStId (eq or < 2) && FiStDa range — exact prefix match
                // - per-sport (live/today/daily/outright/allleagues): same prefix, SpId applied as an in-index filter
                new CreateIndexModel<Fixture>(
                    Builders<Fixture>.IndexKeys.Ascending(x => x.FiTyId).Ascending(x => x.FiStId).Ascending(x => x.FiStDa).Ascending(x => x.SpId),
                    new CreateIndexOptions { Name = "idx_fityid_fistid_fistda_spid" }
                )
            };

            await collection.Indexes.CreateManyAsync(indexes);
        }

        private static async Task CreateMarketIndexes(IMongoDatabase db)
        {
            var collection = db.GetCollection<Market>("Market");

            var indexes = new[]
            {
                new CreateIndexModel<Market>(
                    Builders<Market>.IndexKeys.Ascending(x => x.MaId),
                    new CreateIndexOptions { Name = "idx_maid", Unique = true }
                ),
                new CreateIndexModel<Market>(
                    Builders<Market>.IndexKeys.Ascending(x => x.FiId).Ascending(x => x.MaStId).Ascending(x => x.MaTeId),
                    new CreateIndexOptions { Name = "idx_fiid_mastid_mateid"}
                )
            };

            await collection.Indexes.CreateManyAsync(indexes);
        }

        private static async Task CreateSelectionIndexes(IMongoDatabase db)
        {
            var collection = db.GetCollection<Selection>("Selection");

            var indexes = new[]
            {
                new CreateIndexModel<Selection>(
                    Builders<Selection>.IndexKeys.Ascending(x => x.SeId),
                    new CreateIndexOptions { Name = "idx_seid", Unique = true }
                ),
                new CreateIndexModel<Selection>(
                    Builders<Selection>.IndexKeys.Ascending(x => x.FiId).Ascending(x => x.MaStId).Ascending(x => x.MaTeId),
                    new CreateIndexOptions { Name = "idx_fiid_mastid_mateid" }
                ),
                new CreateIndexModel<Selection>(
                    Builders<Selection>.IndexKeys.Ascending(x => x.MaId),
                    new CreateIndexOptions { Name = "idx_maid" }
                )
            };

            await collection.Indexes.CreateManyAsync(indexes);
        }
    }
}
