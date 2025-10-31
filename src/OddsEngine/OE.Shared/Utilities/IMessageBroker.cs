using OE.Shared.Models;
using RabbitMQ.Client.Events;
using System;

namespace OE.Shared.Utilities
{
    public interface IMessageBroker
    {
        event EventHandler<BasicDeliverEventArgs> MessageReceived;
        void ExternalConnect();
        void ExternalConnect(MessageBrokerModel model);
        void Connect();
        void ConnectWithManualAck();
        void Acknowledge(BasicDeliverEventArgs e);
        void NAcknowledge(BasicDeliverEventArgs e);
        void ExternalPublish<T>(T payload, string messageType, string routingKey, string correlationId);
        void ExternalPublish(string payload, string messageType, string routingKey, string correlationId);
        void Publish<T>(T payload, string messageType, string routingKey, string correlationId);
        void Publish(string payload, string messageType, string routingKey, string correlationId);
        void Dispose();
    }
}
