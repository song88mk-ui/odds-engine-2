using Microsoft.Extensions.Configuration;
using OE.Shared.Utilities;
using OE.UOF.Client;
using OE.UOF.Client.Telemetry;
using OE.UOF.Client.Repository;
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
        services.AddSingleton<IClientRepository, ClientRepository>();
        services.AddSingleton<IPlatformRepository, PlatformRepository>();
        services.AddSingleton<ITradingRepository, TradingRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();
