using MongoDB.Bson.Serialization.Attributes;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    public class MarketMapping
    {
        [BsonId]
        public long MaId { get; set; }

        public int ChTyId { get; set; }

        public string PrFiId { get; set; }

        public string MaTeId { get; set; }

        public string MaSp { get; set; }
    }
}
