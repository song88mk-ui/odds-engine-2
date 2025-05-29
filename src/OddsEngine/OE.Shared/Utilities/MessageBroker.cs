using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace OE.Shared.Utilities
{
    public class MessageBroker : IDisposable, IMessageBroker
    {
        #region Fields

        private ConnectionFactory _connectionFactory;
        private IConnection _connection;
        private IChannel _channel;
        private AsyncEventingBasicConsumer _consumer;
        public event EventHandler<BasicDeliverEventArgs> MessageReceived;

        private readonly IConfiguration _configuration;

        private string _hostName = string.Empty;
        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string _virtualHost = string.Empty;
        private string _exchange = string.Empty;
        private string _queue = string.Empty;
        private string _consumerName = string.Empty;
        private string _routingKey = string.Empty;
        private string _prefetchCount = string.Empty;
        private bool _isExclusive = false;
        private bool _isPriority = false;

        private ConnectionFactory _externalConnectionFactory;
        private IConnection _externalConnection;
        private IChannel _externalChannel;

        private string _externalHostName = string.Empty;
        private string _externalUserName = string.Empty;
        private string _externalPassword = string.Empty;
        private string _externalVirtualHost = string.Empty;
        private string _externalExchange = string.Empty;
        private string _externalQueue = string.Empty;
        private string _externalConsumerName = string.Empty;
        private string _externalRoutingKey = string.Empty;
        private int _externalPort = 5671;

        private static readonly ActivitySource _activitySource = new ActivitySource(nameof(MessageBroker));
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        #endregion

        #region Ctor

        public MessageBroker(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        #endregion

        #region Methods

        public async Task Acknowledge(BasicDeliverEventArgs e)
        {
            await _consumer.Channel.BasicAckAsync(e.DeliveryTag, false);
        }

        public async Task NAcknowledge(BasicDeliverEventArgs e)
        {
            await _consumer.Channel.BasicNackAsync(e.DeliveryTag, false, true);
        }

        public async Task Connect()
        {
            _isExclusive = string.IsNullOrEmpty(_configuration["MessageBroker:IsExclusive"]) ? false : Convert.ToBoolean(_configuration["MessageBroker:IsExclusive"]);
            _isPriority = string.IsNullOrEmpty(_configuration["MessageBroker:IsPriority"]) ? false : Convert.ToBoolean(_configuration["MessageBroker:IsPriority"]);
            _hostName = _configuration["MessageBroker:Host"];
            _userName = _configuration["MessageBroker:Username"];
            _password = _configuration["MessageBroker:Password"];
            _virtualHost = _configuration["MessageBroker:VirtualHost"];
            _exchange = _configuration["MessageBroker:Exchange"];
            _queue = _isExclusive == true ? $"{_configuration["MessageBroker:Queue"]}.{Environment.MachineName.ToLower()}" : _configuration["MessageBroker:Queue"];
            _consumerName = _configuration["MessageBroker:Consumer"];
            _routingKey = _configuration["MessageBroker:RoutingKey"];
            _prefetchCount = string.IsNullOrEmpty(_configuration["MessageBroker:PrefetchCount"]) ? "1000" : _configuration["MessageBroker:PrefetchCount"];

            if (_connection != null)
                return;

            await PrepareConnection();
        }

        public async Task ExternalConnect()
        {
            _externalHostName = _configuration["ExternalMessageBroker:Host"];
            _externalUserName = _configuration["ExternalMessageBroker:Username"];
            _externalPassword = _configuration["ExternalMessageBroker:Password"];
            _externalVirtualHost = _configuration["ExternalMessageBroker:VirtualHost"];
            _externalExchange = _configuration["ExternalMessageBroker:Exchange"];
            _externalQueue = _configuration["ExternalMessageBroker:Queue"];
            _externalConsumerName = _configuration["ExternalMessageBroker:Consumer"];
            _externalRoutingKey = _configuration["ExternalMessageBroker:RoutingKey"];

            var portSection = _configuration.GetSection("ExternalMessageBroker:Port");

            if (portSection.Exists())
                _externalPort = int.Parse(_configuration["ExternalMessageBroker:Port"]);

            PrepareExternalConnection();
        }

        public async Task ConnectWithManualAck()
        {
            _hostName = _configuration["MessageBroker:Host"];
            _userName = _configuration["MessageBroker:Username"];
            _password = _configuration["MessageBroker:Password"];
            _virtualHost = _configuration["MessageBroker:VirtualHost"];
            _exchange = _configuration["MessageBroker:Exchange"];
            _queue = _configuration["MessageBroker:Queue"];
            _consumerName = _configuration["MessageBroker:Consumer"];
            _routingKey = _configuration["MessageBroker:RoutingKey"];
            _prefetchCount = string.IsNullOrEmpty(_configuration["MessageBroker:PrefetchCount"]) ? "1000" : _configuration["MessageBroker:PrefetchCount"];

            if (_connection != null)
                return;

            await PrepareConnection(true);
        }

        public async Task ExternalPublish<T>(T payload, string messageType, string routingKey, string correlationId)
        {
            var serializedPayload = JsonSerializer.Serialize(payload);

            await ExternalPublish(serializedPayload, messageType, routingKey, correlationId);
        }

        public async Task ExternalPublish(string payload, string messageType, string routingKey, string correlationId)
        {
            var properties = new BasicProperties();

            properties.CorrelationId = correlationId;
            properties.DeliveryMode = DeliveryModes.Persistent;
            properties.Type = messageType;

            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            await _externalChannel.BasicPublishAsync(_externalExchange, routingKey, false, properties, payloadBytes);
        }

        public async Task Publish<T>(T payload, string messageType, string routingKey, string correlationId)
        {
            var serializedPayload = JsonSerializer.Serialize(payload);

            await Publish(serializedPayload, messageType, routingKey, correlationId);
        }

        public async Task Publish(string payload, string messageType, string routingKey, string correlationId)
        {
            var activityName = $"{routingKey} send";
            var activity = _activitySource.StartActivity(activityName, ActivityKind.Producer);

            try
            {
                var properties = new BasicProperties();

                properties.CorrelationId = correlationId;
                properties.DeliveryMode = DeliveryModes.Persistent;
                properties.Type = messageType;

                ActivityContext contextToInject = default;

                if (activity != null)
                {
                    contextToInject = activity.Context;
                }
                else if (Activity.Current != null)
                {
                    contextToInject = Activity.Current.Context;
                }

                Propagator.Inject(new PropagationContext(contextToInject, Baggage.Current), properties, this.InjectTraceContextIntoBasicProperties);

                var payloadBytes = Encoding.UTF8.GetBytes(payload);

                await _channel.BasicPublishAsync(_exchange, routingKey, false, properties, payloadBytes);
            }
            finally
            {
                if (activity != null)
                {
                    activity.Stop();
                    activity.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (_channel != null)
                _channel.Dispose();

            if (_externalChannel != null)
                _externalChannel.Dispose();

            if (_connection != null)
                _connection.Dispose();
        }

        #endregion

        #region Utilities

        private async Task PrepareConnection(bool isManualAck = false)
        {
            if (_connectionFactory == null)
            {
                _connectionFactory = new ConnectionFactory
                {
                    HostName = _hostName,
                    Port = 5672,
                    UserName = _userName,
                    Password = _password,
                    VirtualHost = _virtualHost,
                    RequestedHeartbeat = TimeSpan.FromSeconds(60),
                    NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
                };
            }

            await PrepareChannel(_connectionFactory, _connection, _channel);

            if (!string.IsNullOrEmpty(_queue))
            {
                if (isManualAck)
                    await PrepareQueueWithManualAck();
                else
                    await PrepareQueue();
            }
        }

        private async Task PrepareExternalConnection()
        {
            if (_externalConnection == null)
            {
                if (_externalConnectionFactory == null)
                {
                    _externalConnectionFactory = new ConnectionFactory
                    {
                        HostName = _externalHostName,
                        Port = _externalPort,
                        UserName = _externalUserName,
                        Password = _externalPassword,
                        VirtualHost = _externalVirtualHost,
                        RequestedHeartbeat = TimeSpan.FromSeconds(60),
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                        Ssl = new SslOption { Enabled = _externalPort == 5671 ? true : false, ServerName = _externalHostName }
                    };
                }

                await PrepareChannel(_externalConnectionFactory, _externalConnection, _externalChannel);
            }
        }

        private async Task PrepareChannel(ConnectionFactory connectionFactory, IConnection connection, IChannel channel)
        {
            connection = await connectionFactory.CreateConnectionAsync();

            if (channel == null)
                channel = await connection.CreateChannelAsync();
        }

        private async Task PrepareQueue()
        {
            await _channel.ExchangeDeclareAsync(_exchange, "topic", true, false, null);

            if (_isExclusive)
            {
                if (_isPriority)
                {
                    var dictionary = new Dictionary<string, object>();
                    dictionary.Add("x-max-priority", 10);

                    await _channel.QueueDeclareAsync(_queue, true, true, true, dictionary);
                }
                else
                    await _channel.QueueDeclareAsync(_queue, true, true, true, null);
            }
            else
            {
                if (_isPriority)
                {
                    var dictionary = new Dictionary<string, object>();
                    dictionary.Add("x-max-priority", 10);

                    await _channel.QueueDeclareAsync(_queue, true, false, false, dictionary);
                }
                else
                    await _channel.QueueDeclareAsync(_queue, true, false, false, null);
            }

            foreach (var routingKey in _routingKey.Split('|'))
                await _channel.QueueBindAsync(_queue, _exchange, routingKey.ToLower(), null);

            await _channel.BasicQosAsync(0, Convert.ToUInt16(_prefetchCount), false);

            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.ReceivedAsync += HandleMessage;

            await _channel.BasicConsumeAsync(_queue, false, $"{_consumerName}.{Guid.NewGuid()}", _consumer);
        }

        private async Task PrepareQueueWithManualAck()
        {
            await _channel.ExchangeDeclareAsync(_exchange, "topic", true, false, null);
            await _channel.QueueDeclareAsync(_queue, true, false, false, null);

            foreach (var routingKey in _routingKey.Split('|'))
                await _channel.QueueBindAsync(_queue, _exchange, routingKey, null);

            await _channel.BasicQosAsync(0, Convert.ToUInt16(_prefetchCount), false);

            _consumer = new AsyncEventingBasicConsumer(_channel);
            _consumer.ReceivedAsync += HandleMessageWithManualAck;

            await _channel.BasicConsumeAsync(_queue, false, $"{_consumerName}.{Guid.NewGuid()}", _consumer);
        }

        private async Task HandleMessage(object sender, BasicDeliverEventArgs e)
        {
            try
            {
                MessageReceived(sender, e);

                await _consumer.Channel.BasicAckAsync(e.DeliveryTag, false);
            }
            catch (Exception)
            {
                await _consumer.Channel.BasicNackAsync(e.DeliveryTag, false, true);
            }
        }

        private async Task HandleMessageWithManualAck(object sender, BasicDeliverEventArgs e)
        {
            try
            {
                MessageReceived(sender, e);
            }
            catch (Exception)
            {
                await _consumer.Channel.BasicNackAsync(e.DeliveryTag, false, true);
            }
        }

        private void InjectTraceContextIntoBasicProperties(IBasicProperties props, string key, string value)
        {
            try
            {
                if (props.Headers == null)
                {
                    props.Headers = new Dictionary<string, object>();
                }
                props.Headers[key] = value;
            }
            catch (Exception)
            {
                //
            }
        }

        #endregion
    }
}
