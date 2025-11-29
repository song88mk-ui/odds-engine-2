using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OE.UOF.Client.Telemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;

namespace OE.UOF.Client.Extensions
{
    public static class TelemetryExtension
    {
        public static IServiceCollection AddTelemetry(this IServiceCollection services)
        {
            var sp = services.BuildServiceProvider();

            var configuration = sp.GetService<IConfiguration>();
            var logger = sp.GetRequiredService<ILogger<Instrumentation>>();

            Action<ResourceBuilder> resource = r => r.AddService(
                serviceName: Instrumentation.ActivitySourceName,
                serviceVersion: "latest",
                serviceInstanceId: Environment.MachineName);

            services.AddSingleton<Instrumentation>();

            if (!Convert.ToBoolean(configuration["Telemetry:Enabled"]))
                return services;

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

            return services;
        }
    }
}
