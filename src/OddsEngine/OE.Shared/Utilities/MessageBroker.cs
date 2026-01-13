using Microsoft.Extensions.Configuration;
using OE.Shared.Models;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OE.Shared.Utilities
{
    public class MessageBroker : IDisposable, IMessageBroker
    {
        private ConnectionFactory _connectionFactory;
        private IConnection _connection;
        private IModel _model;
        private EventingBasicConsumer _consumer;
        public event EventHandler<BasicDeliverEventArgs> MessageReceived;

        private readonly IConfiguration _configuration;

        private string _hostName = string.Empty;
        private int    _hostPort = 5672; // default non-tls
        private string _userName = string.Empty;
        private string _password = string.Empty;
        private string _virtualHost = string.Empty;
        private string _exchange = string.Empty;
        private string _queue = string.Empty;
        private string _consumerName = string.Empty;
        private string _routingKey = string.Empty;
        private string _prefetchCount = string.Empty;
        private bool   _isExclusive = false;
        private bool   _isPriority = false;

        private ConnectionFactory _externalConnectionFactory;
        private IConnection _externalConnection;
        private IModel _externalModel;

        private string _externalHostName = string.Empty;
        private int    _externalPort = 5672; // default non-tls
        private string _externalUserName = string.Empty;
        private string _externalPassword = string.Empty;
        private string _externalVirtualHost = string.Empty;
        private string _externalExchange = string.Empty;
        private string _externalQueue = string.Empty;
        private string _externalConsumerName = string.Empty;
        private string _externalRoutingKey = string.Empty;

        private static readonly ActivitySource _activitySource = new ActivitySource(nameof(MessageBroker));
        private static readonly TextMapPropagator Propagator = Propagators.DefaultTextMapPropagator;

        public MessageBroker(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void Acknowledge(BasicDeliverEventArgs e)
        {
            _consumer.Model.BasicAck(e.DeliveryTag, false);
        }

        public void NAcknowledge(BasicDeliverEventArgs e)
        {
            _consumer.Model.BasicNack(e.DeliveryTag, false, true);
        }

        public void ExternalConnect()
        {
            _externalHostName = _configuration["ExternalMessageBroker:Host"];
            _externalUserName = _configuration["ExternalMessageBroker:Username"];
            _externalPassword = _configuration["ExternalMessageBroker:Password"];
            _externalVirtualHost = _configuration["ExternalMessageBroker:VirtualHost"];
            _externalExchange = _configuration["ExternalMessageBroker:Exchange"];
            _externalQueue = _configuration["ExternalMessageBroker:Queue"];
            _externalConsumerName = _configuration["ExternalMessageBroker:Consumer"];
            _externalRoutingKey = _configuration["ExternalMessageBroker:RoutingKey"];
            _externalPort = _configuration.GetValue<int>("ExternalMessageBroker:Port", 5672);

            PrepareExternalConnection();
        }

        public void ExternalConnect(MessageBrokerModel model)
        {
            _externalHostName = model.Host;
            _externalUserName = model.Username;
            _externalPassword = model.Password;
            _externalVirtualHost = model.VirtualHost;
            _externalExchange = model.Exchange;
            _externalQueue = model.Queue;
            _externalConsumerName = model.Consumer;
            _externalRoutingKey = model.RoutingKey;

            if (!string.IsNullOrEmpty(model.Port))
                _externalPort = int.Parse(model.Port);

            PrepareExternalConnection();
        }

        public void Connect()
        {
            _isExclusive = string.IsNullOrEmpty(_configuration["MessageBroker:IsExclusive"]) ? false : Convert.ToBoolean(_configuration["MessageBroker:IsExclusive"]);
            _isPriority = string.IsNullOrEmpty(_configuration["MessageBroker:IsPriority"]) ? false : Convert.ToBoolean(_configuration["MessageBroker:IsPriority"]);
            _hostName = _configuration["MessageBroker:Host"];
            _hostPort = _configuration.GetValue<int>("MessageBroker:Port", 5672);
            _userName = _configuration["MessageBroker:Username"];
            _password = _configuration["MessageBroker:Password"];
            _virtualHost = _configuration["MessageBroker:VirtualHost"];
            _exchange = _configuration["MessageBroker:Exchange"];

            var clientProperties = new Dictionary<string, object>
            {
                { "host_name", Environment.MachineName },
                { "application_name", "Song88 Platform" }
            };

            var suffix = "";

            if (_isExclusive)
            {
                using (MD5 hasher = MD5.Create())
                {
                    byte[] bytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(Environment.MachineName));
                    StringBuilder sb = new StringBuilder();
                    foreach(byte b in bytes)
                        sb.Append(b.ToString("x2"));
                    suffix = sb.ToString().Substring(0,5);
                }
            }
            _queue = _isExclusive == true ? $"{_configuration["MessageBroker:Queue"]}.{suffix}" : _configuration["MessageBroker:Queue"];
            _consumerName = _configuration["MessageBroker:Consumer"];
            _routingKey = _configuration["MessageBroker:RoutingKey"];
            _prefetchCount = string.IsNullOrEmpty(_configuration["MessageBroker:PrefetchCount"]) ? "1000" : _configuration["MessageBroker:PrefetchCount"];

            if (_connection == null)
            {
                if (_connectionFactory == null)
                {
                    _connectionFactory = new ConnectionFactory
                    {
                        HostName = _hostName,
                        Port = _hostPort,
                        UserName = _userName,
                        Password = _password,
                        VirtualHost = _virtualHost,
                        RequestedHeartbeat = 60,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                        Ssl = new SslOption { Enabled = _hostPort == 5671 ? true : false, ServerName = _hostName },
                        ClientProperties = clientProperties
                    };
                }

                _connection = _connectionFactory.CreateConnection();

                if (_model == null)
                    _model = _connection.CreateModel();

                if (!string.IsNullOrEmpty(_queue))
                {
                    _model.ExchangeDeclare(_exchange, "topic", true, false, null);

                    if (_isExclusive)
                    {
                        if (_isPriority)
                        {
                            var dictionary = new Dictionary<string, object>();
                            dictionary.Add("x-max-priority", 10);

                            _model.QueueDeclare(_queue, true, true, true, dictionary);
                        }
                        else
                            _model.QueueDeclare(_queue, true, true, true, null);
                    }
                    else
                    {
                        if (_isPriority)
                        {
                            var dictionary = new Dictionary<string, object>();
                            dictionary.Add("x-max-priority", 10);

                            _model.QueueDeclare(_queue, true, false, false, dictionary);
                        }
                        else
                            _model.QueueDeclare(_queue, true, false, false, null);
                    }

                    foreach (var routingKey in _routingKey.Split('|'))
                        _model.QueueBind(_queue, _exchange, routingKey.ToLower(), null);

                    _model.BasicQos(0, Convert.ToUInt16(_prefetchCount), false);
                    _consumer = new EventingBasicConsumer(_model);
                    _consumer.Received += HandleMessage;

                    _model.BasicConsume(_queue, false, $"{_consumerName}.{Guid.NewGuid()}", _consumer);
                }
            }
        }

        public void ConnectWithManualAck()
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
            _hostPort = _configuration.GetValue<int>("MessageBroker:Port", 5672);

            var clientProperties = new Dictionary<string, object>
            {
                { "host_name", Environment.MachineName },
                { "application_name", "Song88 Platform" }
            };

            if (_connection == null)
            {
                if (_connectionFactory == null)

                    _connectionFactory = new ConnectionFactory
                    {
                        HostName = _hostName,
                        Port = _hostPort,
                        UserName = _userName,
                        Password = _password,
                        VirtualHost = _virtualHost,
                        RequestedHeartbeat = 60,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                        Ssl = new SslOption { Enabled = _hostPort == 5671 ? true : false, ServerName = _hostName },
                        ClientProperties = clientProperties
                    };

                _connection = _connectionFactory.CreateConnection();

                if (_model == null)
                    _model = _connection.CreateModel();

                if (!string.IsNullOrEmpty(_queue))
                {
                    _model.ExchangeDeclare(_exchange, "topic", true, false, null);
                    _model.QueueDeclare(_queue, true, false, false, null);

                    foreach (var routingKey in _routingKey.Split('|'))
                        _model.QueueBind(_queue, _exchange, routingKey, null);

                    _model.BasicQos(0, Convert.ToUInt16(_prefetchCount), false);
                    _consumer = new EventingBasicConsumer(_model);
                    _consumer.Received += HandleMessageWithManualAck;

                    _model.BasicConsume(_queue, false, $"{_consumerName}.{Guid.NewGuid()}", _consumer);
                }
            }
        }

        private void HandleMessageWithManualAck(object sender, BasicDeliverEventArgs e)
        {
            try
            {
                MessageReceived(sender, e);
            }
            catch (Exception)
            {
                _consumer.Model.BasicNack(e.DeliveryTag, false, true);
            }
        }

        private void HandleMessage(object sender, BasicDeliverEventArgs e)
        {
            try
            {
                MessageReceived(sender, e);

                _consumer.Model.BasicAck(e.DeliveryTag, false);
            }
            catch (Exception)
            {
                _consumer.Model.BasicNack(e.DeliveryTag, false, true);
            }
        }

        public void ExternalPublish<T>(T payload, string messageType, string routingKey, string correlationId)
        {
            var properties = _externalModel.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.DeliveryMode = 2;
            properties.Type = messageType;

            var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            _externalModel.BasicPublish(_externalExchange, routingKey, properties, payloadBytes);
        }


        public void ExternalPublish(string payload, string messageType, string routingKey, string correlationId)
        {
            var properties = _externalModel.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.DeliveryMode = 2;
            properties.Type = messageType;

            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            _externalModel.BasicPublish(_externalExchange, routingKey, properties, payloadBytes);
        }

        public void Publish<T>(T payload, string messageType, string routingKey, string correlationId)
        {
          var activityName = $"{routingKey} send";
          using (var activity = _activitySource.StartActivity(activityName, ActivityKind.Producer))
          {
            var properties = _model.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.DeliveryMode = 2;
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

            var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));

            _model.BasicPublish(_exchange, routingKey, properties, payloadBytes);
          }
        }

        public void Publish(string payload, string messageType, string routingKey, string correlationId)
        {
          var activityName = $"{routingKey} send";
          using (var activity = _activitySource.StartActivity(activityName, ActivityKind.Producer))
          {
            var properties = _model.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.DeliveryMode = 2;
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

            _model.BasicPublish(_exchange, routingKey, properties, payloadBytes);
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
          catch (Exception) {}
        }

        private void PrepareExternalConnection()
        {
            if (_externalConnection == null)
            {
                if (_externalConnectionFactory == null)
                {
                    var clientProperties = new Dictionary<string, object>
                    {
                        { "application_name", "Song88 Platform" }
                    };

                    _externalConnectionFactory = new ConnectionFactory
                    {
                        HostName = _externalHostName,
                        Port = _externalPort,
                        UserName = _externalUserName,
                        Password = _externalPassword,
                        VirtualHost = _externalVirtualHost,
                        RequestedHeartbeat = 60,
                        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                        Ssl = new SslOption { Enabled = _externalPort == 5671 ? true : false, ServerName = _externalHostName },
                        ClientProperties = clientProperties
                    };
                }

                _externalConnection = _externalConnectionFactory.CreateConnection();

                if (_externalModel == null)
                    _externalModel = _externalConnection.CreateModel();
            }
        }

        public void Dispose()
        {
            if (_model != null)
                _model.Dispose();

            if (_externalModel != null)
                _externalModel.Dispose();

            if (_connection != null)
                _connection.Dispose();
        }
    }
}
