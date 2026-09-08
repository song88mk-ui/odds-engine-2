using MongoDB.Bson.Serialization.Attributes;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    [BsonIgnoreExtraElements]
    public class SportLookup
    {
        [BsonId]
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public bool IsMap { get; set; }
    }

}
