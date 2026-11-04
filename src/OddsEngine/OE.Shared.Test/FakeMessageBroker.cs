using OE.Shared.Models;
using OE.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Shared.Test
{
    // A single call, made through whichever IMessageBroker.Publish/CPublish/ExternalPublish
    // overload the SUT actually uses. ExternalPublish is always a no-op on the fake below, so
    // only Publish<T>/Publish calls (into Published) and CPublish<T>/CPublish calls (into
    // CPublished, which OE.Job.BoostedOddsCap uses for its notification) ever produce one.
    public sealed record PublishedMessage(object? Payload, string MessageType, string RoutingKey, string CorrelationId);

    // Canonical stand-in for IMessageBroker, used across every service's Worker tests. Tracks
    // every call the interface exposes (Connect/ConnectWithManualAck/Acknowledge/Dispose counts,
    // plus Publish/Publish<T> attempts and payloads) so each project's tests can assert on
    // whichever subset applies to it. ExternalConnect/NAcknowledge/ExternalPublish stay no-ops, and
    // CPublish only records the call (it never throws) — no service under test relies on more.
    public sealed class FakeMessageBroker : IMessageBroker
    {
        public int ConnectCalls { get; private set; }
        public int ConnectWithManualAckCalls { get; private set; }
        public int AcknowledgeCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public int PublishAttempts { get; private set; }
        public bool ThrowOnDispose { get; set; }
        public bool ThrowOnPublish { get; set; }
        public bool ThrowOnCPublish { get; set; }
        public bool IsConsuming { get; set; }
        // Runs inside Connect(), before it returns: lets a test deliver a message while the consumer is starting.
        public Action? OnConnect { get; set; }

        public List<PublishedMessage> Published { get; } = new();

        // Calls made through CPublish (a channel of its own), kept apart from Published so a test can tell
        // which of the two paths the SUT used.
        public List<PublishedMessage> CPublished { get; } = new();

        public event EventHandler<BasicDeliverEventArgs>? MessageReceived;

        public void Connect()
        {
            ConnectCalls++;
            OnConnect?.Invoke();
        }
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

        public void CPublish<T>(T payload, string messageType, string routingKey, string correlationId)
        {
            if (ThrowOnCPublish)
                throw new InvalidOperationException("cpublish failed");

            CPublished.Add(new PublishedMessage(payload, messageType, routingKey, correlationId));
        }

        public void CPublish(string payload, string messageType, string routingKey, string correlationId)
            => CPublished.Add(new PublishedMessage(payload, messageType, routingKey, correlationId));

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
