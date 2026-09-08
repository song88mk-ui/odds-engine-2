using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Sportsbook.Service.Telemetry
{

    public class Instrumentation : IDisposable
    {
        internal const string ActivitySourceName = "OE.Sportsbook.Service";
        internal const string MeterName = "OE.Sportsbook.Service";

        private readonly Meter meter;

        public Instrumentation()
        {
            string? version = "latest";
            this.ActivitySource = new ActivitySource(ActivitySourceName, version);
            this.meter = new Meter(MeterName, version);
            this.ProcessedMessagesCounter = this.meter.CreateCounter<long>("processed.messages", "count", "Number of processed messages");
            this.BulkWriteDurationHistogram = this.meter.CreateHistogram<double>("bulkwrite.duration", "ms", "Histogram of bulk write operation duration");
        }

        public ActivitySource ActivitySource { get; }
        public Counter<long> ProcessedMessagesCounter { get; }
        public Histogram<double> BulkWriteDurationHistogram { get; }

        public void Dispose()
        {
            this.ActivitySource.Dispose();
            this.meter.Dispose();
        }
    }
}
