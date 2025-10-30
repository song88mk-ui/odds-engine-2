using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnMarketSettlementMessage : OutgoingAdapterMessageBase
    {
        public List<MarketResult> Ma { get; set; }
    }

    public class MarketResult
    {
        public long MaId { get; set; }
        public string PrMaId { get; set; } //MarketId
        public int MaStId { get; set; } //MarketStatusId
        public List<SelectionResult> Se { get; set; }
    }

    public class SelectionResult
    {
        public long SeId { get; set; }
        public string PrMaId { get; set; } //MarketId
        public string PrSeId { get; set; } //SelectionId
        public int SeStId { get; set; } // Selection Status Id
    }
}
