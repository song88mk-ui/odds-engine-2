using OE.Shared.Messages.OutgoingAdapterMessages.Base;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnBetStopFixtureMessage : OutgoingAdapterMessageBase
    {
        public bool IsBetStop { get; set; }
        public bool SetActiveMarketsToInactive { get; set; }
    }
}
