using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OE.Shared.Test
{
    // Minimal IModel stand-in that records what MessageBroker publishes and acknowledges, so the
    // publish path can be asserted without a live RabbitMQ connection. Only the members the broker
    // actually calls are implemented (CreateBasicProperties, BasicPublish, BasicAck, BasicNack);
    // everything else throws, so a future caller reaching for an unimplemented member fails loudly
    // rather than silently passing.
    public class FakeModel : IModel
    {
        public record PublishedMessage(
            string Exchange,
            string RoutingKey,
            bool Mandatory,
            IBasicProperties Properties,
            byte[] Body);

        public record AckCall(ulong DeliveryTag, bool Multiple);

        public record NackCall(ulong DeliveryTag, bool Multiple, bool Requeue);

        public List<PublishedMessage> Published { get; } = new();

        public List<AckCall> Acks { get; } = new();

        public List<NackCall> Nacks { get; } = new();

        public bool Disposed { get; private set; }

        public IBasicProperties CreateBasicProperties() => new RabbitMQ.Client.Framing.BasicProperties();

        public void BasicPublish(string exchange, string routingKey, bool mandatory, IBasicProperties basicProperties, byte[] body)
            => Published.Add(new PublishedMessage(exchange, routingKey, mandatory, basicProperties, body));

        public void BasicAck(ulong deliveryTag, bool multiple) => Acks.Add(new AckCall(deliveryTag, multiple));

        public void BasicNack(ulong deliveryTag, bool multiple, bool requeue)
            => Nacks.Add(new NackCall(deliveryTag, multiple, requeue));

        public void Dispose() => Disposed = true;

        public int ChannelNumber => 1;

        public ShutdownEventArgs CloseReason => null!;

        public IBasicConsumer DefaultConsumer { get; set; } = null!;

        public bool IsClosed => false;

        public bool IsOpen => true;

        public ulong NextPublishSeqNo => 0;

        public TimeSpan ContinuationTimeout { get; set; }

#pragma warning disable CS0067 // the broker never subscribes to these; they exist to satisfy IModel
        public event EventHandler<BasicAckEventArgs>? BasicAcks;
        public event EventHandler<BasicNackEventArgs>? BasicNacks;
        public event EventHandler<EventArgs>? BasicRecoverOk;
        public event EventHandler<BasicReturnEventArgs>? BasicReturn;
        public event EventHandler<CallbackExceptionEventArgs>? CallbackException;
        public event EventHandler<FlowControlEventArgs>? FlowControl;
        public event EventHandler<ShutdownEventArgs>? ModelShutdown;
#pragma warning restore CS0067

        public void Abort() => throw new NotImplementedException();

        public void Abort(ushort replyCode, string replyText) => throw new NotImplementedException();

        public void BasicCancel(string consumerTag) => throw new NotImplementedException();

        public string BasicConsume(string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive, IDictionary<string, object> arguments, IBasicConsumer consumer)
            => throw new NotImplementedException();

        public BasicGetResult BasicGet(string queue, bool autoAck) => throw new NotImplementedException();

        public void BasicQos(uint prefetchSize, ushort prefetchCount, bool global) => throw new NotImplementedException();

        public void BasicRecover(bool requeue) => throw new NotImplementedException();

        public void BasicRecoverAsync(bool requeue) => throw new NotImplementedException();

        public void BasicReject(ulong deliveryTag, bool requeue) => throw new NotImplementedException();

        public void Close() => throw new NotImplementedException();

        public void Close(ushort replyCode, string replyText) => throw new NotImplementedException();

        public void ConfirmSelect() => throw new NotImplementedException();

        public IBasicPublishBatch CreateBasicPublishBatch() => throw new NotImplementedException();

        public void ExchangeBind(string destination, string source, string routingKey, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void ExchangeBindNoWait(string destination, string source, string routingKey, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void ExchangeDeclare(string exchange, string type, bool durable, bool autoDelete, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void ExchangeDeclareNoWait(string exchange, string type, bool durable, bool autoDelete, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void ExchangeDeclarePassive(string exchange) => throw new NotImplementedException();

        public void ExchangeDelete(string exchange, bool ifUnused) => throw new NotImplementedException();

        public void ExchangeDeleteNoWait(string exchange, bool ifUnused) => throw new NotImplementedException();

        public void ExchangeUnbind(string destination, string source, string routingKey, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void ExchangeUnbindNoWait(string destination, string source, string routingKey, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void QueueBind(string queue, string exchange, string routingKey, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void QueueBindNoWait(string queue, string exchange, string routingKey, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public QueueDeclareOk QueueDeclare(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void QueueDeclareNoWait(string queue, bool durable, bool exclusive, bool autoDelete, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public QueueDeclareOk QueueDeclarePassive(string queue) => throw new NotImplementedException();

        public uint MessageCount(string queue) => throw new NotImplementedException();

        public uint ConsumerCount(string queue) => throw new NotImplementedException();

        public uint QueueDelete(string queue, bool ifUnused, bool ifEmpty) => throw new NotImplementedException();

        public void QueueDeleteNoWait(string queue, bool ifUnused, bool ifEmpty) => throw new NotImplementedException();

        public uint QueuePurge(string queue) => throw new NotImplementedException();

        public void QueueUnbind(string queue, string exchange, string routingKey, IDictionary<string, object> arguments)
            => throw new NotImplementedException();

        public void TxCommit() => throw new NotImplementedException();

        public void TxRollback() => throw new NotImplementedException();

        public void TxSelect() => throw new NotImplementedException();

        public bool WaitForConfirms() => throw new NotImplementedException();

        public bool WaitForConfirms(TimeSpan timeout) => throw new NotImplementedException();

        public bool WaitForConfirms(TimeSpan timeout, out bool timedOut) => throw new NotImplementedException();

        public void WaitForConfirmsOrDie() => throw new NotImplementedException();

        public void WaitForConfirmsOrDie(TimeSpan timeout) => throw new NotImplementedException();
    }
}
