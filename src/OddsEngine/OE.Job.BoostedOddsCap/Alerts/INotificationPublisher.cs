using OE.Shared.Utilities;

namespace OE.Job.BoostedOddsCap.Alerts
{
    public interface INotificationPublisher
    {
        void Publish(string payloadJson);
    }

    // Publishes on its own channel (CPublish) rather than the broker's shared one. The shared channel is the one
    // the consumer acknowledges on, and RabbitMQ.Client channels are not safe to use from two threads at once;
    // alerts are rare, so the cost of a channel per publish is irrelevant here.
    public class BrokerNotificationPublisher : INotificationPublisher
    {
        private readonly IMessageBroker _messageBroker;
        private readonly NotificationOptions _options;

        public BrokerNotificationPublisher(IMessageBroker messageBroker, NotificationOptions options)
        {
            _messageBroker = messageBroker;
            _options = options;
        }

        public void Publish(string payloadJson)
        {
            _messageBroker.CPublish(payloadJson, _options.MessageType, _options.RoutingKey, Guid.NewGuid().ToString());
        }
    }
}
