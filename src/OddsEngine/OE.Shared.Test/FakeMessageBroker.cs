using OE.Shared.Models;
using OE.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Shared.Test
{
    // A single call, made through whichever IMessageBroker.Publish/CPublish/ExternalPublish
    // overload the SUT actually uses. CPublish/ExternalPublish are always no-ops on the fake
    // below (matching every service's real usage in these tests), so only Publish<T>/Publish
    // calls ever produce one of these.
    public sealed record PublishedMessage(object? Payload, string MessageType, string RoutingKey, string CorrelationId);

    // Canonical stand-in for IMessageBroker, used across every service's Worker tests. Tracks
    // every call the interface exposes (Connect/ConnectWithManualAck/Acknowledge/Dispose counts,
    // plus Publish/Publish<T> attempts and payloads) so each project's tests can assert on
    // whichever subset applies to it. ExternalConnect/NAcknowledge/CPublish/ExternalPublish stay
    // no-ops — no service under test relies on them doing anything.
    public sealed class FakeMessageBroker : IMessageBroker
    {
        public int ConnectCalls { get; private set; }
        public int ConnectWithManualAckCalls { get; private set; }
        public int AcknowledgeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int PublishAttempts { get; private set; }
        public bool ThrowOnDispose { get; set; }
        public bool ThrowOnPublish { get; set; }
        public bool IsConsuming { get; set; }

        public List<PublishedMessage> Published { get; } = new();

        public event EventHandler<BasicDeliverEventArgs>? MessageReceived;

        public void Connect() => ConnectCalls++;
        public void ConnectWithManualAck() => ConnectWithManualAckCalls++;
        public void ExternalConnect() { }
        public void ExternalConnect(MessageBrokerModel model) { }
        public void Acknowledge(BasicDeliverEventArgs e) => AcknowledgeCalls++;
        public void NAcknowledge(BasicDeliverEventArgs e) { }
        public void ExternalPublish<T>(T payload, string messageType, string routingKey, string correlationId) { }
        public void ExternalPublish(string payload, string messageType, string routingKey, string correlationId) { }

        public void Publish<T>(T payload, string messageType, string routingKey, string correlationId)
            => Record(payload, messageType, routingKey, correlationId);

        public void Publish(string payload, string messageType, string routingKey, string correlationId)
            => Record(payload, messageType, routingKey, correlationId);

        public void CPublish<T>(T payload, string messageType, string routingKey, string correlationId) { }
        public void CPublish(string payload, string messageType, string routingKey, string correlationId) { }

        public IModel BindQueue(string queueName, Dictionary<string, object> options) => throw new NotSupportedException();

        public void Dispose()
        {
            DisposeCalls++;
            if (ThrowOnDispose)
                throw new InvalidOperationException("dispose failed");
        }

        // Simulates an inbound RabbitMQ delivery for tests that exercise MessageReceived.
        public void Raise(BasicDeliverEventArgs e) => MessageReceived?.Invoke(this, e);

        private void Record(object? payload, string messageType, string routingKey, string correlationId)
        {
            PublishAttempts++;
            if (ThrowOnPublish)
                throw new InvalidOperationException("publish failed");
            Published.Add(new PublishedMessage(payload, messageType, routingKey, correlationId));
        }
    }
}
