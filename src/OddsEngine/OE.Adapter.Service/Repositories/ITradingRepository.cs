using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Utilities;

namespace OE.Adapter.Service.Repositories
{
    public interface ITradingRepository
    {
        void ProcessOnFixtureSetMessage(IMessageBroker messageBroker, OnFixtureSetMessage message, string correlationId);
        void ProcessOnMarketSetMessage(IMessageBroker messageBroker, OnMarketSetMessage message, string correlationId);
    }
}
