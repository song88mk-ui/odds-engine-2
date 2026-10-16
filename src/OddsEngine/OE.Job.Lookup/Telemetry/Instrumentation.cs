using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.Lookup.Telemetry
{
    public class Instrumentation : IDisposable
    {
        internal const string ActivitySourceName = "OE.Job.Lookup";
        internal const string MeterName = "OE.Job.Lookup";

        private readonly Meter meter;

        public Instrumentation()
        {
            var version = "latest";
            this.ActivitySource = new ActivitySource(ActivitySourceName, version);
            this.meter = new Meter(MeterName, version);
            this.ProcessedMessagesCounter = this.meter.CreateCounter<long>("processed.messages", "count", "Number of processed messages");
            this.HandlerDurationMs = this.meter.CreateHistogram<double>("handler.duration", "ms", "Time to handle one message, mostly the Mongo lookup upserts");
        }

        public ActivitySource ActivitySource { get; }
        public Counter<long> ProcessedMessagesCounter { get; }
        public Histogram<double> HandlerDurationMs { get; }

        public void Dispose()
        {
            this.ActivitySource.Dispose();
            this.meter.Dispose();
        }
    }
}
