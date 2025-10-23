using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingReceiverMessages
{
    public class OnResultSetMessage
    {
        public string PrFiId { get; set; }
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public int SegId { get; set; }
        public string SegNa { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public List<ResultMarket> Ma { get; set; }
    }

    public class ResultMarket
    {
        public string PrMaId { get; set; } //MarketId
        public int MaStId { get; set; } //MarketStatusId
        public List<ResultSelection> Se { get; set; }
    }

    public class ResultSelection
    {
        public string PrMaId { get; set; } //MarketId
        public string PrSeId { get; set; } //SelectionId
        public int SeStId { get; set; } // Selection Status Id
    }
}
