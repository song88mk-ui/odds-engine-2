# Coding Standards (observed)

> **Supersedes the Phase 1 version.** Descriptive — the conventions actually present in the code +
> the `.editorconfig`. Match them when contributing. Naming specifics: [NamingConventions.md](./NamingConventions.md);
> patterns: [ImplementationPatterns.md](./ImplementationPatterns.md). All [Verified].

## Enforced by `.editorconfig` (`src/OddsEngine/.editorconfig`)
- 4-space indent (tab width 4), **CRLF** line endings.
- C# namespace/null-handling style rules.
- **`CS8601` (possible null reference assignment) disabled.**

## Project / file layout standards
- Per-service skeleton: `Program.cs`, `Worker.cs`, `Repository|Repositories/`,
  `Telemetry/Instrumentation.cs`, `Messages/` (service-local), `appsettings.json`, `Dockerfile`.
- Interface + impl co-located (`IXxx.cs` next to `Xxx.cs`).
- Shared contracts/models/enums/utilities live in `OE.Shared` only.

## Language usage
- `ImplicitUsings` + `Nullable` enabled in most services (not Job.Cleanup/Job.Lookup — inconsistent).
- Two `Program.cs` styles coexist: top-level statements and classic `class Program` — not standardized.
- Async: `async Task` for the host/`Main` where used; **avoid `async void`** (it appears as an
  anti-pattern — see [AntiPatterns.md](./AntiPatterns.md), not a standard to follow).
- LINQ used freely for in-memory transforms; EF queries use **`AsNoTracking`** (read-only).

## DI standards
- Constructor injection; **Singleton** lifetimes for workers/repos/broker/cache/helpers; pooled
  DbContext (`AddDbContextPool`); `IMongoClient`/`IMongoDatabase` singletons.
- **Imperative `IConfiguration` access** (`cfg["A:B"]`, `GetValue<T>(key, default)`) — the **Options
  pattern is not used**. Follow the imperative convention for consistency.

## Messaging standards
- Topic exchange `e.central`; lowercase dotted routing keys; `|`-separated multi-binds.
- Publish always sets `DeliveryMode=2`, `Type` (= a `MessageType` constant), `CorrelationId`, and
  injects OTel trace headers. Use `CPublish` from multi-threaded code.
- Dispatch consumers on `BasicProperties.Type`.

## Persistence standards
- Mongo writes via the shared `BulkWrite<T>` helper, `UpdateOneModel` upserts, `IsOrdered=false`;
  natural keys with `SetOnInsert`, mutable fields with `Set`. Reads via `Builders<T>.Filter`.
- No transactions/unit-of-work across stores; idempotent retry instead. (See
  [PersistencePatterns.md](./PersistencePatterns.md).)

## Logging standards
- Serilog + the `LogXxxEx` extension helpers (carry class/member/file/line + optional JSON `Data`).
- Prefer message templates over interpolation (mixed adherence in practice).
- Per-service `Instrumentation` for metrics; per-message Activities for traces.

## Error-handling standards
- Wrap each message handler in try/catch → log + Activity-error + `r.error.*` publish + nack/requeue.
- Use the broker's retry/backoff for connections; fail-fast on startup dependencies (Mongo indexes).

## Config / secrets standards
- Baked `appsettings.json` defaults overridden by argocd env (`__`) + AWS Secrets Manager in prod.
- Feature behavior gated by `Custom:*` boolean flags.
- ⚠️ Real secrets must **not** be committed (several currently are — [TechnicalDebt.md](./TechnicalDebt.md) H1).

## CI / packaging standards
- Multi-stage Dockerfile (SDK→aspnet), strip `appsettings.*.json` at publish, pull AWS RDS cert
  bundle (Mongo/RDS TLS), GitHub Packages auth via `NUGET_PASSWORD`.
- `OE.Shared` published as `<Version>-${GITHUB_RUN_NUMBER}`; services built per-directory matrix.

## Documented inconsistencies (be aware; don't propagate)
Mixed TFMs (net8/net9), mixed `Microsoft.Extensions.Hosting`/`MongoDB.Driver` versions, `OE.Job.Lookup`
missing Serilog/telemetry, mixed `Nullable`/`ImplicitUsings`, Dockerfile `ENTRYPOINT`/`CMD` variance.
When adding a service, prefer the **most modern, consistent** choices already present (net8.0+,
Serilog+telemetry, nullable on). Full list: [TechnicalDebt.md](./TechnicalDebt.md).
