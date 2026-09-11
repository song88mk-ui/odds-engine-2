# Dependency Injection

How services are registered, their lifetimes, and the patterns used. All [Verified].
DI container = the default `Microsoft.Extensions.DependencyInjection` from the Generic Host.

## 1. Registration model

All registration happens in each service's `Program.cs` `ConfigureServices` (the composition
root). There are **no assembly-scanning / convention-based registrations**, **no Autofac/other
container**, and only one helper-extension pattern (LiveStream's `AddProviders()`,
`ServiceCollectionBettingExtensions.cs`). Everything else is explicit `services.AddX<I, Impl>()`.

## 2. Lifetimes — the dominant pattern is **Singleton**

| Lifetime | Used for | Why it works (or doesn't) |
|---|---|---|
| **Singleton** | `Instrumentation`, `IMessageBroker`, `IMemCache`, `IHelper`, all repositories, `IMongoClient`, `IMongoDatabase`, the `Worker` (via `AddHostedService`) | Long-lived worker process; broker/Mongo clients are designed to be shared & thread-safe. **Risk:** singleton repositories that hold mutable state must be thread-safe — some are not (see below). |
| **Scoped** | `TranslationContext` (via `AddDbContextPool`); LiveStream's `ILiveStreamProvider`, `ILiveStreamFactory`, `IBetGeniusRepository` (typed HttpClient) | DbContext must not be singleton (EF). |
| **Transient** | (none explicit) typed `HttpClient` handlers are managed by `IHttpClientFactory` | — |

> ⚠️ **Lifetime mismatch to be aware of (LiveStream).** `AddProviders()` registers the providers
> and factory as **Scoped** (`ServiceCollectionBettingExtensions.cs:14-17`), but the consumer is a
> **Singleton** `Worker` (hosted service). Resolving scoped services from a singleton requires the
> Worker to create an `IServiceScope` per iteration; if it instead captures them once, the
> "scoped" lifetime is effectively singleton. *Verify how the LiveStream Worker resolves them* —
> noted in [Unknowns.md](./Unknowns.md).

> ⚠️ **Singleton state hazards** (cross-ref [TechnicalDebt.md](./TechnicalDebt.md)):
> - `OE.Sportsbook.Service` singleton repo holds `_IsBetStopGlobally` (no synchronization) — H3.
> - `OE.UOF.Client` singleton Worker holds in-memory `_configurations`/`_fixtureConfigurations`
>   lists reset monthly by wall-clock — M12.

## 3. Per-service registration table

| Service | Telemetry | Broker | Cache | Data | HttpClient | Other |
|---|---|---|---|---|---|---|
| Adapter | `Instrumentation` + `AddTelemetry` | `IMessageBroker` | `ITranslationCache` | `AddDbContextPool<TranslationContext>` | — | `IHelper`, `ITradingRepository` |
| Sportsbook | ✅ | `IMessageBroker` | `IMemCache` | `IMongoClient`, `IMongoDatabase("Sportsbook")` ¹ | — | `IHelper`, `ISportsbookRepository` |
| Translation.Service | ✅ | `IMessageBroker` | `IMemCache`, `ITranslationCache` | `AddDbContextPool<TranslationContext>` | — | `IHelper`, `ITranslationRepository`, `IMongoTranslationRepository` |
| Translation.Job | ❌ | `IMessageBroker` | — | `AddDbContextPool<TranslationContext>` | — | `ITranslationCleanupRepository` (**Scoped** — the only non-Singleton repo) ², two `IHostedService`s (`BetPlacementWorker`, `CleanupWorker`) |
| UOF.Client | ✅ | `IMessageBroker` (internal+external use of same singleton) | `IMemCache` | (Trading via repo `new` context) | (Platform via repo) | `IClientRepository`, `IPlatformRepository`, `ITradingRepository` |
| Websocket | ✅ | `IMessageBroker` | `IMemCache` | (Mongo via repo) | (Platform via repo) | `ISportsbookRepository`; SignalR `HubConnection` built in Worker |
| Lookup | ❌ | `IMessageBroker` | — | `IMongoClient` | — | `ISportsbookRepository` |
| Settlement | ✅ | `IMessageBroker` | — | (Mongo via repo) | `AddHttpClient()` (default) | `ISportsbookRepository` |
| LiveStream | ✅ | — | `IMemCache` (in AddProviders) | (Mongo via repo) | `AddHttpClient<IBetGeniusRepository, BetGeniusRepository>()` | `AddProviders()` (factory+provider), `ISportsbookRepository` |
| Cleanup | ✅ | — | — | (Mongo via repo) | — | `ISportsbookRepository` |
| KeepAlive | ✅ | `IMessageBroker` | — | — | — | — |

> ¹ Sportsbook is **Mongo-only**: since the 2026-07 translation-flow redesign it registers no
> `TranslationContext` and no `ITranslationRepository` at all. `ConnectionStrings:Translation` is
> required by **Adapter, Translation.Service and Translation.Job** instead.
>
> ² `ITranslationCleanupRepository` is `AddScoped`, unlike every other repository in the solution
> (Singleton). It holds a `TranslationContext` with tracked writes, so it must not outlive a unit of
> work — its two hosted workers resolve it from a scope per pass.

## 4. Patterns

### 4.1 Interface + implementation pairing
Every repository/utility is `IXxx` + `Xxx` registered together. This is the only abstraction
seam — it enables (in principle) test substitution, though no test projects were found in-repo.

### 4.2 Factory / strategy via `IEnumerable<T>` (LiveStream only)
```csharp
services.AddScoped<ILiveStreamProvider, BetGeniusLiveStream>();  // could register many
services.AddScoped<ILiveStreamFactory, LiveStreamFactory>();
```
`LiveStreamFactory` receives `IEnumerable<ILiveStreamProvider>` and resolves by `ChannelType`
(`LiveStreamFactory.cs:16-24`). This is the idiomatic .NET "strategy registered as many, selected
at runtime" pattern — the one place the codebase is set up for multi-provider extensibility
(BG/EM/OD channels).

### 4.3 Typed `HttpClient` via `IHttpClientFactory`
`AddHttpClient<IBetGeniusRepository, BetGeniusRepository>()` (LiveStream) gives pooled,
properly-disposed handlers. UOF.Client / Websocket repositories instead construct/configure
`HttpClient` more manually (base address + `x-api-key`); UOF.Client uses a hardcoded 5s timeout.

### 4.4 DbContext pooling
`services.AddDbContextPool<TranslationContext>(o => o.UseSqlServer(conn, sql =>
sql.CommandTimeout(DbContextTimeout)))` (Sportsbook `Program.cs:48-52`). Pooling amortizes
context creation for the high-frequency translation lookups. CommandTimeout default 180s.

### 4.5 Mongo client as singleton
`IMongoClient` (singleton) → `IMongoDatabase` (singleton, `GetDatabase("Sportsbook")`). The
MongoDB driver's client is connection-pooled and thread-safe by design — singleton is correct.

### 4.6 Telemetry registration (uniform)
`AddSingleton<Instrumentation>()` + `AddTelemetry(new TelemetrySettings(ServiceName,
ActivitySourceName, MeterName))` from the private `Infra.Extensions.Telemetry` package. See
[Logging.md](./Logging.md).

## 5. Options pattern — [Verified] (essentially **not used**)

The codebase reads configuration **imperatively** via `IConfiguration` (`cfg["Section:Key"]`,
`cfg.GetValue<T>(...)`) inside services — it does **not** use the strongly-typed Options pattern
(`IOptions<T>` / `services.Configure<T>(...)`). The only `Configure`-style calls are framework
ones inside the host. If adding new config, follow the existing imperative convention for
consistency (or introduce Options deliberately and document it). See
[Configuration.md](./Configuration.md).

## 6. Things NOT present (so you don't go looking)
- No `IServiceScopeFactory` usage except Sportsbook's startup index bootstrap scope.
- No decorators, no `TryAdd*`, no keyed services, no `services.AddOptions().Bind()`.
- No MediatR/CQRS handlers, no AutoMapper.
- No middleware/auth/authz services (no web host) — see [MiddlewarePipeline.md](./MiddlewarePipeline.md).
