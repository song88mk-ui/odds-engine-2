using System.Collections.Generic;
using System;
using OE.Shared.Enums;

namespace OE.Shared.Messages.OutgoingSportsbookMessages
{
    public class OnInternalOddsChangeMessage
    {
        public int SportId { get; set; }
        public string SportName { get; set; }
        public int SegmentId { get; set; }
        public string SegmentName { get; set; }
        public int LeagueId { get; set; }
        public string LeagueName { get; set; }
        public long FixtureId { get; set; }
        public string ProviderFixtureId { get; set; }
        public string FixtureName { get; set; }
        public DateTime FixtureStartDate { get; set; }
        public int FixtureStatusId { get; set; }
        public string FixtureStatusName { get; set; }
        public string Score { get; set; }
        public string FixtureSpecifier { get; set; }
        public bool IsOfferedLive { get; set; }
        public int HomeRedCards { get; set; }
        public int AwayRedCards { get; set; }
        public int CurrentPeriodId { get; set; }
        public string CurrentPeriod { get; set; }
        public int CurrentMinute { get; set; }
        public int FixtureTypeId { get; set; }
        public string FixtureTypeName { get; set; }
        public int TotalTime { get; set; }
        public string Favorite { get; set; }
        public int SortOrder { get; set; }
        public string ClientCodes { get; set; }
        public string CorrelationId { get; set; }
        public bool IsSuspended { get; set; }
        public bool? IsBoosted { get; set; }
        //public List<SportTranslation> SportTranslations { get; set; }
        //public List<SegmentTranslation> SegmentTranslations { get; set; }
        //public List<LeagueTranslation> LeagueTranslations { get; set; }
        //public List<FixtureTranslation> FixtureTranslations { get; set; }
        public List<InternalMarketMessage> Markets { get; set; } = new List<InternalMarketMessage>();
        public string HomeId { get; set; }
        public string AwayId { get; set; }
        //public List<InternalPeriodScore> PeriodScores { get; set; }
        public int CurrentServer { get; set; }
        public bool IsInsert { get; set; }
    }

    public class InternalMarketMessage
    {
        public string ProviderFixtureId { get; set; }
        public long MarketId { get; set; }
        public string MarketTemplateId { get; set; }
        public string ProviderMarketId { get; set; }
        public string Line { get; set; }
        public int MarketStatusId { get; set; }
        public int MarketStatusName { get; set; }
        public int CashoutStatusId { get; set; }
        public int CashoutStatusName { get; set; }
        public string MarketSpecifier { get; set; }
        public string MarketName { get; set; }
        public int SortOrder { get; set; }
        public bool IsInsert { get; set; }
        public string ClientCodes { get; set; }
        public string ClientCodeMargin { get; set; }
        //public List<MarketTranslation> MarketTranslations { get; set; }
        public List<InternalSelectionMessage> Selections { get; set; } = new List<InternalSelectionMessage>();
    }

    public class InternalSelectionMessage
    {
        public string ProviderFixtureId { get; set; }
        public string MarketTemplateId { get; set; }
        public string SelectionTemplateId { get; set; }
        public string MarketSpecifier { get; set; }
        public long SelectionId { get; set; }
        public decimal Odds { get; set; }
        public double Probability { get; set; }
        public double WonProbability { get; set; }
        public double LostProbability { get; set; }
        public double VoidProbability { get; set; }
        public double HalfWonProbability { get; set; }
        public double HalfLostProbability { get; set; }
        public bool IsActive { get; set; }
        public string SelectionSpecifier { get; set; }
        public string SelectionName { get; set; }
        public int SortOrder { get; set; }
        public bool IsInsert { get; set; }
        public List<InternalClientOdds> ClientOdds { get; set; }
        //public List<SelectionTranslation> SelectionTranslations { get; set; }
    }

    public class InternalClientOdds
    {
        public string ClientCode { get; set; }
        public decimal DecimalOdds { get; set; }
        public decimal MalaysianOdds { get; set; }
        public decimal HongkongOdds { get; set; }
        public decimal IndonesianOdds { get; set; }
        public string SelectionTemplateId { get; set; }
        public long SelectionId { get; set; }
    }
}
