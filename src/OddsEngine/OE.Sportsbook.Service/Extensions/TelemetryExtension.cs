using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Sportsbook.Service.Telemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;

namespace OE.Sportsbook.Service.Extensions
{
    public static class ServiceExtension
    {
        public static IServiceCollection AddTelemetry(this IServiceCollection services)
        {

            var sp = services.BuildServiceProvider();

            var configuration = sp.GetService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<Instrumentation>>();

            Action<ResourceBuilder> resource = r => r.AddService(
                serviceName: "OE.Sportsbook.Service",
                serviceNamespace: configuration["Telemetry:Environment"] ?? "dev",
                serviceVersion: "latest",
                serviceInstanceId: Environment.MachineName);

            services.AddSingleton<Instrumentation>();

            if (Convert.ToBoolean(configuration["Telemetry:Enabled"]))
            {
                var metricsPort = configuration.GetValue<int>("Telemetry:MetricsPort", 8080);
                logger.LogInformation("Setting up Open Telemetry: Prometheus endpoint on port {metricsPort}", metricsPort);

                services
                .AddOpenTelemetry()
                .ConfigureResource(resource)
                .WithTracing(tracing =>
                {
                    tracing
                    .AddSource(Instrumentation.ActivitySourceName)
                    .SetSampler(new AlwaysOnSampler())
                    .AddOtlpExporter();
                })
                .WithMetrics(metrics =>
                {
                    metrics
                    .AddMeter(Instrumentation.MeterName)
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddPrometheusHttpListener(opts => opts.UriPrefixes = new string[] { $"http://+:{metricsPort}" });
                });
            }

            return services;
        }
    }
}
