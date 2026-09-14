# Database — SQL Server

odds-engine-2 uses **SQL Server via EF Core 3.1** for two **read-mostly reference databases**. It is
**not** the catalogue store (that's MongoDB — [MongoDb.md](./MongoDb.md)). All [Verified] unless tagged.

## Overview
| DB | Context | Used by | Access | Writer |
|---|---|---|---|---|
| **Translation** | `TranslationContext` | OE.Adapter.Service (read), OE.Translation.Service (read+write), OE.Translation.Job (write) | **read/write** | OE.Translation.Service + the external translation pipeline |
| **Trading** | `TradingContext` | OE.UOF.Client | **read-only** | backoffice-final |

> OEv2 **reads** SQL config (Trading); the **writer is backoffice-final**. `Translation` is different:
> since the **2026-07 translation-flow redesign** OEv2 both reads **and writes** it — `OE.Translation.Service`
> seeds/updates rows (`SaveChangesAsync`) and `OE.Translation.Job` issues raw deletes/updates. The
> "OEv2 doesn't write SQL" rule now applies to **Trading only**. (see
> [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md), [OddsEngineIntegration.md](./OddsEngineIntegration.md)).
> EF Core is pinned to **3.1 (EOL)** by the netstandard2.1 `OE.Shared` target ([TechnicalDebt.md](./TechnicalDebt.md) M1).

## Translation DB — `TranslationContext` (`OE.Shared/Data/Translation/`)
> **2026-07 translation-flow redesign.** `TranslationCache2` is the single translation store **that
> OEv2 uses**: one flat row per `STRING_ID`, with a fixed `TARGET_TEXT_*` column per language.
> `OE.Sportsbook.Service` no longer touches this context at all (it is not registered there); the
> consumers are `OE.Adapter.Service`, `OE.Translation.Service` and `OE.Translation.Job`.
>
> ⚠️ **"Retired" means retired from OEv2's code, not dropped from the database.**
> `SportsbookDynamic` and `SportsbookGlossary` still exist in the `Translation` schema — no migration
> drops them (`infrastructure/flyway/migrations/*/V1__Baseline.sql`). Expect to find them, with data,
> if you look. OEv2 simply no longer reads or writes them.

> **Schema source of truth: the `infrastructure/flyway` repo**, not this one. OEv2 ships **no**
> migrations and no DDL for any SQL table — the EF models here are hand-written mappings onto a schema
> owned elsewhere, so they can and do drift (see the index note below). Cross-check
> `infrastructure/flyway/migrations/<env>/` before trusting an EF model as a schema description.
>
> **The `Translation` database is shared.** Besides the two tables below it holds
> `SportsApiTranslationCache` (a newer, unrelated 8-language table — `zh, pt_br, th, id, ko, ja, vi,
> pt` — added in `V2__SportsApiTranslationCache_Table.sql`, **not** referenced anywhere in OEv2),
> plus `SportsbookStatic` and `BackofficeStatic`. Do not read "the Translation DB" as "OEv2's DB".

### `TranslationCache2` — the live store
- **DbSet:** `TranslationCache2` → table **`TranslationCache2`** (`TranslationContext.cs:13`).
- **Columns** (Fluent API, `TranslationContext.cs:19-92`): `ID` (PK, `PK_TranslationCache2`),
  `STRING_ID` (≤200, required), `SOURCE_LANGUAGE` (≤10, required), `SOURCE_TEXT` (≤250, required),
  `TARGET_TEXT_ZH` / `TARGET_TEXT_ES` / `TARGET_TEXT_FR` (≤250, required — empty string, not NULL,
  means "not translated yet"), `HAS_BET_PLACEMENT`, `CREATED_AT`, `PROCESSED`, `UPDATED_AT`.
- **Indexes — the DB has three; the EF model declares only one:**
  | Index | Keys | Purpose |
  |---|---|---|
  | `UX_TC2_STRING` | `STRING_ID` (unique) | OEv2's lookup key — **the only one EF knows about** |
  | `IX_TC2_SOURCE_TEXT` | `SOURCE_TEXT, SOURCE_LANGUAGE` INCLUDE (`STRING_ID`, `TARGET_TEXT_*`) | the external pipeline's dedup/lookup-by-text path |
  | `IX_TC2_UNPROCESSED` | `ID` filtered `WHERE PROCESSED = 0` | the external pipeline's worklist scan |
  The last two exist only in flyway's baseline. They are **load-bearing for the external pipeline** —
  don't "clean up" an index just because `TranslationContext` doesn't mention it.
- ⚠️ **`STRING_ID` collates `Latin1_General_CI_AS` (case-insensitive)**, and `UX_TC2_STRING` is unique
  over it — so two keys differing only in case are the **same row**. Relevant to `MaTe:` keys, whose
  identity is computed from market-template wording (`MarketTemplateVariantId.Compute`).
- **Who writes what** (the ownership split matters — see `TranslationContext.cs:20-26`):
  | Columns | Owner |
  |---|---|
  | `STRING_ID`, `SOURCE_*`, `PROCESSED`, `CREATED_AT`/`UPDATED_AT` | `OE.Translation.Service` |
  | `HAS_BET_PLACEMENT` | `OE.Translation.Job` (on `r.bet.placement`) |
  | `TARGET_TEXT_*` | the **external** translation pipeline only |
- **Read pattern:** `OE.Adapter.Service` preloads the **whole table** read-only at startup into its
  in-process `TranslationCache` (`OE.Adapter.Service/Repositories/TranslationCache.cs:67-73`) — there
  is no per-message SQL read on the hot path any more.
- **`STRING_ID` format:** `"{Prefix}:{Id}"` — `SpId:`, `SegId:`, `LeId:`, `Te:` (team), `MaTe:`
  (market template, per wording), `MaTeP:` (player-placeholder template), `SeTe:` (selection template).
- **Registration:** `AddDbContextPool<TranslationContext>` with `UseSqlServer(conn,
  CommandTimeout=Custom:DbContextTimeout (180s))` in `OE.Adapter.Service/Program.cs:40`,
  `OE.Translation.Service/Program.cs:47`, `OE.Translation.Job/Program.cs:13`. Conn string
  `ConnectionStrings:Translation` (AWS RDS) — **in use**, and required by all three.

### `TranslationCache` (`LegacyTranslationCache`) — RETIRED (GM-784, 2026-09-14)
The legacy, `SOURCE_HASH`-keyed `TranslationCache` table is **no longer read by this repo**. The
`LegacyTranslationCache` entity, the `TranslationContext.TranslationCache` DbSet, the
`Custom:Translation:CacheBackfill` bridge and its `SourceHash` helper were all deleted.
`TranslationCache2` is now the context's only DbSet. The table itself still exists in SQL (it is
externally owned, with DDL in `infrastructure/flyway/migrations/<env>/V1__Baseline.sql`); nothing in
OEv2 touches it, so it can be dropped independently whenever the owning team is ready.

**Why it was removed.** The bridge copied legacy targets into empty `TARGET_TEXT_*` columns and set
`PROCESSED = 1` **in SQL only**. Nothing announced those rows: `update.py`'s worklist is
`PROCESSED = 0`, and this service's in-memory cache, OE.Adapter and Mongo never learned about them.
Team and player names therefore rendered **English** on the sportsbook while Backoffice showed them
translated, until a Backoffice re-save (which publishes `OnTranslationChange` itself) or a service
restart (which re-`Preload()`s from SQL and stamps missing `*NaTr` via `BackfillActiveSnapshot`).
In production the bridge was silently swallowing **1,700-2,300 targets/day** while `update.py`
logged `cache_hits=0` on every pass. Live from 2026-07-06 until this removal.

Rows now seed with empty targets and `PROCESSED = 0`, so `update.py` translates them via DeepL and
announces them normally. That roughly doubles daily DeepL volume, which is the intended trade:
correctness over the cost saving the bridge existed to provide.

## Trading DB — `TradingContext` (`OE.Shared/Data/Trading/`)
- **DbSets / tables:** `ClientOddsFeedConfiguration`, `ClientOddsFeedLiveFixture`.
- **Keys** (`TradingContext.cs:14-15`): `ClientOddsFeedConfiguration` PK `{ClientId, SportId}`;
  `ClientOddsFeedLiveFixture` PK `{ClientId, SportId, FixtureSpecifier}`.
- **Columns:** `ClientOddsFeedConfiguration {ClientId(Guid), SportId, PrematchOdds, LiveOdds,
  MaxFixtures?}`; `ClientOddsFeedLiveFixture {ClientId, SportId, FixtureSpecifier, FixtureStartDate}`.
- **Read pattern** (`OE.UOF.Client/Repository/TradingRepository.cs`): on startup, read this client's
  configs (by `ClientId`) and current-month live fixtures. ⚠️ **News up a `new TradingContext` per
  call** (not pooled/injected) — [TechnicalDebt.md](./TechnicalDebt.md) M9.
- **Purpose:** client entitlement for the external UOF feed (which sports / prematch vs live /
  `MaxFixtures`). ⚠️ Enforcement is currently **disabled** (commented out) — [BusinessRules.md](./BusinessRules.md) BR-17.
- Conn string `Custom:Trading`.

## SQL databases OEv2 does NOT use (boundary)
- **`Sportsbook` SQL DB** (sport/segment/league order, `MarketTemplateGroup`, `TierLeagueOrder`,
  mappings, boosted/priceboost config) — written by **backoffice-final**; whether OEv2 reads it
  directly is unconfirmed (OEv2's known SQL reads are only Trading + Translation) —
  [Unknowns.md](./Unknowns.md) #26. [Verified-sibling:backoffice-final]
- **`Auth` SQL DB**, **Betting/Wallet SQL** — platform/Auth only. [Verified-sibling]

## Patterns & notes
- **No migrations in-repo** — the SQL schema is owned/managed elsewhere (backoffice-final / DBA).
  EF here is a **read mapping**, not a schema authority. [Verified — absence]
- **No transactions / unit-of-work** spanning SQL + Mongo + RabbitMQ — each store is updated
  independently; consistency is eventual (see [PersistencePatterns.md](./PersistencePatterns.md)).
- **`AsNoTracking`** for the read-only translation query (correct — no change tracking overhead).
- **Redis: not used.** There is no Redis/distributed cache anywhere in OEv2 (caching is in-process
  `MemoryCache` — [Caching.md](./Caching.md)).
