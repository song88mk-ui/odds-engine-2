# Database — SQL Server

odds-engine-2 uses **SQL Server via EF Core 3.1** for two **read-mostly reference databases**. It is
**not** the catalogue store (that's MongoDB — [MongoDb.md](./MongoDb.md)). All [Verified] unless tagged.

## Overview
| DB | Context | Used by | Access | Writer |
|---|---|---|---|---|
| **Translation** | `TranslationContext` | OE.Sportsbook.Service | **read-only** (`AsNoTracking`) | external translation svc / backoffice-final |
| **Trading** | `TradingContext` | OE.UOF.Client | **read-only** | backoffice-final |

> OEv2 **reads** SQL config (Trading); the **writer is backoffice-final**. (Corrected 2026-06-30:
> translations removed from pipeline — OEv2 no longer reads SQL `Translation`; that path is orphaned.) (see
> [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md), [OddsEngineIntegration.md](./OddsEngineIntegration.md)).
> EF Core is pinned to **3.1 (EOL)** by the netstandard2.1 `OE.Shared` target ([TechnicalDebt.md](./TechnicalDebt.md) M1).

## Translation DB — `TranslationContext` (`OE.Shared/Data/Translation/`)
> **(Corrected 2026-06-30: translations removed from pipeline.)** `OE.Sportsbook.Service` **no longer
> invokes** this context: `TranslationRepository.GetTranslationsAsync` and its `SportsbookRepository`
> call sites were deleted, and `ITranslationRepository` is no longer injected. The DbSet/columns/indexes
> below still exist in code (and `AddDbContextPool<TranslationContext>` is still registered), but the
> read pattern is **dormant/orphaned**; `ConnectionStrings:Translation` is now unused.

- **DbSet:** `SportsbookDynamic` → table **`SportsbookDynamic`**.
- **Columns** (mapped via Fluent API, `TranslationContext.cs:15-103`): `ID` (PK identity,
  `PK_SportsbookDynamic_v2`), `String_ID` (≤100), `SOURCE_LANGUAGE_ID`/`TARGET_LANGUAGE_ID` (≤10),
  `SOURCE_TEXT`, `TARGET_TEXT` (nullable), `SOURCE_HASH` (computed), `GLOSSARY_VERSION` (default 1),
  `CREATED_AT` (`GETDATE()`), `UPDATED_AT`, `PROCESSED` (default false).
- **Indexes** (declared in EF model):
  | Name | Keys | Notes |
  |---|---|---|
  | `UX_SBD_STRING_LANG` | `String_ID, TARGET_LANGUAGE_ID` | unique |
  | `IX_SBD_STRING_LANG` | `String_ID, TARGET_LANGUAGE_ID` | non-unique (duplicate of the unique — redundant) |
  | `IX_SBD_HASH_LANG` | `SOURCE_HASH, SOURCE_LANGUAGE_ID, TARGET_LANGUAGE_ID, GLOSSARY_VERSION` | dedup/versioning |
  | `IX_SBD_MISSING` | `TARGET_LANGUAGE_ID` filtered `WHERE [TARGET_TEXT] IS NULL` | find untranslated rows |
- **Read pattern** (`TranslationRepository.cs:73`): `SportsbookDynamic.AsNoTracking().Where(StringId ∈
  missing && Processed && TargetText != null).Select(TranslationItem).ToList()` — one batched query
  for all cache-missed `StringId`s; results grouped by `StringId`. Uses `UX_SBD_STRING_LANG`.
  **(Corrected 2026-06-30: this read is no longer invoked by Sportsbook — `GetTranslationsAsync` is uncalled.)**
- **Registration:** `AddDbContextPool<TranslationContext>` with `UseSqlServer(conn,
  CommandTimeout=Custom:DbContextTimeout (180s))` (`Program.cs:48`). Pooled → amortized for the
  high-frequency translation lookups. Conn string `ConnectionStrings:Translation` (AWS RDS).
- **`String_ID` format:** `"{Prefix}:{Id}"` e.g. `MaId:123`, `SpId:1` (built in
  `AddTranslationKey`, `SportsbookRepository.cs:955`).

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
