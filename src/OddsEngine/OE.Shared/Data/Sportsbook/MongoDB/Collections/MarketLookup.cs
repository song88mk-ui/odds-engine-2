using MongoDB.Bson.Serialization.Attributes;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    [BsonIgnoreExtraElements]
    public class MarketLookup
    {
        [BsonId]
        public string MaLoId { get; set; }
        public int SpId { get; set; }
        public string MaTeId { get; set; }
        public string MaTeNa { get; set; }
        public int FiTyId { get; set; }
        public bool IsMap { get; set; }
    }

}
