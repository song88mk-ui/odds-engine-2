using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;

namespace OE.Sportsbook.Service.Repositories
{
    public interface ISportsbookRepository
    {
        void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId);
        void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId);
        void ProcessOnMarketSettlementMessage(IMessageBroker messageBroker, OnMarketSettlementMessage message, string correlationId);
        void ProcessOnBetStopGlobalMessage(IMessageBroker messageBroker, OnBetStopGlobalMessage message, string correlationId);
        void ProcessOnBetStopFixtureMessage(IMessageBroker messageBroker, OnBetStopFixtureMessage message, string correlationId);
        void ProcessOnMarketCancellationMessage(IMessageBroker messageBroker, OnMarketCancellationMessage message, string correlationId);
        void ProcessCacheInvalidationMessage(OnDeleteCacheMessage message);
        Task SetInitialData();
    }
}
