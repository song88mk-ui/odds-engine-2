using OE.Shared.Models;
using OE.Shared.Utilities;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Adapter.Service.Tests
{
    // Captures Publish calls so tests can assert routing keys, message types, and payloads without
    // a broker. Everything else is a no-op.
    public class FakeMessageBroker : IMessageBroker
    {
#pragma warning disable 67
        public event EventHandler<BasicDeliverEventArgs>? MessageReceived;
#pragma warning restore 67

        public List<(object Payload, string MessageType, string RoutingKey, string CorrelationId)> Published { get; } = new();

        public void Publish<T>(T payload, string messageType, string routingKey, string correlationId)
            => Published.Add((payload!, messageType, routingKey, correlationId));

        public void Publish(string payload, string messageType, string routingKey, string correlationId)
            => Published.Add((payload, messageType, routingKey, correlationId));

        public void CPublish<T>(T payload, string messageType, string routingKey, string correlationId)
            => Published.Add((payload!, messageType, routingKey, correlationId));

        public void CPublish(string payload, string messageType, string routingKey, string correlationId)
            => Published.Add((payload, messageType, routingKey, correlationId));

        public void ExternalPublish<T>(T payload, string messageType, string routingKey, string correlationId)
            => Published.Add((payload!, messageType, routingKey, correlationId));

        public void ExternalPublish(string payload, string messageType, string routingKey, string correlationId)
            => Published.Add((payload, messageType, routingKey, correlationId));

        public void Connect() { }

        public void ConnectWithManualAck() { }

        public void ExternalConnect() { }

        public void ExternalConnect(MessageBrokerModel model) { }

        public void Acknowledge(BasicDeliverEventArgs e) { }

        public void NAcknowledge(BasicDeliverEventArgs e) { }

        public IModel BindQueue(string queueName, Dictionary<string, object> options) => null!;

        public void Dispose() { }
    }
}
