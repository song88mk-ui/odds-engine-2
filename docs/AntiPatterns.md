# Anti-Patterns (observed)

Recurring code-level anti-patterns **present** in odds-engine-2, documented factually so engineers
recognize them when reading/extending the code. This is the *pattern* view; the severity-ranked
*issue* inventory is [TechnicalDebt.md](./TechnicalDebt.md) (IDs referenced). All [Verified].

## Concurrency
- **Shared mutable state without synchronization.** `_IsBetStopGlobally` (bool) is read/written by a
  Singleton repository under concurrent message processing (prefetch 1000) with no lock/volatile —
  `SportsbookRepository.cs:48,220,232`. (TechDebt H3)
- **Wall-clock-driven Singleton state reset.** UOF.Client holds in-memory config/fixture lists reset
  monthly by `DateTime` checks inside a Singleton worker. (M12)
- **Unmanaged threads + hard exit.** Websocket.Service spawns ~13 `new Thread()` on
  `BlockingCollection` with no `CancellationToken`/join, and calls `Environment.Exit(1)` on error
  instead of nack/requeue. (H2)

## Error handling
- **Silent exception swallowing.** Empty `catch {}` with no log: Job.Lookup message handler
  (`Worker.cs:81-84`); UOF.Client `PlatformRepository` (`:43-45`). Failures become invisible. (M4)
- **`async void` fire-and-forget.** `PlatformRepository.ProcessMarketTemplatePlayerProps()` is
  `async void` — exceptions can't be caught by the caller and a startup failure is silent. (H5/M4)
- **`Environment.Exit(1)` as error handling** (Websocket) — bypasses graceful shutdown. (H2)
- **Loops that ignore the host `CancellationToken`** — `while(true)`/manual threads don't observe
  shutdown, so `StopAsync` can't drain them (KeepAlive/Cleanup/Settlement/LiveStream/Websocket). (M6)

## Persistence & caching
- **Unacknowledged writes (`w=0`) + unordered bulk** → write/partial-batch failures are silent.
  (P3)
- **Unindexed hot read.** Bet-settlement selection lookup filters on `PrFiId+MaTeId+SeTeId+SeSp`,
  covered by no index → potential collection scan per settlement. (P1)
- **Fragile TTL arithmetic.** `(int)TimeSpan.FromMinutes(n).TotalHours` truncates sub-hour TTLs to
  `0` (instant expiry); the cache TTL API takes **hours**. Works at 60 min only by luck. (P2)
- **`new DbContext()` per call.** UOF.Client `TradingRepository` constructs a context per method
  instead of pooling/injecting. (M9)
- **Per-document delete loop.** Job.Cleanup deletes fixtures one-by-one (`DeleteOneAsync`) instead of
  `DeleteMany`. (M7)
- **O(n) cache prefix purge.** `DeleteCacheByPrefix` scans every key in the process cache. (P7)
- **Dual Mongo client.** DI registers `IMongoClient`/`IMongoDatabase` (used only by index bootstrap)
  while `SportsbookRepository` news its own client. (P5)
- **Set-and-forget 1-year cache** for player props with no refresh path. (P6)

## Configuration & security
- **Committed live secrets.** Real RabbitMQ/BetGenius/SQL/API credentials in several
  `appsettings.json` (`.gitignore` excludes only `appsettings.Development.json`). (H1)
- **Magic numbers / hardcoded values.** Global bet-stop shard hardcoded `"10"`
  (`Adapter/TradingRepository.cs:75`); +2% margin hardcoded; sport-id→name as a hardcoded `switch`;
  BetGenius login path hardcoded `PROD` despite a configurable env. (L6 + Unknowns)
- **Hardcoded `IsHealthy = true`** heartbeat — a health signal that can't report unhealthy. (H4)

## Structure / dead code
- **Large commented-out blocks** as "disabled features" rather than removed or flagged: translation
  `.Set()`s never persisted, `UpdateFixtureConfiguration`, `PublishBetStopMessage`, validation,
  favorite-from-odds logic, period scores. Obscures actual behavior. (P4, L5, H5)
- **Dead project `OE.API`** (no code) and **`OE.Job.KeepAlive` absent from the `.sln`** but built by
  CI. (L1, L2)
- **Unused dependencies/helpers.** `IdGen` referenced unused (Sportsbook); status-mapper helpers
  never called (Adapter `TradingRepository.cs:95-113`); `Custom:ApiKey` unused (Settlement). (L3, L4)

## Naming / clarity
- **Misleading class names.** `TradingRepository` that does routing (Adapter) or SQL (UOF); 
  `PlatformRepository` that is an HTTP client — name ≠ responsibility/store. (L10)
- **File/class name mismatch** (`LoggerExtension.cs` → `LoggerExtensions`).
- **Typo in a published routing key** (`r.bg.receiver.onfixturecchange`) — risks an unbound consumer.
- **Lost correlation chains.** Settlement publishes use `Guid.NewGuid()` as correlation id instead of
  threading the incoming one. (M10)

## Logging
- **"Structured" logging that isn't.** `LogXxxEx` concatenates everything into one string (Serilog
  message-template properties are lost; only the JSON `Data:` suffix is structured).
- **String-interpolated error logs** (`BetGeniusRepository`) reduce queryability.
- **No trace-id enricher** in Serilog — a log line can't be tied to its trace without manual correlation.

## Consistency drift (team-level)
- Mixed `MongoDB.Driver` versions (3.4.0 vs 3.6.0), `OE.Job.Lookup` missing Serilog/telemetry,
  mixed `Nullable`/`ImplicitUsings`, Dockerfile `ENTRYPOINT`/`CMD` and output-path variance,
  two `Program.cs` styles. (TFMs and `Microsoft.Extensions.*` versions unified by GM-751.)

---
> **Note (per Phase-8 scope):** this documents what *exists* — it does not prescribe replacements.
> Many items are **intentionally-disabled in-progress features**, not bugs; confirm intent (see
> [Unknowns.md](./Unknowns.md)) before changing. Remediation priorities live in
> [TechnicalDebt.md](./TechnicalDebt.md).
