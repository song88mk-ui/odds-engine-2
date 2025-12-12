using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.Settlement.Telemetry;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "OE.Job.Settlement";
    internal const string MeterName = ActivitySourceName;
    internal const string serviceVersion = "latest";

    private readonly Meter meter;

    public Instrumentation()
    {
        this.ActivitySource = new ActivitySource(ActivitySourceName, serviceVersion);
        this.meter = new Meter(MeterName, serviceVersion);

        this.JobDurationMs = this.meter.CreateHistogram<double>("job.duration", "ms",
                "Histogram for duration for the job");
        this.JobLastExecutionTimestampSeconds = this.meter.CreateCounter<long>("job.last.execution.timestamp", "seconds",
                "The timestamp of the last successful or failed execution of the job");
        this.JobSuccessResult = this.meter.CreateGauge<short>("job", "success",
                "A binary gauge metric that is if the job was successful and 0 if it failed");
        this.JobRunTotal = this.meter.CreateCounter<long>("job.runs", "total",
                "The total number of times the job has ben executed");
        this.JobRunProcessedTotal = this.meter.CreateCounter<long>("job.run.processed.total", "total",
                "The total number of records processed on the job run");
    }

    public ActivitySource ActivitySource { get; }

    public Histogram<double> JobDurationMs { get; }
    public Counter<long> JobLastExecutionTimestampSeconds { get; }
    public Gauge<short> JobSuccessResult { get; }
    public Counter<long> JobRunTotal { get; }
    public Counter<long> JobRunProcessedTotal { get; }

    public void Dispose()
    {
        this.ActivitySource.Dispose();
        this.meter.Dispose();
    }
}
