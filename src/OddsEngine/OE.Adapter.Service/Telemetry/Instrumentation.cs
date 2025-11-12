using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Adapter.Service.Telemetry
{

    public class Instrumentation : IDisposable
    {
        internal const string ActivitySourceName = "OE.Adapter.Service";
        internal const string MeterName = "OE.Adapter.Service";

        private readonly Meter meter;

        public Instrumentation()
        {
            string? version = "latest";
            this.ActivitySource = new ActivitySource(ActivitySourceName, version);
            this.meter = new Meter(MeterName, version);
            this.ProcessedMessagesCounter = this.meter.CreateCounter<long>("processed.messages", "count", "Number of processed messages");
        }

        public ActivitySource ActivitySource { get; }
        public Counter<long> ProcessedMessagesCounter { get; }

        public void Dispose()
        {
            this.ActivitySource.Dispose();
            this.meter.Dispose();
        }
    }
}
