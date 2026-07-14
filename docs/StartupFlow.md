# Startup Flow

> **Phase 2 scope note.** Phase 2's template targets a *web* application. `odds-engine-2` has
> **no web app** — it is 9 headless .NET worker processes (the sibling **platform-web** is the
> Blazor app and is already documented in `platform-web/docs/`). This document covers worker
> **startup & initialization**. Web-only concerns (middleware/auth/authz) are addressed — with
> their worker analogues — in [MiddlewarePipeline.md](./MiddlewarePipeline.md),
> [Authentication.md](./Authentication.md), [Authorization.md](./Authorization.md). See also Phase 1
> [StartupProjects.md](./StartupProjects.md) (this doc supersedes/expands it for runtime detail).

All findings [Verified] unless tagged.

## 1. Hosting model — Generic Host (not WebHost)

Every executable uses `Host.CreateDefaultBuilder(args)` → `.Build().Run()` (or `RunAsync`).
There is **no `WebApplication.CreateBuilder`, no Kestrel, no `app.Use*` pipeline**. Compare to
platform-web's `Program.cs` which *does* use `WebApplication.CreateBuilder` + a middleware
pipeline — that contrast is the defining difference between the two repos.

`Host.CreateDefaultBuilder` gives each worker, for free:
- Config sources (appsettings → appsettings.{Env} → user secrets (Dev) → env vars → args) — see
  [Configuration.md](./Configuration.md).
- Console logging (then replaced/augmented by Serilog via `UseSerilog`).
- DI container, `IHostedService` lifecycle, graceful-shutdown signal wiring (SIGTERM/Ctrl-C).

## 2. Generic startup sequence (all services)

```
Main(args)
 └─ Host.CreateDefaultBuilder(args)
     ├─ UseSerilog((ctx,_,cfg) => cfg.ReadFrom.Configuration(ctx.Configuration))   // 8/9 services
     └─ ConfigureServices((ctx, services) => {
            services.AddSingleton<Instrumentation>();
            services.AddTelemetry(new TelemetrySettings(... Instrumentation names ...));
            // service-specific singletons (broker, repos, mongo, dbcontext, providers)
            services.AddHostedService<Worker>();
        })
 └─ .Build()
 └─ .Run()  →  Worker.StartAsync → Worker.ExecuteAsync (the real work begins here)
```

Two code styles exist (top-level statements vs `class Program`) but the sequence is identical.
DI registration detail: [DependencyInjection.md](./DependencyInjection.md).

## 3. Per-service startup order & initialization dependencies

The **order of operations inside `ExecuteAsync`** is where the meaningful startup logic lives.

### OE.Sportsbook.Service (most complex — the canonical example)
1. **`Program.Main`** builds host, opens a **DI scope**, resolves `IMongoDatabase`, and runs
   `MongoIndexBootstrapper.CreateIndexesAsync(db)` **before** `host.RunAsync()`
   (`Program.cs:21-27`). → *Init dependency: MongoDB must be reachable at startup* or indexes
   fail. Creates unique `idx_fiid/idx_maid/idx_seid` + compound indexes.
2. **`Worker.ExecuteAsync`** → `SetInitialData()` → `SetBetStopStatus()` reads the `BettingStatus`
   collection (model id `"betstop"`) to seed the in-memory `_IsBetStopGlobally` flag.
   → *Init dependency: Mongo read.*
3. `_messageBroker.Connect()` declares exchange/queue/bindings, sets QoS, starts the consumer.
   → *Init dependency: RabbitMQ.*
4. Subscribes the `MessageReceived` handler. Steady state begins.

> SQL `TranslationContext` is **not** touched at startup — it's lazily hit on the first
> cache-miss during message processing.

### OE.UOF.Client
1. `_platformRepository.ProcessMarketTemplatePlayerProps()` — HTTP GET to Platform API, cache 1y.
2. `_tradingRepository.GetClientConfigurations()` + `GetClientLiveFixtures()` — SQL `Trading` reads.
3. `_messageBrokerExternal.ExternalConnect()` (vhost `ext`), then `_messageBroker.Connect()` (internal).
4. Subscribe handler.
→ *Init dependencies: Platform API, SQL Trading, two RabbitMQ connections.*

### OE.Websocket.Service
1. Builds the SignalR `HubConnection` (MessagePack or JSON per `Custom:UseMessagePack`), `StartAsync`.
2. Spawns ~13 dedicated `Thread`s draining per-class `BlockingCollection`s.
3. `_messageBroker.Connect()` + subscribe.
→ *Init dependencies: platform-final `/platform` hub, RabbitMQ, MongoDB/Platform API for templates.*

### Adapter / Lookup / KeepAlive (event-driven or simple)
- Adapter: `Connect()` → subscribe. No DB.
- Lookup: `Connect()` → subscribe; registers `IMongoClient`.
- KeepAlive: `Connect()` → enter 5s publish loop.

### Timer-loop jobs (LiveStream / Settlement / Cleanup)
- No broker subscription (LiveStream/Cleanup) or publish-only (Settlement). `ExecuteAsync`
  enters a `while` loop with `Task.Delay`, gated by feature flags (`Custom:Enabled`,
  `Custom:EditSportsbook`) and (Cleanup) a UTC time window.

## 4. Startup dependency matrix

| Service | Needs at startup | Lazy / runtime deps |
|---|---|---|
| Sportsbook | MongoDB (index + betstop read), RabbitMQ | SQL Translation (first cache miss) |
| UOF.Client | Platform API, SQL Trading, RabbitMQ ×2 (internal+ext) | — |
| Websocket | `/platform` hub, RabbitMQ | MongoDB, Platform API |
| Adapter | RabbitMQ | — |
| Lookup | RabbitMQ, MongoDB | — |
| Settlement | MongoDB, RabbitMQ (publish) | — |
| LiveStream | MongoDB, BetGenius API | — |
| Cleanup | MongoDB | — |
| KeepAlive | RabbitMQ | — |

## 5. Hosted services

Each process registers exactly **one** `AddHostedService<Worker>()`. There are no secondary
`IHostedService`s, no `BackgroundService` fan-out within a process. Horizontal scale is achieved
by running **many instances of the same worker** (esp. Sportsbook 00–10), not multiple hosted
services per process. See [Hosting.md](./Hosting.md).

## 6. Startup failure behavior — [Verified] (uneven; see TechnicalDebt.md)

- **Sportsbook**: if Mongo is down, `MongoIndexBootstrapper` throws in `Main` → process exits
  (crash-loops under k8s until Mongo is up — acceptable fail-fast).
- **Broker connect**: `MessageBroker.GetConnection` retries 3× with backoff on transient
  (Timeout/IO/Socket) errors, else throws `RetryableConnectionException`.
- **UOF.Client `ProcessMarketTemplatePlayerProps`** is `async void` and swallows exceptions —
  a failed Platform API call at startup is **silent** (player-props cache simply empty).
- No explicit readiness/liveness probe endpoints exist in-repo (no HTTP). KeepAlive's heartbeat
  is the only liveness signal and it is **hardcoded healthy** — see [Logging.md](./Logging.md) +
  [TechnicalDebt.md](./TechnicalDebt.md).

## 7. Shutdown sequence — [Verified]

- Host receives SIGTERM → `Worker.StopAsync`. Most services just dispose the `MessageBroker`
  (closes channel/connection; in-flight unacked messages are redelivered — at-least-once).
- **Gaps:** KeepAlive/Cleanup/Settlement/LiveStream `while` loops and Websocket's manual threads
  do **not** observe the `CancellationToken`, so shutdown is not fully graceful. Detailed in
  [TechnicalDebt.md](./TechnicalDebt.md) (M6, H2).
