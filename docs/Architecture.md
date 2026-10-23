# Architecture

## Architecture style — [Verified] (High confidence)

`odds-engine-2` is a **message-driven micro-worker architecture**:

- A set of **independent .NET console/worker processes**, each a single deployable
  container, each owning one stage of an odds-processing pipeline.
- Coordinated **asynchronously through a RabbitMQ topic exchange** (`e.central`) — there are
  no direct service-to-service RPC calls *within* OEv2. Services communicate only by
  publishing/consuming messages.
- **Shared-kernel** library `OE.Shared` carries the message contracts, DB models, enums,
  and the messaging/cache utilities used by every service (netstandard2.1).
- **Polyglot persistence**: MongoDB is the operational sportsbook catalogue; SQL Server
  holds translations and client trading configuration.

There is **no API gateway, no CQRS mediator, no domain/DDD layering** inside OEv2. Each
worker is a thin `BackgroundService` → repository → (broker | database) stack. The design
optimizes for **throughput and horizontal sharding** of a high-volume real-time feed.

Evidence: every project is `OutputType=Exe` hosting `IHostedService`/`BackgroundService`
workers (`*/Program.cs`, `*/Worker.cs`); the shared broker is
`OE.Shared/Utilities/MessageBroker.cs`; the contracts live in `OE.Shared/Messages/*`.

## Runtime topology

| Service | Role | Continuous/Job | Consumes | Publishes / Writes |
|---|---|---|---|---|
| **OE.Adapter.Service** | Normalizer + sticky-shard router | Continuous | `r.uof.#`, `r.bg.receiver.#` | `r.oe.adapter.<type>.<NN>` |
| **OE.Sportsbook.Service** | Catalogue writer + odds-change publisher | Continuous (sharded 00–10) | `r.oe.adapter.#.<NN>` | MongoDB Sportsbook; `r.oe.sb.engine.on_odds_change`, `r.oe.bet.settlement` |
| **OE.UOF.Client** | External/white-label feed egress | Continuous | `r.oe.adapter.*.*`, `r.oe.uof.heartbeat`, `r.of.recovery.#.tso` | **External** broker (vhost `ext`): `uof.onoddschange.*`, `uof.onmarketsettlement.*`, … |
| **OE.Websocket.Service** | Real-time SignalR push bridge | Continuous | `r.oe.sb.engine.*`, `r.bet.*`, `r.wallet.*` | SignalR `/platform` hub (client); MongoDB reads |
| **OE.Job.Lookup** | Lookup-table maintainer | Continuous (event-driven) | `r.oe.adapter.onmarketchange.#` | MongoDB `*Lookup` collections |
| **OE.Job.LiveStream** | Livestream URL sync | Job (60s loop) | — | BetGenius Video API (HTTP) → MongoDB `Fixture` |
| **OE.Job.Settlement** | Stale-fixture closer | Job (5min loop) | — | reads MongoDB; publishes fixture-close `OnFixtureChange` |
| **OE.Job.Cleanup** | Retention purge + orphan sweep | Job (5min loop, UTC window) | — | deletes from MongoDB |
| **OE.Job.BoostedOddsCap** | Price boost cap alerts | Continuous (event-driven, single replica) | `r.oe.sb.engine.onoddschange` | `r.oe.bm.engine.notifications` (back-office toast); optional SMTP email; reads SQL `Trading` |
| **OE.Job.KeepAlive** | Heartbeat publisher | Job (5s loop) | — | `r.oe.uof.heartbeat` |

Detailed per-service behavior: [ProjectOverview.md](./ProjectOverview.md).

## The core data flow — [Verified] for OEv2 hops; [Verified-X] for cross-repo hops

1. **Ingress.** `bet-genius` (and [Likely] an `oddsfeed`/UOF source) publish provider-normalized
   messages to `e.central` with routing keys `r.bg.receiver.*` / `r.uof.*`.
   (`OE.Adapter.Service` argocd routing key = `r.bg.receiver.#|r.uof.#`.)

2. **Normalize + shard.** `OE.Adapter.Service` deserializes each message into the shared
   `Outgoing*Message` contracts, then re-publishes to `r.oe.adapter.<type>.<NN>` where
   `<NN>` is a **2-digit instance** computed from the **provider fixture ID**
   (`OE.Shared/Utilities/Helper.cs:8` `GetInstanceName`). Global bet-stop is hardcoded to
   instance `10` (`OE.Adapter.Service/Repositories/TradingRepository.cs:75`).

3. **Persist (sharded).** `OE.Sportsbook.Service` runs as **11 instances (00–10)**; each binds
   `r.oe.adapter.#.<NN>` and owns a disjoint slice of fixtures. It upserts
   Fixture/Market/Selection/BettingStatus into MongoDB via unordered `BulkWrite`, enriches
   names with SQL-backed translations (MemCache, 60-min TTL), then publishes the canonical
   internal `OnOddsChange` to `r.oe.sb.engine.on_odds_change` and per-selection settlements
   to `r.oe.bet.settlement`.

4. **Lookups.** In parallel, `OE.Job.Lookup` consumes market-change messages and maintains
   denormalized `SportLookup`/`SegmentLookup`/`LeagueLookup`/`MarketLookup`/`SelectionLookup`
   collections (for UI filters/dropdowns).

5. **External egress.** `OE.UOF.Client` consumes the normalized adapter stream and republishes
   a **Unified Odds Feed** to a *separate* RabbitMQ broker/vhost (`ext`) with client-specific
   margin and Asian odds-format conversions — for white-label client `TSO`.

6. **Real-time push.** `OE.Websocket.Service` consumes `r.oe.sb.engine.*` plus
   bet-status/wallet/cashout keys, and as a **SignalR client** invokes hub methods
   (`PublishOddsChangeForMainMarkets`, `PublishBetStatus`, `PublishWalletChange`,
   `PublishCashOutChange`, `PublishBetStop*`, `PublishPayableChange`) on the
   `platform-final` `/platform` hub.

7. **Consumption.** `platform-final` broadcasts hub messages to subscribed groups and also
   **reads** the Sportsbook MongoDB for its REST API (it never writes Mongo). `platform-web`
   (Blazor) is the SignalR + REST client that renders to the browser.

## Layer boundaries & dependency direction — [Verified]

```
              ┌────────────────────────────────────────────┐
   every →    │  OE.Shared  (netstandard2.1 shared kernel)  │  ← depends on nothing in-repo
   service    │  contracts · DB models · enums · broker ·    │
              │  memcache · helpers · providers              │
              └────────────────▲───────────────────────────┘
                               │ ProjectReference (all 11 workers)
   ┌───────────────────────────┴───────────────────────────┐
   │  Worker (BackgroundService)                            │
   │     └─ Repository (per service)                        │
   │           └─ IMessageBroker / IMongoClient / DbContext │
   └────────────────────────────────────────────────────────┘
```

Dependencies point **inward** to `OE.Shared`. No worker references another worker. The only
cross-service coupling is the **message contract** (the `Outgoing*Message` shapes) and the
**RabbitMQ routing-key namespace**. This is the system's true API surface.

## Communication patterns — [Verified]

- **Topic routing.** Exchange `e.central` is declared `topic`, durable
  (`MessageBroker.cs:175`). Routing keys are hierarchical & lowercased on bind
  (`MessageBroker.cs:202-203`); `|`-separated keys allow one consumer to bind many patterns.
- **At-least-once delivery.** Manual ack: success → `BasicAck`; exception → `BasicNack` with
  `requeue=true` (`MessageBroker.cs:332-344`). `PrefetchCount` default 1000.
- **Distributed tracing.** OpenTelemetry trace context is **injected into / extracted from**
  RabbitMQ `BasicProperties` headers (`MessageBroker.cs:391`, propagator pattern), so a trace
  spans the whole pipeline. Each service registers its own `ActivitySource`/`Meter` via
  `Telemetry/Instrumentation.cs` and the `Infra.Extensions.Telemetry` package.
- **Exclusive/priority queues.** Optional per-config: exclusive queues get an MD5-of-hostname
  suffix (`MessageBroker.cs:131-142`); priority queues set `x-max-priority=10`.

## Sharding model — [Verified] (the single most important architectural concept)

`Helper.GetInstanceName(providerFixtureId)` (`OE.Shared/Utilities/Helper.cs:8-23`):

```csharp
int last       = providerFixtureId[^1] - '0';   // last digit
int secondLast = providerFixtureId[^2] - '0';   // second-to-last digit
int baseDigit  = (last / 2) * 2;                 // 0,2,4,6,8
int result     = secondLast % 2 == 0 ? baseDigit : baseDigit + 1;  // 0..9
return result.ToString("D2");                     // "00".."09"
```

- Produces a **deterministic, sticky** instance `00`–`09` from the **last two digits** of the
  provider fixture id → all events for a given fixture always land on the same Sportsbook
  instance (no cross-instance races on a fixture's documents).
- Instance **`10`** is **reserved for global bet-stop** (hardcoded
  `TradingRepository.cs:75`), so the production fleet is **11 instances (00–10)**, matching
  argocd (`dev-00`…`dev-10`). The committed dev `appsettings.json` stub binds `.11` — that is
  a local-only value; production routing is set by argocd env vars.

Implications for anyone changing this: the digit math **must** stay consistent between
`OE.Adapter.Service` (router) and `OE.Sportsbook.Service` (consumer binding), and the instance
count is coupled to it. See [ArchitectureDecisions.md](./ArchitectureDecisions.md) ADR-003.

## Cross-cutting concerns — [Verified]

| Concern | Mechanism |
|---|---|
| Logging | Serilog, `ReadFrom.Configuration`, Compact JSON formatter to console (→ container logs). Extension helpers `LogInformationEx/ErrorEx/WarningEx` in `OE.Shared/Extensions/LoggerExtension.cs`. |
| Telemetry | OpenTelemetry traces + metrics via `Infra.Extensions.Telemetry`; OTLP/protobuf to Tempo (argocd env). Per-service counters (`processed.messages`, `forwarded.messages`) and histograms (`bulkwrite.duration`). |
| Config | `Microsoft.Extensions.Configuration`; `appsettings.json` baked into image but **overridden by argocd env vars** (`__`-delimited) + AWS Secrets Manager via External Secrets. |
| Caching | In-process `System.Runtime.Caching.MemoryCache` wrapper (`OE.Shared/Utilities/MemCache.cs`). |
| Resilience | RabbitMQ auto network recovery + custom retry/backoff on connect (`MessageBroker.GetConnection`, `MessageBroker.cs:546-593`). |

## Notable architectural risks (see TechnicalDebt.md for full list)

- **`OE.Job.KeepAlive` always reports `IsHealthy=true`** — heartbeat is a liveness ping, not a
  real health check.
- **`OE.Websocket.Service` spawns ~13 unmanaged threads** with `BlockingCollection` and no
  cancellation/join on shutdown; errors call `Environment.Exit(1)`.
- **`_IsBetStopGlobally` flag is read/written without synchronization** under concurrent
  message processing in `OE.Sportsbook.Service`.
- **Committed secrets** in `appsettings.json` for several services (mitigated in prod by
  argocd secret injection, but live credentials are in git history).
