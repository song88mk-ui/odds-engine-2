using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OE.Job.BoostedOddsCap.Alerts;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Job.BoostedOddsCap.Telemetry;
using OE.Shared.Utilities;
using Serilog;

namespace OE.Job.BoostedOddsCap
{
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
                    var notificationOptions = NotificationOptions.FromConfiguration(configuration);
                    var emailOptions = EmailOptions.FromConfiguration(configuration);

                    services.AddSingleton<Instrumentation>();
                    services.AddTelemetry(new TelemetrySettings(
                        ServiceName: Instrumentation.ActivitySourceName,
                        ActivitySourceName: Instrumentation.ActivitySourceName,
                        MeterName: Instrumentation.MeterName));
                    services.AddSingleton<IMessageBroker, MessageBroker>();

                    services.AddSingleton<IBoostSource>(_ => new SqlBoostSource(
                        configuration.GetConnectionString("Trading"),
                        configuration.GetValue<int>("Custom:DbContextTimeout", 30)));
                    services.AddSingleton<BoostSnapshotHolder>();
                    services.AddSingleton(new CapAlertTracker(configuration.GetValue<decimal>("Custom:HysteresisPercent", 0.25m)));
                    services.AddSingleton(sp => new OddsChangeEvaluator(
                        sp.GetRequiredService<CapAlertTracker>(),
                        ParseTemplateIds(configuration["Custom:PlayerPropMarketTemplateIds"])));

                    services.AddSingleton(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<AlertQueue>>();
                        var instrumentation = sp.GetRequiredService<Instrumentation>();

                        return new AlertQueue(configuration.GetValue<int>("Custom:AlertBufferSize", 1000), alert =>
                        {
                            instrumentation.AlertsDropped.Add(1);
                            logger.LogWarning("Alert queue is full, dropped a price boost cap alert for client {ClientCode}",
                                    alert.ClientCode);
                        });
                    });

                    services.AddSingleton(notificationOptions);
                    services.AddSingleton(emailOptions);
                    services.AddSingleton<ToastFactory>();
                    services.AddSingleton<INotificationPublisher, BrokerNotificationPublisher>();
                    services.AddSingleton<IAlertEmailSender, SmtpAlertEmailSender>();
                    services.AddSingleton(new RecipientRateLimiter(emailOptions.MaxPerRecipientPerHour));
                    services.AddSingleton<AlertDispatcher>();

                    // the refresher is registered first so its first load is under way before the consumer waits for it
                    services.AddHostedService<BoostRefreshWorker>();
                    services.AddHostedService<OddsChangeWorker>();
                    services.AddHostedService<AlertDeliveryWorker>();
                });

        private static ISet<string> ParseTemplateIds(string value)
        {
            return new HashSet<string>(
                (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }
}
