using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.Adapter.Service.Telemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry;
using Prometheus;
using System;

namespace OE.Adapter.Service.Extensions
{
    public static class ServiceExtension
    {
        public static IServiceCollection AddTelemetry(this IServiceCollection services)
        {

            var sp = services.BuildServiceProvider();

            var configuration = sp.GetService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<Instrumentation>>();

            Action<ResourceBuilder> resource = r => r.AddService(
                serviceName: "OE.Adapter.Service",
                serviceNamespace: configuration["Telemetry:Environment"] ?? "dev",
                serviceVersion: "latest",
                serviceInstanceId: Environment.MachineName);

            services.AddSingleton<Instrumentation>();

            if (Convert.ToBoolean(configuration["Telemetry:Enabled"]))
            {
                logger.LogInformation("Initialize OpenTelemetry and Prometheus Exporter");
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
                        .AddOtlpExporter();
                    });

                services.AddMetricServer(opts => { opts.Port = 8080; });
            }

            return services;
        }
    }
}
