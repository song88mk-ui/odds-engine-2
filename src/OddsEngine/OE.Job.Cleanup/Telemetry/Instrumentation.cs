using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.Job.Cleanup.Telemetry;

public class Instrumentation : IDisposable
{
    internal const string ActivitySourceName = "OE.Job.Cleanup";
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
        this.JobRunFixturesDeleted = this.meter.CreateCounter<long>("job.run.fixtures.deleted", "count",
                "The total number of records processed on the job run");
        this.JobRunOrphansDeleted = this.meter.CreateCounter<long>("job.run.orphans.deleted", "count",
                "The total number of fixture ids whose orphaned markets and selections were removed on the job run");
    }

    public ActivitySource ActivitySource { get; }

    public Gauge<long> JobLastExecutionTimestampSeconds { get; }
    public Counter<long> JobRunCount { get; }
    public Gauge<short> JobSuccessResult { get; }
    public Counter<long> JobRunFixturesDeleted { get; }
    public Counter<long> JobRunOrphansDeleted { get; }

    public void Dispose()
    {
        this.ActivitySource.Dispose();
        this.meter.Dispose();
    }
}
