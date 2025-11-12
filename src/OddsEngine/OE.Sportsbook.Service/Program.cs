using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OE.Shared.Utilities;
using OE.Sportsbook.Service.Repositories;
using OE.Sportsbook.Service.Extensions;

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
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddTelemetry();

                    services.AddSingleton<IHelper, Helper>();
                    services.AddSingleton<IMemCache, MemCache>();
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<ISportsbookRepository, SportsbookRepository>();

                    services.AddHostedService<Worker>();
                });
    }
}
