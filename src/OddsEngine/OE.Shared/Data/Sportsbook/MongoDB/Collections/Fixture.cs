using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace OE.Shared.Data.Sportsbook.MongoDB.Collections
{
    [BsonIgnoreExtraElements]
    public class Fixture
    {
        [BsonId]
        public string Id { get; set; }
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public long FiId { get; set; }
        public string ClCo { get; set; }
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public int SegId { get; set; }
        public string SegNa { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public string FiNa { get; set; }
        public DateTime FiStDa { get; set; }
        public int FiStId { get; set; }
        public string FiStNa { get; set; }
        public int FiTyId { get; set; }
        public string FiTyNa { get; set; }
        public string Sc { get; set; }
        public bool IsOfLi { get; set; }
        public int HoReCa { get; set; }
        public int AwReCa { get; set; }
        public int CuPe { get; set; }
        public int CuMi { get; set; }
        public string FiSp { get; set; }
        public bool IsHoFa { get; set; }
        public int FiSoOr { get; set; }
        public int ToTi { get; set; }
        public string LiId { get; set; }
        public string StUr { get; set; }
        public bool IsSu { get; set; }
        public string ClMa { get; set; }
        public string GeAl { get; set; }
        public bool IsBo { get; set; }
        public string ClBo { get; set; }
        public List<SportTranslationMDB> SpNaTr { get; set; }
        public List<SegmentTranslationMDB> SegNaTr { get; set; }
        public List<LeagueTranslationMDB> LeNaTr { get; set; }
        public List<FixtureTranslationMDB> FiNaTr { get; set; }
        public string HoId { get; set; }
        public string HoNa { get; set; } // Home team name — source text for translation lookup
        public int HoBeId { get; set; }
        public string AwId { get; set; }
        public string AwNa { get; set; } // Away team name — source text for translation lookup
        public int AwBeId { get; set; }
        // Team-name translations (TranslationCache2 keys "Te:{HoId}" / "Te:{AwId}"), added with the
        // 2026-07 translation-flow redesign alongside the existing *NaTr lists above.
        public List<TeamTranslationMDB> HoNaTr { get; set; }
        public List<TeamTranslationMDB> AwNaTr { get; set; }
        public int LeBeId { get; set; }
        public List<PeriodScoreMDB> PeSc { get; set; }
        public int CuSe { get; set; }
        // Player roster (2026-07 player-markets, GM-528): vendor CompetitorType=Person entries
        // flattened by the adapter (OnFixtureChangeMessage.Pl); reference data joined against
        // Market.CompId. Names only — translations are never stored per player: the player's
        // Pl:{PlId} translation feeds the COMPOSED market name (Market.MaNaTr, see
        // PlayerMarketNameComposer) instead.
        public List<PlayerMDB> Pl { get; set; }
    }

    public class SportTranslationMDB
    {
        [BsonId]
        public string LaCo { get; set; }
        public string SpNa { get; set; }
    }

    public class SegmentTranslationMDB
    {
        [BsonId]
        public string LaCo { get; set; }
        public string SegNa { get; set; }
    }

    public class LeagueTranslationMDB
    {
        [BsonId]
        public string LaCo { get; set; }
        public string LeNa { get; set; }
    }

    public class FixtureTranslationMDB
    {
        [BsonId]
        public string LaCo { get; set; }
        public string FiNa { get; set; }
    }

    public class TeamTranslationMDB
    {
        [BsonId]
        public string LaCo { get; set; }
        public string TeNa { get; set; }
    }

    public class PlayerMDB
    {
        [BsonId]
        public int PlId { get; set; }
        public string PlNa { get; set; }
    }

    public class PeriodScoreMDB
    {
        [BsonId]
        public string MaStCo { get; set; }
        public int Ho { get; set; }
        public int Aw { get; set; }
        public string PeTy { get; set; }
    }
}
