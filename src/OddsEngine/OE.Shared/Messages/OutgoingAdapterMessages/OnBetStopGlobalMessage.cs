using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using System;
using System.Collections.Generic;
using System.Text;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnBetStopGlobalMessage : OutgoingAdapterMessageBase
    {
        public bool IsBetStop { get; set; }
    }
}
