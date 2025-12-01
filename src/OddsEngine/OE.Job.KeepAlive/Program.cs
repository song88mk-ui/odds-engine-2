using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OE.Shared.Utilities;

namespace OE.Job.KeepAlive
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
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddHostedService<Worker>();
                });
    }
}
