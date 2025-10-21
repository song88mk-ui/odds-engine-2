using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using OE.Shared.Messages.OutgoingReceiverMessages;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnSettlementChangeMessage : OutgoingAdapterMessageBase
    {
        public List<ResultMarket> Ma { get; set; }
    }
}
