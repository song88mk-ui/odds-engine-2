using OE.Shared.Utilities;
using OE.Websocket.Service;
using OE.WebSocket.Service.Repository;
using OE.WebSocket.Service.Extensions;

Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddTelemetry();

        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<IMemCache, MemCache>();
        services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();
