using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;
using OE.Job.Lookup.Repository;
using OE.Shared.Utilities;
using System.Threading.Tasks;

namespace OE.Job.Lookup
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var host = CreateHostBuilder(args).Build();

            await host.RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<IMessageBroker, MessageBroker>();
                    services.AddSingleton<ISportsbookRepository, SportsbookRepository>();

                    // MongoDB
                    services.AddSingleton<IMongoClient>(sp =>
                        new MongoClient(hostContext.Configuration["Custom:MongoDB-Sportsbook"]));

                    services.AddHostedService<Worker>();
                });
    }
}
