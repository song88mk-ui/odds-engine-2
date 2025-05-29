using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Messages.OutgoingReceiverMessages.Base;
using OE.Shared.Utilities;

namespace OE.Adapter.Service.Repositories
{
    public class TradingRepository : ITradingRepository
    {
        #region Fields

        private readonly IHelper _helper;

        #endregion

        #region Ctor

        public TradingRepository(IHelper helper)
        {
            _helper = helper;
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureSetMessage(IMessageBroker messageBroker, OnFixtureSetMessage payload, string correlationId)
        {
            PublishMessage(messageBroker, payload, MessageType.ON_FIXTURE_SET, correlationId);
        }

        public void ProcessOnMarketSetMessage(IMessageBroker messageBroker, OnMarketSetMessage payload, string correlationId)
        {
            PublishMessage(messageBroker, payload, MessageType.ON_MARKET_SET, correlationId);
        }

        #endregion

        #region Utilities

        private void PublishMessage<T>(IMessageBroker messageBroker, T payload, string messageType, string correlationId) where T : OutgoingReceiverMessageBase
        {
            var instanceName = _helper.GetInstanceName(payload.FiId.ToString());

            messageBroker.Publish(payload, messageType, $"r.oe.adapter.{messageType.ToLower()}.{instanceName}", correlationId);
        }

        #endregion
    }
}
