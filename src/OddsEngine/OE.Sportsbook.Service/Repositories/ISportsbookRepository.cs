using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public interface ISportsbookRepository
    {
        void ProcessOnFixtureSetMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId);
        void ProcessOnMarketSetMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId);
    }
}
