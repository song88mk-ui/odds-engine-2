using RabbitMQ.Client.Events;
using System;
using System.Threading.Tasks;

namespace OE.Shared.Utilities
{
    public interface IMessageBroker
    {
        event EventHandler<BasicDeliverEventArgs> MessageReceived;
        Task Connect();
        Task ExternalConnect();
        Task ConnectWithManualAck();
        Task Acknowledge(BasicDeliverEventArgs e);
        Task NAcknowledge(BasicDeliverEventArgs e);
        Task ExternalPublish<T>(T payload, string messageType, string routingKey, string correlationId);
        Task ExternalPublish(string payload, string messageType, string routingKey, string correlationId);
        Task Publish<T>(T payload, string messageType, string routingKey, string correlationId);
        Task Publish(string payload, string messageType, string routingKey, string correlationId);
        void Dispose();
    }
}
