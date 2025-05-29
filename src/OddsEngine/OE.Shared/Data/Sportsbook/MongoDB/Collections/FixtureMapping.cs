using MongoDB.Bson.Serialization.Attributes;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    [BsonIgnoreExtraElements]
    public class FixtureMapping
    {
        [BsonId]
        public long FiId { get; set; }
        public string PrFiId { get; set; }
        public int ChTyId { get; set; }
    }
}
