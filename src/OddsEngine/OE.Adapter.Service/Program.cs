using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Adapter.Service.Repositories;
using OE.Adapter.Service.Extensions;
using OE.Shared.Utilities;

namespace OE.Adapter.Service
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
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<ITradingRepository, TradingRepository>();

                    services.AddHostedService<Worker>();
                });
    }
}
