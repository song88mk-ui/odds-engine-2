using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Models;
using OE.Shared.Utilities;

namespace OE.UOF.Client.Repository
{
    public interface IClientRepository
    {
        void ProcessFixtureChange(OnInternalOddsChangeMessage message, IMessageBroker messageBroker, string correlationId, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations);
        void ProcessBetStop(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessBetStopGlobal(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketSettlement(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessMarketCancellation(string payload, IMessageBroker messageBroker, string correlationId);
        void ProcessHeartbeat(string payload, IMessageBroker messageBroker, string correlationId);
        void UpdateFixtureConfiguration(OnInternalOddsChangeMessage message, IMessageBroker messageBroker, List<ClientLiveFixtureModel> fixtureConfigurations, List<ClientConfigurationModel> sportConfigurations);
    }
}
