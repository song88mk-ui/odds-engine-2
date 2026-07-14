# Persistence Patterns

The recurring persistence patterns across odds-engine-2 — follow these when adding code. All
[Verified]. See [MongoDb.md](./MongoDb.md), [Database.md](./Database.md), [Caching.md](./Caching.md),
[RepositoryPatterns.md](./RepositoryPatterns.md).

## 1. Polyglot persistence (right store per job)
- **MongoDB** — hot, high-churn catalogue (fixtures/markets/selections), schema-flexible, upsert-heavy.
- **SQL Server (EF 3.1)** — relatively static relational reference data (translations, client feed
  config); **read-only** from OEv2 (backoffice writes).
- **In-process MemoryCache** — translations + provider lookups; never the source of truth.
- **RabbitMQ** — not persistence per se, but durable (`DeliveryMode=2`) in-flight message transport.
No store spans a transaction with another (see §7).

## 2. Idempotent upsert by natural key
Every Mongo write is `UpdateOneModel` with a filter on the natural id (`_id`/`FiId`/`MaId`/`SeId`) and
`IsUpsert=true` for create-or-update paths. Combined with at-least-once delivery, **re-processing a
message is safe** (same upsert → same state).

## 3. `Set` vs `SetOnInsert` (immutable identity, mutable state)
Natural keys are written with **`SetOnInsert`** (e.g. `FiId`/`MaId`/`SeId`) so they're fixed at
creation; all mutable fields use **`Set`** (odds, status, probabilities, provider ids, `CoId`).
Conditional sets for optional values (e.g. score only when non-empty, favorite only when not None).
*Example:* `ToFixture` (`SportsbookRepository.cs:351`), `ToMarketAndSelectionChange` (`:458`).

## 4. Unordered bulk writes
All catalogue writes funnel through one `BulkWrite<T>` helper using
`BulkWriteOptions { IsOrdered = false }` (max parallelism within a batch) and record latency to the
`bulkwrite.duration` histogram. Trade-off: partial failures don't stop the batch and (with `w=0`)
aren't individually surfaced — see §8.

## 5. Read-modify-publish (not read-modify-write)
The dominant flow is: **read related docs → write the change → publish a derived message**, rather
than read-modify-write loops. E.g. `ProcessOnFixtureChangeMessage`: read active/suspended
markets+selections → upsert the fixture → build & publish `OnOddsChange`. The published message is a
*projection* of current state (with translations merged in from cache), not a DB round-trip for readers.

## 6. Status-only settlement writes
Settlement upserts touch **only status fields** (`MaStId`/`SeStId`), leaving odds intact
(`ToMarketAndSelection`, `:541`, `IsUpsert=false`). Settlement *outcomes* are published per selection
to `r.oe.bet.settlement`; the bet-level result is composed downstream (platform).

## 7. No distributed transactions / unit-of-work
There is **no transaction** spanning Mongo + SQL + RabbitMQ, and no Mongo multi-document transaction.
A handler may upsert Mongo **and** publish a message; if the publish fails after the write (or vice
versa), there is no rollback — the message is nack'd/requeued and the **idempotent upsert** makes
re-processing safe. This is the **"dual write + idempotent retry"** pattern, not transactional outbox.
Consequence: a brief window where Mongo is updated but the downstream message hasn't been delivered.

## 8. Throughput-over-durability write concern
The Mongo connection uses **`w=0`** (unacknowledged). Writes are fire-and-forget for speed; failures
are largely invisible. Acceptable for a high-volume, continuously-refreshed catalogue (the next tick
overwrites), but means **no per-write durability guarantee** — a design choice worth knowing.

## 9. Cache-aside + negative caching for reference reads
SQL translations are read cache-aside (check MemCache → batch SQL on miss → store, incl. empty
results) — see [Caching.md](./Caching.md). Provider lookups (BetGenius) are read-through with TTL.

## 10. Per-fixture serialization via sharding (consistency without locks)
There is no optimistic concurrency / version field on documents. Cross-instance write races are
avoided structurally: the **sticky shard** (`GetInstanceName`) routes all of a fixture's events to
one Sportsbook instance, so its documents are written by a single consumer in arrival order
(per-fixture). Across fixtures there's no global ordering. (Exception: `_IsBetStopGlobally` in-memory
flag is shared mutable state and **not** synchronized — [TechnicalDebt.md](./TechnicalDebt.md) H3.)

## 11. Denormalize-on-write, no joins on read
Sport/segment/league dimensions are **denormalized** onto every Market/Selection document (no
referential integrity, no `$lookup` joins). Lookups are maintained as separate materialized
collections (Job.Lookup). Readers get a self-contained document. Trade-off: a renamed sport/league
must be re-propagated to many docs (handled by the continuous feed re-writing them).

## 12. Connection management
- Mongo: `MongoClient` is connection-pooled & thread-safe; held as a field per repository (Sportsbook
  news its own; others via DI singleton). One client per process.
- SQL: `TranslationContext` is **pooled** (`AddDbContextPool`); `TradingContext` is **newed per call**
  in UOF.Client (anti-pattern — M9).
- MemoryCache: process-wide `Default` instance.

## Anti-patterns present (don't copy — see TechnicalDebt.md)
- `new TradingContext()` per call (UOF.Client) instead of pooled/injected (M9).
- Per-document `DeleteOneAsync` loop in Cleanup instead of `DeleteMany` (M7).
- TTL-in-hours truncation in the cache call (Caching.md).
- Unindexed settlement selection lookup (MongoDb.md).
- `w=0` + unordered bulk = silent write failures.
- Dual Mongo client wiring (injected vs self-newed) in Sportsbook.
