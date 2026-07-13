using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OE.Shared.Utilities;
using OE.Job.Cleanup.Data;
using OE.Job.Cleanup.Repository;
using OE.Job.Cleanup.Telemetry;
using Serilog;

namespace OE.Job.Cleanup
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
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

                    services.AddDbContextPool<BettingContext>(options =>
                        options.UseSqlServer(
                            configuration.GetConnectionString("Betting"),
                            sqlOptions => sqlOptions.CommandTimeout(dbContextTimeout)
                        ));

                    services.AddSingleton<Instrumentation>();
                    services.AddTelemetry(new TelemetrySettings(
                        ServiceName: Instrumentation.ActivitySourceName,
                        ActivitySourceName: Instrumentation.ActivitySourceName,
                        MeterName: Instrumentation.MeterName));
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<IBetFixtureCache, BetFixtureCache>();
                    services.AddSingleton<ISportsbookRepository, SportsbookRepository>();
                    services.AddHostedService<BetPlacementWorker>();
                    services.AddHostedService<Worker>();
                });
    }
}
