using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using System;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnBetStopFixtureMessage : OutgoingAdapterMessageBase
    {
        public int SpId { get; set; }
        public int SegId { get; set; }
        public int LeId { get; set; }
        public int FiStId { get; set; }
        public int FiTyId { get; set; }
        public DateTime FiStDa { get; set; }
        public bool IsBetStop { get; set; }
        public bool SetActiveMarketsToInactive { get; set; }
    }
}
