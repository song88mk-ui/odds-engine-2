using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Utilities;
using RabbitMQ.Client.Events;

namespace OE.Shared.Test.Utilities
{
    // MessageBroker is the publish spine every service goes through: it stamps DeliveryMode 2
    // (persistent), the message Type used for Worker dispatch, the CorrelationId, and the
    // OpenTelemetry trace headers, then publishes to the configured exchange.
    //
    // Connect() opens a real AMQP connection, so these tests bypass it and place a FakeModel in the
    // private _model/_consumer fields instead. That keeps the production class unchanged - no test
    // seam, no InternalsVisibleTo - at the cost of naming private fields here. If a field is
    // renamed, these tests fail fast on the reflection lookup rather than silently passing.
    public class MessageBrokerTests
    {
        private const string Exchange = "e.central";

        private static MessageBroker CreateBroker(FakeModel model, string exchange = Exchange)
        {
            var broker = new MessageBroker(
                new FakeConfiguration(new Dictionary<string, string?>()),
                NullLogger<MessageBroker>.Instance);

            SetPrivateField(broker, "_model", model);
            SetPrivateField(broker, "_exchange", exchange);
            SetPrivateField(broker, "_consumer", new EventingBasicConsumer(model));

            return broker;
        }

        private static void SetPrivateField(MessageBroker broker, string name, object value)
        {
            var field = typeof(MessageBroker).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.NotNull(field);

            field!.SetValue(broker, value);
        }

        private sealed record Payload(string FiId, int MaStId);

        [Fact]
        public void PublishGeneric_StampsPersistentDeliveryTypeAndCorrelationId()
        {
            var model = new FakeModel();
            var broker = CreateBroker(model);

            broker.Publish(new Payload("123", 4), "ON_ODDS_CHANGE", "r.oe.sb.engine.onoddschange", "cid-1");

            var published = Assert.Single(model.Published);
            Assert.Equal(2, published.Properties.DeliveryMode);
            Assert.Equal("ON_ODDS_CHANGE", published.Properties.Type);
            Assert.Equal("cid-1", published.Properties.CorrelationId);
        }

        [Fact]
        public void PublishGeneric_RoutesToConfiguredExchangeWithGivenRoutingKey()
        {
            var model = new FakeModel();
            var broker = CreateBroker(model);

            broker.Publish(new Payload("123", 4), "ON_ODDS_CHANGE", "r.oe.sb.engine.onoddschange", "cid-1");

            var published = Assert.Single(model.Published);
            Assert.Equal(Exchange, published.Exchange);
            Assert.Equal("r.oe.sb.engine.onoddschange", published.RoutingKey);
        }

        [Fact]
        public void PublishGeneric_SerializesPayloadAsJsonUtf8()
        {
            var model = new FakeModel();
            var broker = CreateBroker(model);
            var payload = new Payload("123", 4);

            broker.Publish(payload, "ON_ODDS_CHANGE", "r.oe.sb.engine.onoddschange", "cid-1");

            var published = Assert.Single(model.Published);
            Assert.Equal(JsonSerializer.Serialize(payload), Encoding.UTF8.GetString(published.Body));
        }

        [Fact]
        public void PublishString_SendsPayloadVerbatimWithoutReserializing()
        {
            var model = new FakeModel();
            var broker = CreateBroker(model);

            broker.Publish("""{"FiId":"123"}""", "ON_FIXTURE_CHANGE", "r.oe.adapter.onfixturechange.00", "cid-2");

            var published = Assert.Single(model.Published);
            Assert.Equal("""{"FiId":"123"}""", Encoding.UTF8.GetString(published.Body));
            Assert.Equal("ON_FIXTURE_CHANGE", published.Properties.Type);
            Assert.Equal(2, published.Properties.DeliveryMode);
        }

        // Propagators.DefaultTextMapPropagator is a no-op until the OpenTelemetry SDK installs a
        // real one, and OE.Shared references only OpenTelemetry.Api. So the header-writing callback
        // the broker hands to the propagator is asserted directly - that part is this repo's code.
        [Fact]
        public void InjectTraceContext_CreatesHeaderDictionaryWhenAbsent()
        {
            var properties = new RabbitMQ.Client.Framing.BasicProperties();

            Assert.Null(properties.Headers);

            InvokeInjectTraceContext(properties, "traceparent", "00-trace-span-01");

            Assert.NotNull(properties.Headers);
            Assert.Equal("00-trace-span-01", properties.Headers["traceparent"]);
        }

        [Fact]
        public void InjectTraceContext_KeepsExistingHeadersAndOverwritesSameKey()
        {
            var properties = new RabbitMQ.Client.Framing.BasicProperties
            {
                Headers = new Dictionary<string, object> { ["existing"] = "keep" }
            };

            InvokeInjectTraceContext(properties, "traceparent", "first");
            InvokeInjectTraceContext(properties, "traceparent", "second");

            Assert.Equal("keep", properties.Headers["existing"]);
            Assert.Equal("second", properties.Headers["traceparent"]);
        }

        [Fact]
        public void Publish_WithCurrentActivity_PublishesWithinTheProducerActivity()
        {
            using var listener = ListenToMessageBrokerActivities();
            var model = new FakeModel();
            var broker = CreateBroker(model);
            var started = new List<Activity>();

            listener.ActivityStarted = started.Add;

            broker.Publish("{}", "ON_FIXTURE_CHANGE", "r.oe.adapter.onfixturechange.00", "cid-3");

            Assert.Single(model.Published);
            var activity = Assert.Single(started);
            Assert.Equal("r.oe.adapter.onfixturechange.00 send", activity.OperationName);
            Assert.Equal(ActivityKind.Producer, activity.Kind);
        }

        [Fact]
        public void Publish_WithoutAnyActivity_StillPublishes()
        {
            var model = new FakeModel();
            var broker = CreateBroker(model);

            Activity.Current = null;

            broker.Publish("{}", "ON_FIXTURE_CHANGE", "r.oe.adapter.onfixturechange.00", "cid-4");

            Assert.Single(model.Published);
        }

        [Fact]
        public void Acknowledge_AcksOnlyTheDeliveredTag()
        {
            var model = new FakeModel();
            var broker = CreateBroker(model);

            broker.Acknowledge(new BasicDeliverEventArgs { DeliveryTag = 42 });

            var ack = Assert.Single(model.Acks);
            Assert.Equal(42UL, ack.DeliveryTag);
            Assert.False(ack.Multiple);
        }

        [Fact]
        public void NAcknowledge_RequeuesOnlyTheDeliveredTag()
        {
            var model = new FakeModel();
            var broker = CreateBroker(model);

            broker.NAcknowledge(new BasicDeliverEventArgs { DeliveryTag = 7 });

            var nack = Assert.Single(model.Nacks);
            Assert.Equal(7UL, nack.DeliveryTag);
            Assert.False(nack.Multiple);
            Assert.True(nack.Requeue);
        }

        [Theory]
        [InlineData(typeof(TimeoutException), true)]
        [InlineData(typeof(IOException), true)]
        [InlineData(typeof(InvalidOperationException), false)]
        public void RetryableException_ClassifiesTransientConnectionFailures(Type exceptionType, bool expected)
        {
            var exception = (Exception)Activator.CreateInstance(exceptionType)!;

            Assert.Equal(expected, InvokeRetryableException(exception));
        }

        [Fact]
        public void RetryableException_TreatsInnerSocketExceptionAsRetryable()
        {
            var exception = new InvalidOperationException("connect failed", new SocketException(10061));

            Assert.True(InvokeRetryableException(exception));
        }

        [Fact]
        public void RetryableException_TreatsUnrelatedInnerExceptionAsFatal()
        {
            var exception = new InvalidOperationException("bad config", new ArgumentException("nope"));

            Assert.False(InvokeRetryableException(exception));
        }

        private static void InvokeInjectTraceContext(RabbitMQ.Client.IBasicProperties properties, string key, string value)
        {
            var method = typeof(MessageBroker).GetMethod(
                "InjectTraceContextIntoBasicProperties",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);

            method!.Invoke(null, new object[] { properties, key, value });
        }

        private static bool InvokeRetryableException(Exception exception)
        {
            var method = typeof(MessageBroker).GetMethod(
                "RetryableException",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.NotNull(method);

            return (bool)method!.Invoke(null, new object[] { exception })!;
        }

        // The broker only injects trace headers when an Activity is actually created, which needs a
        // listener that samples its ActivitySource.
        private static ActivityListener ListenToMessageBrokerActivities()
        {
            var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == nameof(MessageBroker),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };

            ActivitySource.AddActivityListener(listener);

            return listener;
        }
    }
}
