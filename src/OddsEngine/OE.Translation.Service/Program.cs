using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OE.Shared.Data.Translation;
using OE.Shared.Utilities;
using OE.Translation.Service.Repositories;
using OE.Translation.Service.Telemetry;
using Serilog;

namespace OE.Translation.Service
{
    // Translation owner for the odds engine (2026-07 translation-flow redesign, migrated from
    // bet-genius's BG.Translation.Service):
    //   * seeds the flat TranslationCache2 source side from the raw BG.Receiver stream (the
    //     retired SportsbookDynamic/SportsbookGlossary tables are gone — TC2 is the only store),
    //   * writes the per-language *NaTr arrays into the shared sportsbook MongoDB documents
    //     (OE.Sportsbook.Service no longer writes translations), and
    //   * owns the ON_TRANSLATION_CHANGE change path: persist deltas + emit translation-only
    //     ON_ODDS_CHANGE payloads for open fixtures.
    // Single queue (competing consumers) — each message is handled exactly once regardless of
    // replica count, unlike the retired 10-instance sportsbook fan-out.
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseSerilog((context, _, serilog) =>
                {
                    serilog.ReadFrom.Configuration(context.Configuration);
                })
                .ConfigureServices((hostContext, services) =>
                {
                    var configuration = hostContext.Configuration;
                    var dbContextTimeout = configuration.GetValue("Custom:DbContextTimeout", 180);

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
                    services.AddSingleton<ITranslationCache, TranslationCache>();
                    services.AddSingleton<ITranslationRepository, TranslationRepository>();
                    services.AddSingleton<IMongoTranslationRepository, MongoTranslationRepository>();

                    services.AddHostedService<Worker>();
                });
    }
}
