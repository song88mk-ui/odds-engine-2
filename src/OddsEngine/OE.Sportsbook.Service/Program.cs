using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using OE.Sportsbook.Service.Telemetry;
using Serilog;

namespace OE.Sportsbook.Service
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
                    services.AddSingleton<Instrumentation>();
                    services.AddTelemetry(new TelemetrySettings(
                        ServiceName: Instrumentation.ActivitySourceName,
                        ActivitySourceName: Instrumentation.ActivitySourceName,
                        MeterName: Instrumentation.MeterName));

                    services.AddSingleton<IHelper, Helper>();
                    services.AddSingleton<IMemCache, MemCache>();
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<ISportsbookRepository, SportsbookRepository>();

                    services.AddHostedService<Worker>();
                });
    }
}
