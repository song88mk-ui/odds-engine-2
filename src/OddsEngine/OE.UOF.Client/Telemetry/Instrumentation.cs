using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.UOF.Client.Telemetry
{

    public class Instrumentation : IDisposable
    {
        internal const string ActivitySourceName = "OE.UOF.Client";
        internal const string MeterName = "OE.UOF.Client";

        private readonly Meter meter;

        public Instrumentation()
        {
            string? version = "latest";
            this.ActivitySource = new ActivitySource(ActivitySourceName, version);
            this.meter = new Meter(MeterName, version);
            this.ForwardedMessagesCounter = this.meter.CreateCounter<long>("forwarded.messages", "count", "Number of forwarded messages to external brokers.");
        }

        public ActivitySource ActivitySource { get; }
        public Counter<long> ForwardedMessagesCounter { get; }

        public void Dispose()
        {
            this.ActivitySource.Dispose();
            this.meter.Dispose();
        }
    }
}
