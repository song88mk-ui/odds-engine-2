using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using OE.Shared.Messages.OutgoingReceiverMessages;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnResultChangeMessage : OutgoingAdapterMessageBase
    {
        public int ChTyId { get; set; }
        public List<ResultMarket> Ma { get; set; }
    }
}
