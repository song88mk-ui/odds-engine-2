using Microsoft.EntityFrameworkCore;
using OE.Shared.Data.Translation;
using OE.Shared.Utilities;
using OE.Translation.Job.Repositories;
using OE.Translation.Job.Workers;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;
        var dbContextTimeout = configuration.GetValue<int>("Custom:DbContextTimeout", 180);

        services.AddDbContextPool<TranslationContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Translation"),
                sqlOptions => sqlOptions.CommandTimeout(dbContextTimeout)
            ));

        services.AddSingleton<IMessageBroker, MessageBroker>();
        services.AddScoped<ITranslationCleanupRepository, TranslationCleanupRepository>();

        services.AddHostedService<BetPlacementWorker>();
        services.AddHostedService<CleanupWorker>();
    })
    .Build();

host.Run();
