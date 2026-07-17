using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    [BsonIgnoreExtraElements]
    public class Market
    {
        [BsonId]
        public string Id { get; set; }
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public string PrMaId { get; set; }
        public long FiId { get; set; }
        public long MaId { get; set; }
        public int SpId { get; set; }
        public int SegId { get; set; }
        public int LeId { get; set; }
        public string MaTeId { get; set; }
        public string MaNa { get; set; }
        public string Li { get; set; }
        public bool IsHa { get; set; } // IsHandicap — used by platform-final to recompose selection names (template + line)
        public int MaStId { get; set; }
        public string MaStNa { get; set; }
        public int MaSoOr { get; set; }
        public DateTime FiStDa { get; set; }
        public int FiStId { get; set; }
        public int CaStId { get; set; }
        public string CaStNa { get; set; }
        public string ClCo { get; set; }
        public string ClCoMa { get; set; }
        public bool IsBo { get; set; }
        public string ClBo { get; set; }
        public bool InPl { get; set; }
        public List<MarketTranslationMDB> MaNaTr { get; set; }
        // Vendor market-level CompetitorOne (2026-07 player-markets, GM-528): present = per-player
        // prop market for that player; joined against Fixture.Pl. Distinct from Selection.CompId
        // (competitor-specific outcome) and never to be confused with Selection.CoId (bet-builder
        // correlation id).
        public int? CompId { get; set; }
    }

    public class MarketTranslationMDB
    {
        [BsonId]
        public string LaCo { get; set; }
        public string MaNa { get; set; }
    }

    public class MarketStatusProjection
    {
        [BsonId]
        public long MaId { get; set; }
        public string PrFiId { get; set; }
    }
}
