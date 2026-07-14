# Template — New OEv2 Worker Service

Copy an existing service folder (e.g. `OE.Job.Cleanup` for a job, `OE.Adapter.Service` for an
event-driven worker) and adapt. Keep the house conventions ([docs/StartupProjects.md](../../docs/StartupProjects.md),
[docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md)).

## Required pieces
- `OE.<Area>.<Kind>.csproj` — `net8.0` (preferred), `<ProjectReference Include="..\OE.Shared\OE.Shared.csproj" />`,
  `Infra.Extensions.Telemetry`, Serilog stack, `Microsoft.Extensions.Hosting`, `MongoDB.Driver` if needed.
- `Program.cs` — `Host.CreateDefaultBuilder` → `UseSerilog(ReadFrom.Configuration)` →
  `AddSingleton<Instrumentation>()` + `AddTelemetry(new TelemetrySettings(...))` → register
  `IMessageBroker`/`IMemCache`/repos as **Singleton** → `AddHostedService<Worker>()`.
- `Worker.cs` — `BackgroundService`; event-driven (`Connect()` + `MessageReceived` dispatch on
  `BasicProperties.Type`) **or** timer-loop (`while` + `Task.Delay`, gated by `Custom:Enabled`).
- `Repository/IXxxRepository.cs` + `XxxRepository.cs` — name by **responsibility/store** (avoid the
  misleading-name trap). Mongo via `BulkWrite<T>` upserts; SQL via pooled `AsNoTracking`.
- `Telemetry/Instrumentation.cs` — `ActivitySourceName`/`MeterName` = service name; metric instruments.
- `appsettings.json` — `MessageBroker`/`Custom:*`/`Serilog`/`Telemetry`; **no real secrets** (dev stubs).
- `Dockerfile` — multi-stage SDK→aspnet; strip `appsettings.*.json`; AWS cert bundle if it talks to
  Mongo/RDS; `NUGET_PASSWORD` build-arg.

## Don't forget
- Add to `OddsEngine.sln` (KeepAlive's omission is a known gap).
- Add to CI matrix (`.github/workflows/ci.yml`) → ECR image `oev2-<short>`.
- Add argocd app (`apps/oddsengine/<service>/{dev,stg,prd}.yaml`) with env + External Secrets +
  routing-key bindings.
- Honor sharding if it binds `r.oe.adapter.#.<NN>`.
- Observe `CancellationToken` in loops (avoid the unclean-shutdown anti-pattern).
