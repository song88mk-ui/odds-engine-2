using OE.Shared.Messages.OutgoingAdapterMessages;

namespace OE.Job.Lookup.Repository
{
    public interface ISportsbookRepository
    {
        void ProcessOnOddsChangeMessage(OnMarketChangeMessage message);
    }
}
