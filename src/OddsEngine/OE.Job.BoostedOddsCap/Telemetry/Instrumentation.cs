using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.BoostedOddsCap.Telemetry
{
    public class Instrumentation : IDisposable
    {
        internal const string ActivitySourceName = "OE.Job.BoostedOddsCap";
        internal const string MeterName = ActivitySourceName;
        internal const string serviceVersion = "latest";

        private readonly Meter meter;

        public Instrumentation()
        {
            this.ActivitySource = new ActivitySource(ActivitySourceName, serviceVersion);
            this.meter = new Meter(MeterName, serviceVersion);

            this.MessagesReceived = this.meter.CreateCounter<long>("boostedcap.messages.received", "count",
                    "Odds change messages read off the queue");
            this.MessagesMatched = this.meter.CreateCounter<long>("boostedcap.messages.matched", "count",
                    "Odds change messages for a fixture that has boosts, so were fully read and evaluated");
            this.EvaluationErrors = this.meter.CreateCounter<long>("boostedcap.evaluation.errors", "count",
                    "Odds change messages that could not be evaluated");
            this.EvaluationDuration = this.meter.CreateHistogram<double>("boostedcap.evaluation.duration", "ms",
                    "Time to read and evaluate an odds change message that has boosts");

            this.AlertsRaised = this.meter.CreateCounter<long>("boostedcap.alerts.raised", "count",
                    "Boosts that reached their cap");
            this.AlertsDropped = this.meter.CreateCounter<long>("boostedcap.alerts.dropped", "count",
                    "Alerts discarded because the delivery queue was full");
            this.ToastsPublished = this.meter.CreateCounter<long>("boostedcap.toasts.published", "count",
                    "Back-office notifications published");
            this.ToastFailures = this.meter.CreateCounter<long>("boostedcap.toasts.failed", "count",
                    "Back-office notifications that could not be published");
            this.EmailsSent = this.meter.CreateCounter<long>("boostedcap.emails.sent", "count", "Emails sent");
            this.EmailsFailed = this.meter.CreateCounter<long>("boostedcap.emails.failed", "count",
                    "Emails the mail server did not accept");
            this.EmailsSuppressed = this.meter.CreateCounter<long>("boostedcap.emails.suppressed", "count",
                    "Emails withheld by the per-recipient hourly limit");

            this.IndexRules = this.meter.CreateGauge<long>("boostedcap.index.rules", "count",
                    "Boosts held in the in-memory index");
            this.IndexFixtures = this.meter.CreateGauge<long>("boostedcap.index.fixtures", "count",
                    "Fixtures with at least one boost in the in-memory index");
            this.IndexLastRefreshTimestampSeconds = this.meter.CreateGauge<long>("boostedcap.index.last_refresh", "seconds",
                    "The timestamp of the last successful index refresh");
            this.IndexRefreshFailures = this.meter.CreateCounter<long>("boostedcap.index.refresh.failures", "count",
                    "Index refreshes that failed and left the previous index in place");
        }

        public ActivitySource ActivitySource { get; }

        public Counter<long> MessagesReceived { get; }
        public Counter<long> MessagesMatched { get; }
        public Counter<long> EvaluationErrors { get; }
        public Histogram<double> EvaluationDuration { get; }
        public Counter<long> AlertsRaised { get; }
        public Counter<long> AlertsDropped { get; }
        public Counter<long> ToastsPublished { get; }
        public Counter<long> ToastFailures { get; }
        public Counter<long> EmailsSent { get; }
        public Counter<long> EmailsFailed { get; }
        public Counter<long> EmailsSuppressed { get; }
        public Gauge<long> IndexRules { get; }
        public Gauge<long> IndexFixtures { get; }
        public Gauge<long> IndexLastRefreshTimestampSeconds { get; }
        public Counter<long> IndexRefreshFailures { get; }

        public void Dispose()
        {
            this.ActivitySource.Dispose();
            this.meter.Dispose();
        }
    }
}
