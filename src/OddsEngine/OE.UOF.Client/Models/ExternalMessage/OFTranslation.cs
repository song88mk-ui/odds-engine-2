namespace OE.UOF.Client.Models
{
    public class OFTranslation
    {
        public string LanguageCode { get; set; }
    }

    public class OFSportTranslation : OFTranslation
    {
        public string SportName { get; set; }
    }

    public class OFSegmentTranslation : OFTranslation
    {
        public string SegmentName { get; set; }
    }

    public class OFLeagueTranslation : OFTranslation
    {
        public string LeagueName { get; set; }
    }

    public class OFFixtureTranslation : OFTranslation
    {
        public string FixtureName { get; set; }
    }

    public class OFMarketTranslation : OFTranslation
    {
        public string MarketName { get; set; }
    }

    public class OFSelectionTranslation : OFTranslation
    {
        public string SelectionName { get; set; }
    }
}
