using OE.WebSocket.Service.Telemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OE.WebSocket.Service.Extensions
{
    public static class TelemetryExtension
    {
        public static IServiceCollection AddTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            Action<ResourceBuilder> resource = r => r.AddService(
                serviceName: "OE.WebSocket.Service",
                serviceNamespace: configuration["Telemetry:Environment"] ?? "development",
                serviceInstanceId: Environment.MachineName);

            services.AddSingleton<Instrumentation>();

            if (Convert.ToBoolean(configuration["Telemetry:Enabled"]))
            {
                services
                    .AddOpenTelemetry()
                    .ConfigureResource(resource)
                    .WithTracing(tracing =>
                    {
                        tracing
                        .AddSource(Instrumentation.ActivitySourceName)
                        .SetSampler(new AlwaysOnSampler())
                        .AddOtlpExporter(o =>
                        {
                            o.Protocol = OtlpExportProtocol.HttpProtobuf;
                            o.Endpoint = new Uri(configuration["Telemetry:Collector"] + "/v1/traces");
                        });
                    })
                    .WithMetrics(metrics =>
                    {
                        metrics
                        .AddMeter(Instrumentation.MeterName)
                        .AddRuntimeInstrumentation()
                        .AddOtlpExporter(o =>
                        {
                            o.Protocol = OtlpExportProtocol.HttpProtobuf;
                            o.Endpoint = new Uri(configuration["Telemetry:Collector"] + "/v1/metrics");
                        });
                    });
            }

            return services;
        }
    }
}
