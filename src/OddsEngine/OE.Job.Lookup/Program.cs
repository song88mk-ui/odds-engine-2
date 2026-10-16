using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using OE.Job.Lookup.Repository;
using OE.Job.Lookup.Telemetry;
using OE.Shared.Utilities;
using Serilog;
using System.Linq;
using System.Threading.Tasks;

namespace OE.Job.Lookup
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();
            var worker = host.Services.GetServices<IHostedService>().OfType<Worker>().Single();

            await host.RunAsync();

            // The host logs a failed BackgroundService and then exits 0, which Kubernetes records as Completed.
            // A lost consumer exits 1 so the restart shows up as an error.
            return worker.ConsumerLost ? 1 : 0;
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog((context, _, serilog) =>
                {
                    serilog.ReadFrom.Configuration(context.Configuration);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<Instrumentation>();
                    services.AddTelemetry(new TelemetrySettings(
                        ServiceName: Instrumentation.ActivitySourceName,
                        ActivitySourceName: Instrumentation.ActivitySourceName,
                        MeterName: Instrumentation.MeterName));

                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<ISportsbookRepository, SportsbookRepository>();

                    // MongoDB
                    services.AddSingleton<IMongoClient>(sp =>
                        new MongoClient(hostContext.Configuration["Custom:MongoDB-Sportsbook"]));

                    services.AddHostedService<Worker>();
                });
    }
}
