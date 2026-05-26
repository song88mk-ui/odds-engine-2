using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using OE.Shared.Data.Translation;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using OE.Sportsbook.Service.Telemetry;
using Serilog;
using System.Configuration;

namespace OE.Sportsbook.Service
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IMongoDatabase>();
                await MongoIndexBootstrapper.CreateIndexesAsync(db);
            }

            await host.RunAsync();
        }


        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog((context, _ , serilog) =>
                {
                    serilog.ReadFrom.Configuration(context.Configuration);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var configuration = hostContext.Configuration;
                    var dbContextTimeout = configuration.GetValue<int>("Custom:DbContextTimeout", 180);

                    services.AddSingleton<Instrumentation>();
                    services.AddTelemetry(new TelemetrySettings(
                        ServiceName: Instrumentation.ActivitySourceName,
                        ActivitySourceName: Instrumentation.ActivitySourceName,
                        MeterName: Instrumentation.MeterName));

                    services.AddDbContextPool<TranslationContext>(options =>
                        options.UseSqlServer(
                            configuration.GetConnectionString("Translation"),
                            sqlOptions => sqlOptions.CommandTimeout(dbContextTimeout)
                        ));

                    services.AddSingleton<IHelper, Helper>();
                    services.AddSingleton<IMemCache, MemCache>();
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
                    services.AddSingleton<ITranslationRepository, TranslationRepository>();

                    // MongoDB
                    services.AddSingleton<IMongoClient>(sp =>
                        new MongoClient(hostContext.Configuration["Custom:MongoDB-Sportsbook"]));
                    
                    services.AddSingleton<IMongoDatabase>(sp =>
                    {
                        var client = sp.GetRequiredService<IMongoClient>();
                        return client.GetDatabase("Sportsbook");
                    });

                    services.AddHostedService<Worker>();
                });
    }
}
