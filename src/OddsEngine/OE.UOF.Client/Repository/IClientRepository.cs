using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Utilities;
using OE.UOF.Client.Models;

namespace OE.UOF.Client.Repository
{
    public interface IClientRepository
    {
        void ProcessFixtureChange(OnFixtureChangeMessage message, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketChange(OnMarketChangeMessage message, IMessageBroker messageBroker, string correlationId, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations);
        void ProcessBetStopGlobal(OnBetStopGlobalMessage message, IMessageBroker messageBroker, string correlationId);
        void ProcessBetStop(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketSettlement(OnMarketSettlementMessage message, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketCancellation(OnMarketCancellationMessage message, IMessageBroker messageBroker, string correlationId);
        void ProcessHeartbeat(OFHeartbeatMessage message, IMessageBroker messageBroker, string correlationId);
        void UpdateFixtureConfiguration(OnFixtureChangeMessage message, IMessageBroker messageBroker, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations);
    }
}
