# Repository Patterns

How the repository layer is structured across odds-engine-2. The "repository" here is **"the thing a
Worker delegates persistence + message-shaping to"** — not strictly Fowler's repository (some do no
DB work). All [Verified].

## Conventions
- **One repository (or set) per service**, in `Repository/` or `Repositories/`, as an
  **`IXxxRepository` + `XxxRepository`** pair, registered **Singleton** in `Program.cs`
  (DbContext-backed ones still resolve pooled contexts per call).
- A repository owns **both** persistence *and* the transformation between wire messages and
  documents (`ToFixture`, `ToMarketAndSelectionChange`, `ToDBBettingStatus`, `PrepareInternal…`).
- Mongo collections are obtained once in a `PrepareDatabaseConnection()` ctor call and held as fields.
- Writes funnel through a private `BulkWrite<T>` helper; reads use `Builders<T>.Filter` + `Find`.

## Repository inventory & what each actually does
| Service | Repository | Store(s) | Role |
|---|---|---|---|
| **Sportsbook.Service** | `SportsbookRepository` | MongoDB (Fixture/Market/Selection/BettingStatus) | the real persistence repo: upserts catalogue, reads for projections, publishes derived messages |
| | `TranslationRepository` | SQL Translation + MemCache | cache-aside translation lookups (read-only) — **orphaned as of 2026-06-30: still DI-registered but never resolved/called by `SportsbookRepository`** |
| **UOF.Client** | `TradingRepository` | SQL Trading | read client feed config/live fixtures (`new` context per call) |
| | `PlatformRepository` | HTTP (Platform API) | fetch player-props; **not** a DB repo |
| | `ClientRepository` | none | odds math + transform + external publish |
| **Adapter.Service** | `TradingRepository` | none | **routing/transform only** (despite the name — no trading DB) |
| **Job.Lookup** | `SportsbookRepository` | MongoDB (`*Lookup`) | upsert lookup dimensions |
| **Job.LiveStream** | `SportsbookRepository` | MongoDB (Fixture) + BetGenius via `IBetGeniusRepository` | sync stream URLs |
| **Job.Settlement** | `SportsbookRepository` | MongoDB (Fixture read) | find stale fixtures, publish close |
| **Job.Cleanup** | `SportsbookRepository` | MongoDB (delete) | retention purge |
| **Websocket.Service** | `SportsbookRepository` | MongoDB read + MemCache | template/metadata reads for push shaping |
| **OE.Shared** | `BetGeniusRepository` | HTTP (BetGenius) + MemCache | provider auth + fixtures (read-through cache) |
| | `RabbitMQRepository` | HTTP | publish-via-HTTP bridge |

> **Naming caveat:** the class name doesn't imply the store. `TradingRepository` is a SQL reader in
> UOF.Client but a pure router in Adapter; `PlatformRepository` is an HTTP client. Read the
> implementation, not the name.

## The Mongo repository pattern (canonical — `SportsbookRepository`)
1. **Ctor** captures config/cache/helper/instrumentation/logger + `PrepareDatabaseConnection()`
   (news `MongoClient`, gets `Sportsbook` DB + 4 collection handles). [`:54, :340`]
2. **Process* methods** (one per message type) = read related docs → `BulkWrite` upsert(s) → publish
   derived message(s). [`:76-321`]
3. **To* helpers** build `WriteModel<T>[]` (UpdateOneModel, Set/SetOnInsert, IsUpsert). [`:351-575`]
4. **Prepare* helpers** build outgoing messages. (Corrected 2026-06-30: translations removed from
   pipeline — the `MapTranslations`/`LoadTranslationsAsync`/`AddTranslationKey`/`AddContentKey` calls and
   the `*Tr` lists were deleted, and `ITranslationRepository` is no longer injected; `Prepare*` no longer
   merges any translations.)
5. **BulkWrite<T>** central write + latency histogram. [`:578`]
6. **Read helpers**: `GetActiveMarketIds` (filter + projection), `SetBetStopStatus` (load global flag). [`:867-886`]

## EF repository pattern (`TranslationRepository`, `TradingRepository`)
- Inject the pooled `DbContext` (Sportsbook) **or** new one per call (UOF.Client — anti-pattern).
- **`AsNoTracking`** read-only queries, batched `Where(... Contains(ids) ...)`, `Select` into a DTO,
  `ToList`. No writes, no SaveChanges (OEv2 doesn't write SQL).
- Cache-aside layered on top (TranslationRepository) — see [Caching.md](./Caching.md).
- **(Corrected 2026-06-30: translations removed from pipeline.)** `TranslationRepository`/
  `GetTranslationsAsync` is now **orphaned** — `SportsbookRepository` no longer injects/calls it, so the
  pattern below survives only as `TradingRepository` (UOF.Client). `ConnectionStrings:Translation` and
  `Custom:Translation:UseContentKey` are now unused.

## Patterns observed
- **Interface seam** for every repo (testable in principle; no tests in-repo).
- **Transform-and-persist co-location** (mapping logic lives with persistence).
- **Read-through / cache-aside** for reference data.
- **No generic repository / no `IQueryable` leakage** — each repo exposes intent-named methods
  (`ProcessOnMarketChangeMessage`, `GetClientConfigurations`; `GetTranslationsAsync` still exists but is
  now uncalled — see 2026-06-30 correction above).
- **No unit-of-work / no SaveChanges batching** — Mongo writes are immediate bulk upserts.

## Inconsistencies / debt at the repository layer (see TechnicalDebt.md)
- `new TradingContext()` per call (UOF.Client) — M9.
- Dual Mongo client (DI singleton vs self-newed in SportsbookRepository) — minor.
- Mixed folder naming `Repository/` vs `Repositories/` across services.
- Class names that misdescribe the store (TradingRepository/PlatformRepository) — L10.
- Cleanup's per-document delete loop instead of `DeleteMany` — M7.
- Large commented-out blocks (translations to docs, favorite logic) inside the To*/Prepare* helpers.
