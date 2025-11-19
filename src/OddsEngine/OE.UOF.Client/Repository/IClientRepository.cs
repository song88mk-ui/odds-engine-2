using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Utilities;

namespace OE.UOF.Client.Repository
{
    public interface IClientRepository
    {
        void ProcessFixtureChange(OnFixtureChangeMessage message, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketChange(OnMarketChangeMessage message, IMessageBroker messageBroker, string correlationId, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations);
        void ProcessBetStop(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessBetStopGlobal(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketSettlement(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketCancellation(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessHeartbeat(string payload, IMessageBroker messageBroker, string correlationId);
        void UpdateFixtureConfiguration(OnFixtureChangeMessage message, IMessageBroker messageBroker, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations);
    }
}
