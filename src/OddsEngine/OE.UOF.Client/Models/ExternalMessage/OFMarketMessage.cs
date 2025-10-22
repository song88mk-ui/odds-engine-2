
namespace OE.UOF.Client.Models
{
    public class OFMarketMessage
    {
        public OFMarketMessage()
        {
            Selections = new List<OFSelectionMessage>();
            MarketNameTranslations = new List<OFMarketTranslation>();
        }

        public string MarketTemplateId { get; set; }
        public string MarketName { get; set; }
        public string Line { get; set; }
        public int MarketStatusId { get; set; }
        public string MarketStatusName { get; set; }
        public string MarketSpecifier { get; set; }
        public long? NextBetStop { get; set; }
        public long? StartTime { get; set; }
        public long? EndTime { get; set; }
        public List<OFMarketTranslation> MarketNameTranslations { get; set; }
        public List<OFSelectionMessage> Selections { get; set; }
    }
}
