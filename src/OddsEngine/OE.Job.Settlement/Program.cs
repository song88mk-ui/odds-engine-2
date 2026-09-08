using OE.Job.Settlement;
using OE.Job.Settlement.Repository;
using OE.Shared.Utilities;
using Serilog;
using OE.Job.Settlement.Telemetry;

Host.CreateDefaultBuilder(args)
    .UseSerilog((context, _, serilog) =>
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
        services.AddHttpClient();
        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();
