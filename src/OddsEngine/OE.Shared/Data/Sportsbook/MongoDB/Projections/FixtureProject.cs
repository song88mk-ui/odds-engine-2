using MongoDB.Bson.Serialization.Attributes;
using System;

namespace OE.Shared.Data.Sportsbook.MongoDB.Projections
{
    public class FixtureProject
    {
        [BsonId]
        public long FiId { get; set; }
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public int SpId { get; set; }
        public int SegId { get; set; }
        public int LeId { get; set; }
        public DateTime FiStDa { get; set; }
        public int FiStId { get; set; }
    }
}
