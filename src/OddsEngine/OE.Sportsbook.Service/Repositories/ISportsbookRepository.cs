using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public interface ISportsbookRepository
    {
        void ProcessOnFixtureSetMessage(IMessageBroker messageBroker, OnFixtureSetMessage payload, string correlationId);
        void ProcessOnMarketSetMessage(IMessageBroker messageBroker, OnMarketSetMessage payload, string correlationId);
    }
}
