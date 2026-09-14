# Implementation Patterns

> **Supersedes the Phase 1 version** — this is the definitive catalog of the patterns this team uses
> in odds-engine-2, organized by the Phase-8 list. **Descriptive, not prescriptive** — it documents
> what exists so new code matches it. All [Verified] with file references. Cross-refs:
> [PersistencePatterns.md](./PersistencePatterns.md), [DependencyInjection.md](./DependencyInjection.md),
> [Logging.md](./Logging.md), [CodingStandards.md](./CodingStandards.md).

## Repository pattern
- Every service has a `Repository/`|`Repositories/` folder with **`IXxxRepository` + `XxxRepository`**
  pairs, registered **Singleton**. The repository owns **persistence + message transformation**
  (it is "the thing the Worker delegates to", not strictly Fowler's repository).
- Intent-named methods, no `IQueryable` leakage, no generic repository.
- Mongo collections captured once in `PrepareDatabaseConnection()`; writes via a private `BulkWrite<T>`
  helper; reads via `Builders<T>.Filter` + `Find`. *Canonical:* `SportsbookRepository.cs`.
- ⚠️ Class names don't imply the store (`Adapter`'s `TradingRepository` is a router; UOF's
  `PlatformRepository` is an HTTP client). Full inventory: [RepositoryPatterns.md](./RepositoryPatterns.md).

## Service pattern (hosted worker, not a "service layer")
- There is **no separate service/business-logic layer**. The unit is a hosted
  `BackgroundService`/`IHostedService` **`Worker`** that connects the broker, dispatches messages by
  type, and delegates to a repository. Business logic lives in the repository's `Process*`/`To*`/
  `Prepare*` methods.
- "Service"-suffixed projects (`OE.*.Service`) are deployable workers, not DI services.

## Factory pattern
- **`LiveStreamFactory : ILiveStreamFactory`** (`OE.Shared/Providers/`) — resolves an
  `ILiveStreamProvider` by `ChannelType` from a DI-injected `IEnumerable<ILiveStreamProvider>`
  (`FirstOrDefault(p => p.ChannelType == ...)`, throws `NotSupportedException`). The idiomatic
  ".NET strategy registered-as-many, selected-at-runtime" factory — the one extensibility seam.
- **`ConnectionFactory`** (RabbitMQ.Client) in `MessageBroker` — third-party factory for connections.
- **`MongoClient`** acts as the connection/collection factory (`GetDatabase`/`GetCollection`).

## Builder pattern
- **MongoDB fluent builders** are used pervasively: `Builders<T>.Filter`, `Builders<T>.Update`
  (`.Set`/`.SetOnInsert`), `Builders<T>.IndexKeys`, `Builders<T>.Projection`. This is the dominant
  "builder" usage. *Examples:* `SportsbookRepository.ToFixture/ToMarketAndSelectionChange`,
  `MongoIndexBootstrapper`.
- **Generic Host builder** (`Host.CreateDefaultBuilder`) + EF `DbContextOptionsBuilder`.
- No custom domain builder classes.

## Extension methods
Static `*Extensions` classes in `OE.Shared/Extensions/`:
- **`LoggerExtensions`** (`LoggerExtension.cs`) — `LogInformationEx/LogWarningEx/LogErrorEx<T>` using
  `[CallerMemberName]`/`[CallerFilePath]`/`[CallerLineNumber]` to emit
  `[Project.Class.Member (file:line)] message | Data: {json}`. The standard logging entry point.
- **`DateTimeExtension`** — `ToEpochTimestamp()` (ms since epoch via tick math), `ConvertToDateTime()`.
- **`IntExtensions`** — `GetBetStatus(this int settlementStatusId)` (SettlementStatus→BetStatus switch,
  throws on undefined), `IsVirtualSportId()` (true only for Virtual Cricket, sport 11205870).
- Also `ServiceCollection` extensions for DI wiring (`AddProviders` in Job.LiveStream;
  `AddClient*` in platform-web).
- Convention: `public static class XxxExtensions`, first param `this T`.

## Validators
- **No validation framework** (no FluentValidation/DataAnnotations validation pipeline). Validation is
  **inline guard clauses** (`if (message == null || ...) return;`, `if (!message.Ma.Any()) return;`).
- A bespoke `IsMessageValid`/`ProcessMessageWithValidation` exists in `OE.UOF.Client` but is
  **commented out / disabled** ([BusinessRules.md](./BusinessRules.md) BR-17). So the convention in
  practice is: **guard clauses, no formal validators.**

## Mappers
- **Manual mapping only** — no AutoMapper. Two idioms:
  - **`To*` methods** build Mongo `WriteModel<T>[]` from messages (`ToFixture`, `ToMarketAndSelection*`,
    `ToDBBettingStatus`).
  - **`Prepare*` methods** build outgoing messages from inputs (`PrepareInternalOddsChangeMessage`).
    They forward the inbound `*Tr` lists through `GateTranslations` rather than looking translations up
    — sourcing happens upstream in `OE.Adapter.Service`.
  - **`Map*`** for value projection. Note `MapTranslations` → `TranslationBase` was deleted in the
    2026-07 redesign; `TranslationBase` (`OE.Shared/Models/`) is now **orphaned** and should be removed.
  - **`To*Translations`** (`ToOutgoingTranslations` in Websocket, the `Select(t => new
    TranslationMessage {...})` projections in Sportsbook) convert between the Mongo `*NaTr` shape
    (`{LaCo, <Name>}`) and the wire `*Tr` shape (`{LaCo, Tx}`) — the two are deliberately different.
- DTO↔entity duplication is accepted (e.g. `ClientConfigurationModel` mirrors the EF entity).

## Helpers
- **`IHelper`/`Helper`** (`OE.Shared/Utilities`) — cross-service pure logic: `GetInstanceName`
  (sharding) + static `GetVoidFactor`. Registered Singleton where needed.
- Per-service private helper methods (`GetSortOrder`, `GetActiveMarketIds`, `AddTranslationKey`).
- Hardcoded lookups appear as `switch` helpers (e.g. sport-id→name in UOF.Client).

## Background services (hosted workers)
Two shapes (see [StartupFlow.md](./StartupFlow.md)):
- **Event-driven:** `Connect()` broker → subscribe `MessageReceived` → dispatch by
  `BasicProperties.Type`. (Adapter, Sportsbook, UOF.Client, Websocket, Lookup.)
- **Timer-loop job:** `while(...) { work; await Task.Delay(interval); }`, often gated by a
  `Custom:Enabled` flag and/or UTC window. (KeepAlive 5s, Settlement/Cleanup 5min, LiveStream 60s.)

## Event handling
- **RabbitMQ consumption is event-based**: `MessageBroker` exposes
  `event EventHandler<BasicDeliverEventArgs> MessageReceived`; `EventingBasicConsumer.Received`
  raises it; the Worker subscribes and dispatches. Ack/nack policy in `HandleMessage(WithManualAck)`.
- **OpenTelemetry trace context** is injected into / extracted from message headers around each
  publish/consume (propagator pattern).
- No in-process event bus / MediatR; the "event bus" is RabbitMQ itself.

## Logging
Serilog (`ReadFrom.Configuration`, Compact JSON to console) + the `LogXxxEx` extensions + per-message
OTel Activities with tags. Metrics via per-service `Instrumentation`. Full detail: [Logging.md](./Logging.md).
> Convention: `LogXxxEx` builds a single formatted string (loses Serilog structured properties — the
> "structured" part is the JSON `Data:` suffix, not message-template properties).

## Exception handling
- **Per-message try/catch** in the Worker handler: log (with payload + stack) → set Activity status
  Error → publish to an `r.error.*` routing key → nack/requeue (event-driven) or 5-min backoff (jobs).
- **Connection resilience**: `MessageBroker.GetConnection` retries 3× with exponential backoff on
  transient (Timeout/IO/Socket) errors, wraps in `RetryableConnectionException`.
- **Fail-fast startup**: Mongo index bootstrap throws → process crash-loops until Mongo is up.
- Anti-pattern variants exist (silent `catch {}`, `async void`, `Environment.Exit(1)`) — see
  [AntiPatterns.md](./AntiPatterns.md).

## Feature flags
- **Plain config booleans** (no FeatureManagement library): `Custom:Enabled`, `Custom:EditSportsbook`,
  `Custom:IsOEEnabled`, `Custom:UseMessagePack`, `Custom:AsianClient`, `MessageBroker:IsExclusive/
  IsPriority`, `Telemetry:Enabled`. Read via `GetValue<bool>(key, default)`. Full list:
  [Configuration.md](./Configuration.md).

## Cross-cutting patterns (also see other docs)
- **Sticky-shard routing** (`GetInstanceName`) for per-fixture ordering — the defining architectural
  pattern ([Architecture.md](./Architecture.md) ADR-003).
- **Idempotent upsert + Set/SetOnInsert** persistence ([PersistencePatterns.md](./PersistencePatterns.md)).
- **Cache-aside + negative caching** ([Caching.md](./Caching.md)).
- **Trace propagation through messages** ([Logging.md](./Logging.md)).
