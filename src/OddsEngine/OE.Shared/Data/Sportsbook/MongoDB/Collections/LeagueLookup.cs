using MongoDB.Bson.Serialization.Attributes;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    [BsonIgnoreExtraElements]
    public class LeagueLookup
    {
        [BsonId]
        public string LeLoId { get; set; }
        public int SpId { get; set; }
        public int SegId { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public bool IsMap { get; set; }
    }

}
