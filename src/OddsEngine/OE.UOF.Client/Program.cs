using OE.Shared.Utilities;
using OE.UOF.Client;
using OE.UOF.Client.Repository;


Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<IMemCache, MemCache>();
        services.AddSingleton<IClientRepository, ClientRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();