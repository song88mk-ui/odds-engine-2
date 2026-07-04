using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;

namespace OE.Adapter.Service.Repositories
{
    public interface ITradingRepository
    {
        void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId);
        void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId);
        void ProcessOnMarketSettlementMessage(IMessageBroker messageBroker, OnMarketSettlementMessage message, string correlationId);
        void ProcessOnBetSettlementMessage(IMessageBroker messageBroker, OnBetSettlementMessage message, string correlationId);
        void ProcessOnBetStopGlobalMessage(IMessageBroker messageBroker, OnBetStopGlobalMessage message, string correlationId);
        void ProcessMarketCancellation(IMessageBroker messageBroker, OnMarketCancellationMessage message, string correlationId);
        void ProcessOnBetStopFixture(IMessageBroker messageBroker, OnBetStopFixtureMessage message, string correlationId);
        void ProcessOnTranslationChangeMessage(IMessageBroker messageBroker, OnTranslationChangeMessage message, string correlationId);
    }
}
