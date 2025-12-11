using OE.Job.Settlement;
using OE.Job.Settlement.Repository;
using OE.Shared.Utilities;
using Serilog;

Host.CreateDefaultBuilder(args)
    .UseSerilog((context, _, serilog) =>
    {
        serilog.ReadFrom.Configuration(context.Configuration);
    })
    .ConfigureServices((context, services) =>
    {
        services.AddHttpClient();
        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();
