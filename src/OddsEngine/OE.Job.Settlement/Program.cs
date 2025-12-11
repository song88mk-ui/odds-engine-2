using OE.Job.Settlement;
using OE.Job.Settlement.Repository;
using OE.Shared.Utilities;

Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddHttpClient();
        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
        services.AddHostedService<Worker>();
    })
    .Build()
    .Run();
