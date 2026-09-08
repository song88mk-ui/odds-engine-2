using MongoDB.Bson.Serialization.Attributes;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    [BsonIgnoreExtraElements]
    public class SelectionLookup
    {
        [BsonId]
        public string SeLoId { get; set; }
        public int SpId { get; set; }
        public string MaTeId { get; set; }
        public int FiTyId { get; set; }
        public string SeTeId { get; set; }
        public string SeTeNa { get; set; }
        public bool IsMap { get; set; }
    }

}
