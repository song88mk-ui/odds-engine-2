using MongoDB.Bson.Serialization.Attributes;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    public class BettingStatus
    {
        [BsonId]
        public string BeSo { get; set; }
        public bool IsBeSo { get; set; }
    }
}
