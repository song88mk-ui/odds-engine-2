
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.LiveStream.Telemetry;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "OE.Job.LiveStream";
    internal const string MeterName = ActivitySourceName;
    internal const string serviceVersion = "latest";

    private readonly Meter meter;

    public Instrumentation()
    {
        this.ActivitySource = new ActivitySource(ActivitySourceName, serviceVersion);
        this.meter = new Meter(MeterName, serviceVersion);

        this.JobLastExecutionTimestampSeconds = this.meter.CreateGauge<long>("job_last_execution", "seconds",
                "The timestamp of the last successful or failed execution of the job");
        this.JobRunCount = this.meter.CreateCounter<long>("job_runs", "count",
                "The total number of times the job has ben executed");
        this.JobSuccessResult = this.meter.CreateGauge<short>("job_status", "result",
                "A binary gauge metric that is if the job was successful and 0 if it failed");
        this.JobRunFixturesUpdated = this.meter.CreateCounter<long>("job.run.fixtures.updated", "count",
                "The total number of records processed on the job run");
    }

    public ActivitySource ActivitySource { get; }

    public Gauge<long> JobLastExecutionTimestampSeconds { get; }
    public Counter<long> JobRunCount { get; }
    public Gauge<short> JobSuccessResult { get; }
    public Counter<long> JobRunFixturesUpdated { get; }

    public void Dispose()
    {
        this.ActivitySource.Dispose();
        this.meter.Dispose();
    }
}
