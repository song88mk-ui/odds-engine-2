using OE.Adapter.Service.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using OE.Shared.Utilities;
using System.Diagnostics;

namespace OE.Adapter.Service.Repositories
{
    public class TradingRepository : ITradingRepository
    {
        #region Fields

        private readonly IHelper _helper;
        private const ChannelType _bgProvider = ChannelType.BG;
        private readonly ActivitySource _activitySource;

        #endregion

        #region Ctor

        public TradingRepository(IHelper helper, Instrumentation instrumentation)
        {
            _helper = helper;
            _activitySource = instrumentation.ActivitySource;
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_FIXTURE_CHANGE, correlationId);
        }

        public void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_MARKET_CHANGE, correlationId);
        }

        public void ProcessOnMarketSettlementMessage(IMessageBroker messageBroker, OnMarketSettlementMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_MARKET_SETTLEMENT, correlationId);
        }

        public void ProcessOnBetStopGlobalMessage(IMessageBroker messageBroker, OnBetStopGlobalMessage message, string correlationId)
        {
            using (var activity = _activitySource.StartActivity("publish.message"))
            {
                var instanceName = "10";
                activity?.SetTag("sb.engine.service.id", instanceName);

                messageBroker.Publish(message, MessageType.ON_BET_STOP_GLOBAL, $"r.oe.adapter.{MessageType.ON_BET_STOP_GLOBAL.ToLower()}.{instanceName}", correlationId);
            }
        }

        public void ProcessMarketCancellation(IMessageBroker messageBroker, OnMarketCancellationMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_MARKET_CANCEL, correlationId);
        }
        public void ProcessOnBetStopFixture(IMessageBroker messageBroker, OnBetStopFixtureMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_BET_STOP_FIXTURE, correlationId);
        }

        #endregion

        #region Utilities

        private FixtureStatus GetFixtureStatus(int fixtureStatusId)
        {
            return (FixtureStatus)fixtureStatusId;
        }

        private FixtureType GetFixtureType(int fixtureTypeId)
        {
            return (FixtureType)fixtureTypeId;
        }

        private MarketStatus GetMarketStatus(int marketStatusId)
        {
            return (MarketStatus)marketStatusId;
        }

        private CashoutStatus GetCashoutStatus(int cashoutStatusId)
        {
            return (CashoutStatus)cashoutStatusId;
        }

        private void PublishMessage<T>(IMessageBroker messageBroker, T payload, string messageType, string correlationId) where T : OutgoingAdapterMessageBase
        {
            using (var activity = _activitySource.StartActivity("publish.message"))
            {
                var instanceName = _helper.GetInstanceName(payload.PrFiId);
                activity?.SetTag("sb.engine.service.id", instanceName);

                messageBroker.Publish(payload, messageType, $"r.oe.adapter.{messageType.ToLower()}.{instanceName}", correlationId);
            }
        }


        #endregion
    }
}
