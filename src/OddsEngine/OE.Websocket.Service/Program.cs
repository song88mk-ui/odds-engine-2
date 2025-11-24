using OE.Shared.Utilities;
using OE.Websocket.Service;
using OE.WebSocket.Service.Repository;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry;
using System.Reflection;
using OE.WebSocket.Service.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Exporter;

Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices((context, services) =>
    {
        var configuration = services.BuildServiceProvider().GetService<IConfiguration>();

        var instanceId = configuration["MessageBroker:Queue"].Split('.').Last();

        Action<ResourceBuilder> resource = r => r.AddService(
            serviceName: configuration["Telemetry:ServiceName"],
            serviceNamespace: configuration["Telemetry:Environment"],
            serviceVersion: Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "latest",
            serviceInstanceId: $"{Environment.MachineName}-{instanceId}");

        services.AddSingleton<Instrumentation>();

        if (Convert.ToBoolean(configuration["Telemetry:Enabled"]))
        {
            services
              .AddOpenTelemetry()
              .ConfigureResource(resource)
              .WithTracing(t =>
              {
                  t
                  .AddSource(Instrumentation.ActivitySourceName)
                  .SetSampler(new AlwaysOnSampler())
                  .AddSqlClientInstrumentation()
                  .AddOtlpExporter(o =>
                  {
                      o.Protocol = OtlpExportProtocol.HttpProtobuf;
                      o.Endpoint = new Uri(configuration["Telemetry:Collector"] + "/v1/traces");
                  });
              })
              .WithMetrics(m =>
              {
                  m
                 .AddMeter(Instrumentation.MeterName)
                 .AddRuntimeInstrumentation()
                 .AddOtlpExporter(o =>
                 {
                     o.Protocol = OtlpExportProtocol.HttpProtobuf;
                     o.Endpoint = new Uri(configuration["Telemetry:Collector"] + "/v1/metrics");
                 });
              });
        }

        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<IMemCache, MemCache>();
        services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();