
namespace OE.UOF.Client.Models
{
    public class OFOddsChangeMessage
    {
        public OFOddsChangeMessage()
        {
            SportNameTranslations = new List<OFSportTranslation>();
            SegmentNameTranslations = new List<OFSegmentTranslation>();
            LeagueNameTranslations = new List<OFLeagueTranslation>();
            FixtureNameTranslations = new List<OFFixtureTranslation>();
            Markets = new List<OFMarketMessage>();

            LeagueReference = new List<ReferenceMessage>();
            HomeReference = new List<ReferenceMessage>();
            AwayReference = new List<ReferenceMessage>();
            PeriodScores = new List<PeriodScoreMessage>();
        }

        public long FixtureId { get; set; }
        public int SportId { get; set; }
        public string SportName { get; set; }
        public int SegmentId { get; set; }
        public string SegmentName { get; set; }
        public int LeagueId { get; set; }
        public string LeagueName { get; set; }
        public List<ReferenceMessage> LeagueReference { get; set; } // TBR
        public int LeagueRoundId { get; set; }
        public string LeagueRoundName { get; set; }
        public bool IsOfferedLive { get; set; }
        public string FixtureSpecifier { get; set; }
        public string FixtureProviderId { get; set; }
        public string FixtureName { get; set; }
        public string FixtureStartDate { get; set; }
        public int FixtureStatusId { get; set; }
        public string FixtureStatusName { get; set; }
        public int FixtureTypeId { get; set; }
        public string FixtureTypeName { get; set; }
        public string HomeScore { get; set; }
        public string AwayScore { get; set; }
        public string HomeId { get; set; }
        public List<ReferenceMessage> HomeReference { get; set; }
        public string AwayId { get; set; }
        public List<ReferenceMessage> AwayReference { get; set; }
        public int HomeRedCards { get; set; }
        public int AwayRedCards { get; set; }
        public int HomeYellowCards { get; set; }
        public int AwayYellowCards { get; set; }
        public int HomeCorners { get; set; }
        public int AwayCorners { get; set; }
        public int CurrentPeriod { get; set; }
        public int CurrentMinute { get; set; }
        public int CurrentSecond { get; set; }
        public int CurrentServer { get; set; }
        public List<PeriodScoreMessage> PeriodScores { get; set; }
        public long TimeStampSent { get; set; }
        public long TimeStampReceived { get; set; }
        public long TimeStampDispatched { get; set; }
        public long TimeStampProcessed { get; set; }
        public List<OFSportTranslation> SportNameTranslations { get; set; }
        public List<OFSegmentTranslation> SegmentNameTranslations { get; set; }
        public List<OFLeagueTranslation> LeagueNameTranslations { get; set; }
        public List<OFFixtureTranslation> FixtureNameTranslations { get; set; }
        public List<OFMarketMessage> Markets { get; set; }
        public string Raw { get; set; }
    }
}
