# Best Practices (observed)

The practices this team **consistently does well** in odds-engine-2 — the positive conventions worth
preserving and replicating in new code. Descriptive (what exists), all [Verified]. The flip side is
[AntiPatterns.md](./AntiPatterns.md).

## Architecture & messaging
- **Idempotent-by-design writes.** Upsert-by-natural-key (`SetOnInsert` for immutable ids) makes
  at-least-once redelivery safe — a genuine strength under the nack/requeue model.
- **Deterministic sticky sharding** for per-fixture ordering without distributed locks
  (`Helper.GetInstanceName`) — clean solution to concurrent catalogue writes.
- **One responsibility per worker**, coordinated only by the routing-key namespace — strong fault
  isolation and independent scaling (esp. Sportsbook 00–10, per-client Websocket/UOF instances).
- **Dedicated external vhost (`ext`)** isolates white-label egress from internal topology.
- **Reserved shard (10) for global bet-stop** avoids broadcasting to all shards.

## Observability
- **End-to-end distributed tracing**: W3C trace context injected into / extracted from RabbitMQ
  headers, so one trace spans the whole pipeline (Adapter→Sportsbook→Websocket). Consistently applied.
- **Per-service telemetry** (`Instrumentation` with ActivitySource/Meter + counters/histograms),
  uniform bootstrap via `AddTelemetry`. `bulkwrite.duration` histogram on the hot write path.
- **Structured, located logging** via `LogXxxEx` (auto class/member/file:line + JSON data).
- **Compact JSON logs** to stdout → container-log/observability friendly.

## Resilience
- **Connection retry/backoff** for RabbitMQ (`GetConnection`, 3× exp backoff on transient errors) +
  native auto-recovery (`NetworkRecoveryInterval`, `RequestedHeartbeat`).
- **Fail-fast startup** (Mongo index bootstrap) — surfaces missing infra immediately, crash-loops
  until healthy rather than running degraded.
- **At-least-once with manual ack** (ack on success, nack+requeue on throw) — no silent message loss
  on the happy/retryable paths.
- **Negative caching** of missing translations prevents SQL stampedes.

## Configuration & deployment
- **Config externalized for prod** via argocd env (`__`) + **AWS Secrets Manager via External
  Secrets** — the intended/correct secret mechanism (committed secrets are the exception/debt, not
  the design).
- **Feature flags** (`Custom:Enabled`/`EditSportsbook`/`UseMessagePack`/…) gate risky behavior and
  enable per-env/per-client toggling without code changes.
- **GitOps with changed-file CI** (build only what changed) + image-updater continuous deploy.
- **OIDC for AWS** (no static cloud keys in CI).
- **DocumentDB/RDS TLS** handled (AWS cert bundle pulled in Dockerfiles).

## Code structure
- **Consistent per-service skeleton** — new services are trivial to scaffold by copying one.
- **Interface seams** on every repository/utility (substitutable, testable in principle).
- **Shared kernel (`OE.Shared`)** keeps the wire/DB contracts single-sourced across OEv2 and
  bet-genius (NuGet) — prevents serialization drift between producer and consumers.
- **Strategy/factory for providers** (`ILiveStreamProvider` + `LiveStreamFactory`) — the live-stream
  channel set is genuinely extensible.
- **Payload minimization** via the abbreviated field schema — smaller messages, smaller Mongo docs,
  smaller SignalR frames.

## Data
- **Polyglot persistence** matched to workload (Mongo for hot catalogue, SQL for relational reference).
- **Pooled DbContext** + **`AsNoTracking`** reads for the high-frequency translation lookups.
- **Cache-aside** with explicit invalidation messages for translations.
- **Bootstrapped indexes** (unique on natural keys, compound on common query shapes) created at startup.

## Cross-repo discipline
- **Clear ownership boundaries**: OEv2 owns the Mongo catalogue (writes); platform reads it; backoffice
  owns SQL config. The "never write the other's store" rule is largely honored (platform never writes
  Mongo).
- **Contract-first thinking**: shared message/DB/enum shapes are explicit in `OE.Shared`.

> These are the patterns to **keep doing**. Where the codebase deviates from its own good practices
> (silent catches, committed secrets, thread-unsafe shared state), those are catalogued separately in
> [AntiPatterns.md](./AntiPatterns.md) and [TechnicalDebt.md](./TechnicalDebt.md).
