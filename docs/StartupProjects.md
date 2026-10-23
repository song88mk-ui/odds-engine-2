# Startup Projects & Hosting Model

All executables use the **.NET Generic Host** (`Host.CreateDefaultBuilder`) and run one or more
`IHostedService`/`BackgroundService` workers. None expose HTTP endpoints (no Kestrel/MVC) — they
are headless workers. (`OE.API` would have been the only web project but is dead — see
[SolutionStructure.md](./SolutionStructure.md).)

## Two Program.cs styles — [Verified]

- **Top-level statements:** OE.UOF.Client, OE.Websocket.Service, OE.Job.Settlement.
- **Classic `class Program { Main / CreateHostBuilder }`:** OE.Adapter.Service,
  OE.Sportsbook.Service, OE.Job.LiveStream, OE.Job.Cleanup, OE.Job.KeepAlive, OE.Job.Lookup,
  OE.Job.BoostedOddsCap.

Both styles do the same three things: configure Serilog, register telemetry + services, add the
hosted `Worker`.

## Per-service composition root (DI registrations) — [Verified]

| Service | Serilog | Telemetry | Broker(s) | MemCache | DB clients | Other registrations |
|---|---|---|---|---|---|---|
| **Adapter.Service** | ✅ | ✅ | `IMessageBroker` | — | — | `IHelper`, `ITradingRepository` (`Program.cs:27-36`) |
| **Sportsbook.Service** | ✅ | ✅ | `IMessageBroker` | ✅ | `IMongoClient`+`IMongoDatabase("Sportsbook")` — **no SQL context** | `IHelper`, `ISportsbookRepository`; **runs MongoIndexBootstrapper at startup** (`Program.cs:21-25`) |
| **Translation.Service** | ✅ | ✅ | `IMessageBroker` | ✅ | `TranslationContext` (DbContextPool) | `IHelper`, `ITranslationCache`, `ITranslationRepository`, `IMongoTranslationRepository`; **two non-fatal startup migrations** — `SeedActiveSnapshot` (Mongo→SQL) and `BackfillActiveSnapshot` (cache→Mongo `*NaTr`), each wrapped so a failure logs and startup continues (`Worker.cs:100-126`) |
| **Translation.Job** | ❌ | ❌ | `IMessageBroker` | ❌ | `TranslationContext` (DbContextPool) | `ITranslationCleanupRepository` (**Scoped**); two hosted services — `BetPlacementWorker` (`r.bet.placement`) and `CleanupWorker` (timer, after `StartupDelaySeconds`) |
| **UOF.Client** | ✅ | ✅ | `IMessageBroker` (×used as internal + external) | ✅ | (Trading via repo) | `IClientRepository`, `IPlatformRepository`, `ITradingRepository` (`Program.cs:21-26`) |
| **Websocket.Service** | ✅ | ✅ | `IMessageBroker` | ✅ | (Mongo via repo) | `ISportsbookRepository`; builds SignalR `HubConnection` in Worker |
| **Job.Lookup** | ❌ (none) | ❌ (none) | `IMessageBroker` | — | `IMongoClient` | `ISportsbookRepository` (`Program.cs:21-31`) — **minimal host** |
| **Job.Settlement** | ✅ | ✅ | `IMessageBroker` | — | (Mongo via repo) | `AddHttpClient()`, `ISportsbookRepository` |
| **Job.LiveStream** | ✅ | ✅ | (via providers) | — | (Mongo via repo) | `AddProviders()` (factory + BG provider), `ISportsbookRepository` |
| **Job.Cleanup** | ✅ | ✅ | — | — | (Mongo via repo) | `ISportsbookRepository` |
| **Job.BoostedOddsCap** | ✅ | ✅ | `IMessageBroker` | — | `BoostContext` (read-only, built per refresh; **not** pooled) | three hosted services (`BoostRefreshWorker`, `OddsChangeWorker`, `AlertDeliveryWorker`); `BoostSnapshotHolder`, `CapAlertTracker`, `OddsChangeEvaluator`, `AlertQueue`, `AlertDispatcher`, `ToastFactory`, `INotificationPublisher`, `IAlertEmailSender` (all Singleton) |
| **Job.KeepAlive** | ✅ | ✅ | `IMessageBroker` | — | — | (broker only) |

> **Lifetime convention:** everything is registered **Singleton** (including repositories and the
> broker). This is deliberate for long-lived workers but means repositories must be
> stateless/thread-safe — and some are not (e.g. Sportsbook's `_IsBetStopGlobally`,
> UOF.Client's monthly-reset in-memory lists). See [TechnicalDebt.md](./TechnicalDebt.md).

> **Outlier:** `OE.Job.Lookup/Program.cs` does **not** configure Serilog or telemetry — it logs
> via the default console logger only. Worth normalizing.

## Telemetry bootstrap pattern — [Verified]

Identical in 8 services:

```csharp
services.AddSingleton<Instrumentation>();
services.AddTelemetry(new TelemetrySettings(
    ServiceName:        Instrumentation.ActivitySourceName,
    ActivitySourceName: Instrumentation.ActivitySourceName,
    MeterName:          Instrumentation.MeterName));
```

`AddTelemetry` comes from the private `Infra.Extensions.Telemetry` package. Each
`Telemetry/Instrumentation.cs` defines the service's `ActivitySourceName`, `MeterName`, and the
metric instruments (counters/histograms). OTLP export target is set by argocd env
(`OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo.monitoring.svc.cluster.local:4318`).

## Worker startup sequence (representative) — [Verified]

`OE.Sportsbook.Service` (the most involved):
1. `Program.Main` builds host, opens a DI scope, runs `MongoIndexBootstrapper.CreateIndexesAsync`
   (creates unique/compound indexes on Fixture/Market/Selection), then `host.RunAsync()`.
2. `Worker.ExecuteAsync` → `SetInitialData()` loads global bet-stop from `BettingStatus`.
3. `_messageBroker.Connect()` declares exchange/queue/bindings and starts consuming.
4. `MessageReceived` event handler dispatches by `BasicProperties.Type` (the message type).

Simpler workers (jobs) skip steps 1–2 and run a `while(true)` loop with `Task.Delay`.

## Shutdown behavior — [Verified] (uneven)

- Most `StopAsync` overrides just dispose the broker connection.
- **Gap:** several `while(true)` loops and the Websocket threads ignore the host
  `CancellationToken`, so graceful shutdown is not clean (KeepAlive, Websocket). See debt doc.

## How to run locally — [Likely]

`dotnet run` from a service folder uses its committed `appsettings.json` (mostly `localhost`
stubs) merged with `appsettings.Development.json` (git-ignored). You need local RabbitMQ + MongoDB
(+ SQL Server for Sportsbook/UOF). No docker-compose for local orchestration was found in-repo —
[Unknown] whether one exists elsewhere.
