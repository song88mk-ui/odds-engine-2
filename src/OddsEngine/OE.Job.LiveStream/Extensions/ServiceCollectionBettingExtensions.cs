using Microsoft.Extensions.DependencyInjection;
using OE.Shared.Providers;
using OE.Shared.Repositories;
using OE.Shared.Utilities;
using OE.Shared.Providers.BGLiveStream;

namespace OE.Job.LiveStream.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddProviders(this IServiceCollection services)
        {
            // Providers
            services.AddScoped<ILiveStreamProvider, BetGeniusLiveStream>();

            // Factory
            services.AddScoped<ILiveStreamFactory, LiveStreamFactory>();

            // HTTP clients
            services.AddHttpClient<IBetGeniusRepository, BetGeniusRepository>();

            //Other Services
            services.AddSingleton<IMemCache, MemCache>();

            return services;
        }
    }
}
