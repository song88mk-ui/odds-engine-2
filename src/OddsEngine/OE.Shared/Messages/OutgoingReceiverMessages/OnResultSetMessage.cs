using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingReceiverMessages
{
    public class OnResultSetMessage
    {
        public int FiId { get; set; }
        public int ChTyId { get; set; }
        public List<ResultMarket> Ma { get; set; }
    }

    public class ResultMarket
    {
        public int MaId { get; set; } //MarketId
        public int MaStId { get; set; } //MarketStatusId
        public List<ResultSelection> Se { get; set; }
    }

    public class ResultSelection
    {
        public int MaId { get; set; } //MarketId
        public int SeId { get; set; } //SelectionId
        public int SeStId { get; set; } // Selection Status Id
    }
}
