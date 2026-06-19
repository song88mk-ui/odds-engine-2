using OE.Shared.Enums;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnBetSettlementMessage
    {
        public int SportId { get; set; }
        public string ProviderFixtureId { get; set; }
        public string FixtureSpecifier { get; set; }
        public string HomeScore { get; set; }
        public string AwayScore { get; set; }
        public List<SelectionSettlementMessage> Selections { get; set; }
    }

    public class SelectionSettlementMessage
    {
        public string MarketTemplateId { get; set; }
        public string SelectionTemplateId { get; set; }
        public string MarketSpecifier { get; set; }
        public string SelectionSpecifier { get; set; }
        public bool Result { get; set; }
        public VoidFactor? VoidFactor { get; set; }
        public decimal DeadHeatFactor { get; set; }
    }
}
