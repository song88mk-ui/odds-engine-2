# Project Overview

One section per project. Every project references **OE.Shared** and follows the
Worker → Repository → (broker | DB) shape. All findings [Verified] unless tagged.

---

## OE.Shared — shared kernel library

- **Purpose:** Single source of truth for message contracts, MongoDB/SQL data models, enums,
  the RabbitMQ broker, MemCache, helpers, and provider integrations.
- **TFM:** netstandard2.1. Packaged as NuGet (`PackageId=OE.Shared`, `Version=0.0.2`) and
  published to GitHub Packages (org `FlyingCockatoo`) by `oe-shared-npkg.yml`.
- **Key packages:** EF Core 3.1.32 (+ SqlServer), MongoDB.Bson 3.4.0, RabbitMQ.Client 5.2.0,
  OpenTelemetry.Api, System.Runtime.Caching, System.Text.Json.
- **Consumers:** all 11 worker services **and** `bet-genius` (shares the same contracts).
- **Detail:** see [SharedLibraries.md](./SharedLibraries.md).

---

## OE.Adapter.Service — normalizer + sticky-shard router

- **Purpose:** The pipeline's front door inside OEv2. Consumes provider-normalized messages,
  enriches them with translations, and re-routes them to the correct Sportsbook shard.
- **Consumes:** `r.uof.#` and `r.bg.receiver.#` (argocd) from `e.central`.
- **Handles** (`Worker.cs:98-144`): `ON_FIXTURE_CHANGE`, `ON_MARKET_CHANGE`,
  `ON_MARKET_SETTLEMENT`, `ON_BET_SETTLEMENT`, `ON_BET_STOP_GLOBAL`, `ON_MARKET_CANCEL`,
  `ON_BET_STOP_FIXTURE`.
- **Publishes:** `r.oe.adapter.<type>.<NN>` where `<NN>` = `Helper.GetInstanceName(ProviderFixtureId)`
  (`Repositories/TradingRepository.cs:58,121`); global bet-stop → `...on_bet_stop_global.10`
  (`:75`); errors → `r.oe.adapter.error` (`Worker.cs:158`).
- **Reads (SQL `Translation`):** at startup only — `TranslationCache.Preload()` loads the whole
  `TranslationCache2` table into an in-process cache (`Repositories/TranslationCache.cs:67-73`).
  Skipped with a warning when `ConnectionStrings:Translation` is blank (`Worker.cs:96-109`), so the
  service still starts without SQL — it just publishes no `*Tr`.
- **Translation enrichment:** `TradingRepository` stamps the `*Tr` lists onto outgoing messages from
  that cache — `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr` (`:119-123`), `MaTeTr` (`:144`), `SeNaTr` (`:159`).
  No per-message SQL.
- **Telemetry:** `processed.messages` counter tagged by `message.type`.
- **Notes:** `TradingRepository` despite its name does **no trading-DB access** here — it is
  the routing/transform/enrichment helper. Several status-mapping helpers (`GetFixtureStatus`, etc.,
  `TradingRepository.cs:95-113`) are **unused dead code**.

---

## OE.Sportsbook.Service — catalogue writer (sharded 00–10)

- **Purpose:** The heart of OEv2. Persists the live sportsbook catalogue into MongoDB and
  emits the canonical internal odds-change + settlement messages.
- **Deployment:** **11 instances** (`00`–`10`), each binding `r.oe.adapter.#.<NN>`
  (argocd). Committed dev stub binds `.11` (local only).
- **Startup:** creates Mongo indexes (`MongoIndexBootstrapper.CreateIndexesAsync`,
  `Program.cs:21-25`), loads global bet-stop state from `BettingStatus` (`Worker.cs:68`).
- **Handles** (`Worker.cs:100-144`): fixture/market change, market & bet settlement,
  bet-stop global/fixture, and `ON_DYNAMIC_TRANSLATION_CHANGE` (cache purge).
- **Writes (MongoDB `Sportsbook`):** Fixture, Market, Selection, BettingStatus — via unordered
  `BulkWrite` (histogram `bulkwrite.duration`).
- **SQL:** none. Sportsbook has **no** `TranslationContext` registration and reads no SQL — it is
  Mongo-only. (Before the 2026-07 redesign it read `SportsbookDynamic` per message; that path is gone.)
- **Translations:** *forwards*, never sources. Inbound `*Tr` lists are passed through
  `GateTranslations` (`SportsbookRepository.cs:789-793, :917-921`), a per-instance delta gate that
  publishes a key's translations only when they changed since that instance last sent them.
  `SeNaTr`/`MaTeTr` on the internal odds-change are read back from the **persisted Mongo `*NaTr`**
  (`:839, :864`) and deliberately **not** gated. Sportsbook writes no `*NaTr` — those writes belong to
  `OE.Translation.Service` (`:414`, `:969`).
- **Publishes:** `r.oe.sb.engine.on_odds_change` (canonical `OnOddsChange`),
  `r.oe.bet.settlement` (per-selection), `r.oe.sb.engine.on_bet_stop_*`.
- **Notes:** `IdGen` package referenced but unused; `_IsBetStopGlobally` not thread-safe; the
  `sb:translation:*` MemCache is never populated — only its purge handler survives
  (`ProcessCacheInvalidationMessage`, `:340`), so `ON_DELETE_CACHE` is a no-op.

---

## OE.Translation.Service — translation store + Mongo `*NaTr` writer

- **Purpose:** Owns the translation side of the pipeline: keeps the SQL worklist
  (`TranslationCache2`) in step with the live feed, and stamps translated names onto the Mongo
  catalogue. Added by the **2026-07 translation-flow redesign**.
- **Consumes** (`q.oe.translation`): `r.bg.receiver.onfixturechange`, `r.bg.receiver.onmarketchange`,
  `r.oe.ontranslationchange`.
- **Handles** (`Worker.cs:171-189`):
  - `ON_FIXTURE_CHANGE` → `OnProcessFixtureSetUpdate` (seed SQL keys) + `WriteFixtureTranslations`
    (stamp Mongo `SpNaTr`/`SegNaTr`/`LeNaTr`/`HoNaTr`/`AwNaTr`).
  - `ON_MARKET_CHANGE` → `OnProcessMarketSetUpdate` + `WriteMarketTranslations` (`MaNaTr`, `SeNaTr`).
  - `ON_TRANSLATION_CHANGE` → `ProcessTranslationChange`: persists the `*NaTr` delta and republishes
    **translation-only** odds changes to `r.oe.sb.engine.on_odds_change`
    (`MongoTranslationRepository.cs:1024-1192`) so live clients pick up a new translation without an
    odds move.
- **Writes (SQL `Translation`):** `SaveChangesAsync` on `TranslationCache2`
  (`TranslationRepository.cs:462, :586`) — **this is the one place OEv2 writes translation SQL.**
- **Startup migrations** (both non-fatal — a failure logs and startup continues):
  `SeedActiveSnapshot` (`Worker.cs:100-109`, seeds SQL from the active Mongo snapshot) and
  `BackfillActiveSnapshot` (`:116-126`, stamps `*NaTr` onto active documents from the preloaded cache,
  insert-missing-only).
- **Config:** `Custom:Translation:CacheSeed` (default `false`; argocd sets it `true` in stg + prd),
  `SeedActiveSnapshot` and `BackfillActiveSnapshot`. The `CacheBackfill` legacy bridge was removed in
  GM-784 (2026-09-14) — see [Database.md](./Database.md).

---

## OE.Translation.Job — translation retention + bet-placement flagging

- **Purpose:** Keeps `TranslationCache2` bounded, and protects rows that a bet depends on.
- **Consumes** (`q.oe.translation.job`): `r.bet.placement` → flags `HAS_BET_PLACEMENT = 1` for the
  keys behind a placed bet (`TranslationCleanupRepository.cs:42`, chunked so the `IN (...)` list stays
  inside the SQL Server parameter limit). Same `BetPlacementWorker` pattern as `OE.Job.Cleanup`.
- **Type:** also a timer job — every `Custom:Cleanup:IntervalMinutes` (default 60) after a
  `StartupDelaySeconds` (default 60).
- **Deletes (SQL):** rows older than `RetentionDays` (default 30) whose `STRING_ID` matches
  `Custom:Cleanup:Prefixes` (default `MaTe:,SeTe:,Te:,SpId:,SegId:,LeId:`), via batched
  `DELETE TOP (BatchSize)` raw SQL (default 2000) to keep transactions small and avoid lock escalation
  (`TranslationCleanupRepository.cs:80`, `RunBatchedDeleteAsync`).
- **Config:** `Custom:Cleanup:Enabled` (default `true`), `ConnectionStrings:Translation`.

---

## OE.UOF.Client — external Unified Odds Feed egress

- **Purpose:** Produces a **white-label outbound odds feed** (a "Unified Odds Feed") to an
  **external** RabbitMQ broker for a specific client (`ClientCode=TSO`). Applies per-client
  margin (+2%) and Asian odds conversions (Malaysian/Hong Kong/Indonesian). "UOF" = Unified
  Odds Feed **[Likely]**.
- **Consumes (internal broker):** `r.oe.adapter.#.#`, `r.oe.uof.heartbeat`, `r.of.recovery.#.tso`.
- **Publishes (external broker, vhost `ext`):** `uof.onfixture.change.*`, `uof.onoddschange.*`,
  `uof.onbetstop.*`, `uof.onbetstopglobal`, `uof.onmarketsettlement.*`,
  `uof.onmarketcancellation.*`, `uof.onheartbeat`; errors → `r.error.engine.uof.client`.
- **Data:** SQL Server `Trading` DB (`ClientOddsFeedConfiguration`, `ClientOddsFeedLiveFixture`)
  via `TradingRepository`; Platform REST API for market-template player props
  (`v1/sportsbook/client/markettemplateplayerprops`, cached 1 year).
- **Notes:** Heavy odds math in `ClientRepository`. Large blocks of disabled validation and
  fixture-cap enforcement are commented out — see [TechnicalDebt.md](./TechnicalDebt.md).
  Sport-id→name mapping is a hardcoded switch.

---

## OE.Websocket.Service — real-time SignalR push bridge

- **Purpose:** Bridges RabbitMQ → the platform's SignalR hub so the browser gets live updates.
  Acts as a **SignalR client** of `platform-final`'s `/platform` hub.
- **Consumes:** `r.oe.sb.engine.onoddschange`, `...onbetstopfixture`, `...onbetstopglobal`,
  `r.bet.betstatus.s88`, `r.bet.cashoutchange.s88`, `r.wallet.s88`,
  `r.oe.sb.engine.onvisitfavorites.s88`, `...onvisitboostedfixtures.s88`,
  `...onmarketsortorderchange.s88` (client `S88`).
- **Invokes hub methods:** `PublishOddsChangeMainMarkets/AllMarkets`, `PublishBetStatus`,
  `PublishCashOutChange`, `PublishWalletChange`, `PublishPayableChange`,
  `PublishBetStopGlobal/Fixture` (`Repository/SportsbookRepository.cs:586-590`).
- **Transport:** MessagePack (toggle `Custom:UseMessagePack`). Reads MongoDB Sportsbook +
  Platform REST API for templates/metadata.
- **Concurrency:** ~13 dedicated `Thread`s draining `BlockingCollection`s per message class.
- **Deployment:** multiple **client-specific** instances (krj/qte/g0x/oge/gsa) per env.
- **Notes:** highest-risk service — unmanaged threads, `async void`, `Environment.Exit(1)` on
  error. See [TechnicalDebt.md](./TechnicalDebt.md).

---

## OE.Job.Lookup — lookup-table maintainer

- **Purpose:** Maintains denormalized reference collections used for UI filtering/dropdowns.
- **Type:** Event-driven continuous worker (no timer).
- **Consumes:** `r.oe.adapter.onmarketchange.#` (`ON_MARKET_CHANGE`).
- **Writes (MongoDB):** `SportLookup`, `SegmentLookup`, `LeagueLookup`, `MarketLookup`,
  `SelectionLookup` — all upsert, unordered.
- **Notes:** minimal Program.cs (no Serilog/telemetry registration); silent exception swallow
  in the message handler; inefficient manual Pascal-casing.

---

## OE.Job.LiveStream — livestream URL sync

- **Purpose:** Periodically syncs live-stream availability/URLs from the BetGenius **Video** API
  into the `Fixture` collection.
- **Type:** Job — `while(true)` with `Task.Delay(60s)`; gated by `Custom:Enabled`; 5-min backoff
  on error.
- **Reads/Writes (MongoDB):** reads prematch/live match fixtures; updates `LiId`/`GeAl` via BulkWrite.
- **External:** BetGenius Video API via `IBetGeniusRepository`/`BetGeniusLiveStream` provider
  (OE.Shared). Only `ChannelType.BG` channels processed.
- **DI extension:** `Extensions/ServiceCollectionBettingExtensions.AddProviders()` wires the
  `LiveStreamFactory` + providers.

---

## OE.Job.Settlement — stale-fixture closer

- **Purpose:** Finds fixtures past start / 6h+ since match end and emits fixture-close so
  downstream settles them.
- **Type:** Job — `while(true)` with `Task.Delay(5min)`.
- **Reads (MongoDB):** `Fixture` filtered by status/type/time; gated by `Custom:EditSportsbook`.
- **Publishes:** `OnFixtureChange` with `FiStId=Closed` to `r.bg.receiver.onfixturecchange`
  [Verified — note the apparent typo "cchange" in the routing key].
- **Notes:** reads but does **not** write Mongo itself; `Custom:ApiKey` config is unused.

---

## OE.Job.Cleanup — retention purge

- **Purpose:** Deletes aged ended/closed fixtures and their markets/selections to bound MongoDB
  growth.
- **Type:** Job — `while(true)` every 5min, only acts within a **UTC hour window**
  (`Custom:UtcWindowHours:Start/End`, default 1–10), re-checked per batch since 2026-07-13.
  Throttles `Custom:BatchDelayMs` (default 2s) between batches of `Custom:BatchSize` (default 5000).
- **Deletes (MongoDB):** Fixture + related Market + Selection older than `SportRetention`
  (default −30 days), batched `DeleteMany` with `Filter.In` on `FiId`. Mapping-collection deletes
  are commented out.
- **Bet protection (2026-07-13):** fixtures with at least one persisted bet are never deleted.
  Fixture ids are preloaded from the platform **Betting SQL DB** (`ConnectionStrings:Betting`,
  `SELECT DISTINCT FixtureId FROM BetSelection`) and kept current by a `BetPlacementWorker`
  consuming `r.bet.placement` (`OnBetPlacement`, queue `q.oe.job.cleanup`) — same pattern as
  `OE.Translation.Job`. Cleanup throws (fail-safe) until the preload succeeds.
- **Notes:** disabled by default (`Custom:Enabled=false`) — **and disabled in prod argocd since
  2026-02-05, so it has never purged prod**; window logic can't cross midnight.

---

## OE.Job.KeepAlive — heartbeat publisher

- **Purpose:** Liveness ping for monitoring.
- **Type:** Job — `while(true)` with `Task.Delay(5s)`.
- **Publishes:** `OnHeartbeat` (`{IsHealthy, Timestamp}`) to `r.oe.uof.heartbeat`. Consumed by
  `OE.UOF.Client` (and [Likely] monitoring).
- **Notes:** `IsHealthy` is **hardcoded true** — not a real health check. Not in the .sln.

---

## OE.API — **stale / dead**

No `.cs` and no `.csproj`; only `appsettings.Development.json` and empty folders. Not built,
not deployed. Treat as removed. Recorded in [TechnicalDebt.md](./TechnicalDebt.md).
