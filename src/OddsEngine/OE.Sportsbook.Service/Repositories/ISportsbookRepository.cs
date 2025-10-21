using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public interface ISportsbookRepository
    {
        void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId);
        void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId);
        void ProcessOnSettlementMessage(IMessageBroker messageBroker, OnSettlementChangeMessage message, string correlationId);
    }
}
