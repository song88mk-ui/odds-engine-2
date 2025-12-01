using OE.Shared.Utilities;
using OE.UOF.Client;
using OE.UOF.Client.Extensions;
using OE.UOF.Client.Repository;


Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddTelemetry();
        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<IMemCache, MemCache>();
        services.AddSingleton<IClientRepository, ClientRepository>();
        services.AddSingleton<IPlatformRepository, PlatformRepository>();
        services.AddSingleton<ITradingRepository, TradingRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();
