using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Adapter.Service.Repositories;
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
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddSimpleConsole(console => { console.SingleLine = true; });
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<IHelper, Helper>();
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<ITradingRepository, TradingRepository>();

                    services.AddHostedService<Worker>();
                });
    }
}
