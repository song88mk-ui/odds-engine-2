using Microsoft.Extensions.Configuration;
using OE.Shared.Utilities;
using OE.Websocket.Service;
using OE.WebSocket.Service.Repository;
using OE.WebSocket.Service.Telemetry;
using Serilog;

Host.CreateDefaultBuilder(args)
    .UseSerilog((context, _ , serilog) =>
    {
        serilog.ReadFrom.Configuration(context.Configuration);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<Instrumentation>();
        services.AddTelemetry(new TelemetrySettings(
            ServiceName: Instrumentation.ActivitySourceName,
            ActivitySourceName: Instrumentation.ActivitySourceName,
            MeterName: Instrumentation.MeterName));

        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<IMemCache, MemCache>();
        services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();
