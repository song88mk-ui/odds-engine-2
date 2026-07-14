# Domain Model — Sportsbook (odds-engine-2 view)

> **Perspective.** odds-engine-2 is the **catalogue authority**: it ingests provider events and
> owns the live sportsbook *catalogue* (fixtures/markets/selections/odds) in MongoDB. It does **not**
> own bets, wallets, users, or cashout *transactions* — those belong to **platform-final**
> (see Phase 1 [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md)). Where an entity
> is partly cross-repo, that is stated. All [Verified] unless tagged. Field glossary:
> [SharedLibraries.md](./SharedLibraries.md); terms: [BusinessGlossary.md](./BusinessGlossary.md).

## The hierarchy at a glance

```
Sport (SpId)
 └─ Segment (SegId)          region / category   e.g. "England", "International"
     └─ League (LeId)        competition / tournament  e.g. "Premier League"
         └─ Fixture (FiId)   a match OR an outright
             └─ Market (MaId)       one betting market, typed by Market Template (MaTeId)
                 └─ Selection (SeId) one outcome, typed by Selection Template (SeTeId), carries Odds (Od)
```

Sport/Segment/League are **reference dimensions** (denormalized onto every Fixture/Market/Selection
*and* materialized into `*Lookup` collections). Fixture/Market/Selection are the **operational
documents**.

---

## Core operational entities

### Fixture — `Fixture` collection (`Collections/Fixture.cs`)
- **Purpose:** A bettable event — either a **Match** (`FiTyId=Match=0`, has Home/Away) or an
  **Outright** (`FiTyId=Outright=1`, e.g. "tournament winner"). The aggregate root of the catalogue.
- **Identity:** `FiId` (internal long) ↔ `PrFiId` (provider id) + `ChTyId` (channel/provider).
  Mongo `_id` = composite string.
- **Key state:** `FiStId`/`FiStNa` (FixtureStatus), `Sc` (score), live state `CuPe`/`CuMi`/`CuSe`
  + `PeSc` (period scores), `IsOfLi` (offered live), `IsSu` (bet-stopped), `IsBo` (boosted),
  `IsHoFa` (home is favorite), `FiSoOr` (sort order), `LiId`/`StUr`/`GeAl` (livestream + geo),
  `ClCo`/`ClMa` (client visibility), participants `HoId`/`AwId` (+ `HoBeId`/`AwBeId`/`LeBeId`),
  translation fields `SpNaTr`/`SegNaTr`/`LeNaTr`/`FiNaTr` (Mongo schema fields that **remain but are
  never written** — see [MongoDb.md](./MongoDb.md); the wire `*NaTr` equivalents were removed 2026-06-30).
- **Source of truth:** MongoDB `Sportsbook` (owned by OEv2).
- **Created/updated:** `OE.Sportsbook.Service` from `OnFixtureChange`/`OnMarketChange` (upsert by `FiId`).
- **Closed:** `OE.Job.Settlement` (publishes close when started/6h+ ended). **Purged:** `OE.Job.Cleanup`.
- **Stream enriched:** `OE.Job.LiveStream` (sets `LiId`/`GeAl`).
- **Consumed:** platform-final (reads Mongo for REST), `OE.Websocket.Service` (push). **Rendered:**
  platform-web fixture lists / fixture details / live.

### Market — `Market` collection (`Collections/Market.cs`)
- **Purpose:** One betting market on a fixture (e.g. "Match Result", "Total Goals O/U 2.5"),
  **typed by Market Template** `MaTeId`. Carries the line/handicap `Li`.
- **Identity:** `MaId` ↔ `PrMaId`; belongs to `FiId`.
- **Key state:** `MaStId`/`MaStNa` (MarketStatus), `CaStId`/`CaStNa` (CashoutStatus — *availability*),
  `InPl` (in-play), `MaSoOr` (sort order), `IsBo`/`ClBo` (boosted), `ClCo`/`ClCoMa` (client masks),
  `MaNaTr` (Mongo name-translation field — remains in schema but never written; the wire `MaNaTr` was removed 2026-06-30).
- **Source of truth / CRUD / consumers:** same as Fixture (Sportsbook.Service writes; platform reads;
  Websocket pushes; platform-web renders market views & bet slip).

### Selection — `Selection` collection (`Collections/Selection.cs`)
- **Purpose:** One outcome within a market (e.g. "Home", "Over 2.5"), **typed by Selection Template**
  `SeTeId`. The carrier of **Odds**.
- **Identity:** `SeId` ↔ `PrSeId`; belongs to `MaId`/`FiId`.
- **Key state:** `Od` (decimal odds), probabilities `Pr`/`WoPr`/`LoPr`/`VoPr`/`HaWoPr`/`HaLoPr`,
  `DeHeFa` (dead-heat factor), `IsAc` (active), `SeStId`/`SeStNa` (SelectionStatus), `SeSoOr`,
  boosted `ClBoOd`/`ClBoSeNa`/`ClBoSeNaTr`, `SeNaTr` (Mongo translation field — remains in schema but
  never written; the wire `SeNaTr`/`ClBoSeNaTr` were removed 2026-06-30), `CoId` (correlation).
- **CRUD / consumers:** same pipeline; the most frequently updated entity (odds churn).

---

## Reference dimensions (denormalized + materialized as lookups)

| Domain term | OEv2 representation | Lookup collection | Notes |
|---|---|---|---|
| **Sport** | `SpId`/`SpNa` on every doc | `SportLookup` (`SpId`, `SpNa`, `IsMap`) | top of hierarchy |
| **Segment** (region/category) | `SegId`/`SegNa` | `SegmentLookup` (`SeLoId`, `SpId`, `SegId`, `SegNa`, `IsMap`) | the prompt's "Competition" maps best to **Segment** as a grouping, but semantically Segment = region/category |
| **League** (competition/tournament) | `LeId`/`LeNa` | `LeagueLookup` (`LeLoId`, `SpId`, `SegId`, `LeId`, `LeNa`, `IsMap`) | the prompt's "Tournament"/"Competition" = **League** |
| **Market Template** (market type) | `MaTeId`/`MaTeNa` | `MarketLookup` (`MaLoId`, `SpId`, `MaTeId`, `MaTeNa`, `FiTyId`, `IsMap`) | no standalone collection; player-props templates fetched from Platform API (`MarketTemplatePlayerPropsResponseDTO`) |
| **Selection Template** | `SeTeId`/`SeTeNa` | `SelectionLookup` (`SeLoId`, `SpId`, `MaTeId`, `SeTeId`, `SeTeNa`, `FiTyId`, `IsMap`) | — |

- **Maintained by:** `OE.Job.Lookup` (upserts lookups from `OnMarketChange`); dimensions are also
  denormalized onto Fixture/Market/Selection by `OE.Sportsbook.Service`.
- **`IsMap`:** whether the dimension/template is mapped to internal ids (mapping completeness flag).
- **Consumed/rendered:** platform-web sports tree, filters, market-template selectors.

---

## Cross-cutting / supporting entities

### Market Template & Selection Template
- The **type system** for markets/selections (`MaTeId`/`SeTeId`). Defined upstream (provider) and by
  platform configuration (e.g. **player-props** templates via `v1/sportsbook/client/markettemplateplayerprops`,
  **bet-builder** eligible templates processed in `OE.Websocket.Service`). OEv2 references templates;
  the authoritative template *catalogue* is [Likely] platform-side. [Likely]

### Participant (team / player)
- **Not a standalone collection.** Match participants are embedded on Fixture: `HoId`/`AwId`
  (+ `HoBeId`/`AwBeId`, `LeBeId`). Player participants (player-props) are referenced via selection
  templates. Naming/translation handled like other entities.

### Odds — embedded on Selection + multi-format client odds
- `Od` is the master decimal odds. Per-client formats are computed by `OE.UOF.Client`
  (`ClientOdds`: Decimal/Malaysian/HongKong/Indonesian) with a **+2% margin**; boosted odds in
  `ClBoOd`. Probabilities drive settlement math. Highest-frequency event: `OnOddsChange`.

### Cashout — *status*, not transaction
- OEv2 owns **cashout availability** as market state: `CaStId`/`CaStNa` (`CashoutStatus`:
  Available/Unavailable/Closed). The actual **cashout transaction** (offer value, execute) lives in
  **platform-final** (`Platform.CashOut.Service`, SQL Betting). OEv2/Websocket only *forwards*
  `OnCashOutChange` to the UI. Cross-repo.

### Boosted Odds / Price Boost
- A fixture/market/selection can be **boosted for specific clients**: `IsBo` + `ClBo` (client list),
  `ClBoOd` (boosted odds), `ClBoSeNa`/`ClBoSeNaTr` (per-client boosted selection name + translations).
  Rendered as "Price Boost" in platform-web. Created/updated upstream + applied in the catalogue.

### Translation — `SportsbookDynamic` (SQL) + MemCache (read path now REMOVED)
- **Purpose:** localized names for sport/segment/league/fixture/market/selection.
- **Source of truth:** SQL `Translation.SportsbookDynamic` (`StringId`, `SourceText`, `TargetText`,
  `TargetLanguageId`, `Processed`, `GlossaryVersion`, …) — populated by an **external translation
  service** ([Likely] bet-genius `Translation.Service`).
- **(Corrected 2026-06-30: translations removed from pipeline.)** `OE.Sportsbook.Service` **no longer
  reads SQL translations**: `LoadTranslationsAsync`/`MapTranslations`/`AddTranslationKey`/`AddContentKey`
  were deleted from `SportsbookRepository`, `ITranslationRepository` is no longer injected, and the
  `*Tr` lists were removed from the OE.Shared messages — so translations are **no longer embedded** into
  the published message (and were never persisted to Mongo; those `.Set()` calls were already commented
  out, see [MongoDb.md](./MongoDb.md)). The `TranslationRepository`/`TranslationContext` are still
  DI-registered but **never resolved (orphaned/dead code)**, so `ConnectionStrings:Translation` and
  `Custom:Translation:UseContentKey` are now unused, the `sb:translation:*` MemCache is never populated,
  and the `OnDynamicTranslationChange` invalidation handler is effectively a **no-op** (handler retained).
  The **surviving** UI translation path is platform-web's own REST `GetTranslations()` → `TranslationState`. [Verified]

### Trading — client feed entitlement (SQL `TradingContext`)
- "Trading" here = **odds-feed management / client entitlement**, not bet trading. Entities:
  - `ClientOddsFeedConfiguration` (`ClientId`, `SportId`, `PrematchOdds`, `LiveOdds`, `MaxFixtures`)
    — what each external client is entitled to receive.
  - `ClientOddsFeedLiveFixture` (`ClientId`, `SportId`, `FixtureSpecifier`, `FixtureStartDate`).
- **Used by:** `OE.UOF.Client` to shape the external feed. ⚠️ Enforcement (`MaxFixtures`/validation)
  is currently **disabled** (commented out) — see [BusinessRules.md](./BusinessRules.md) / TechnicalDebt H5.
- Broader sense: the upstream **trading feed** is **bet-genius** (Genius Sports) — the origin of all
  fixture/market/odds/settlement events.

### Bet & Bet Builder & Outright (boundary notes)
- **Bet** (`BetStatus` enum) and **Bet Builder** *pricing/combination* are **platform-final/web**
  concerns; OEv2 produces the inputs (odds, settlement) and flags bet-builder-eligible market
  templates (`OE.Websocket.Service.ProcessBetBuilderMarketTemplates`). OEv2 publishes per-selection
  **settlement** (`OnBetSettlement` → `r.oe.bet.settlement`) that platform turns into bet outcomes.
- **Outright** is not a separate entity — it is a **Fixture with `FiTyId=Outright`**; lookups carry
  `FiTyId` to keep outright vs match templates distinct.

### BettingStatus — global bet-stop flag
- `BettingStatus` collection (`BeSo` key, `IsBeSo`). The doc with `BeSo="betstop"` holds the **global
  bet-stop** state, loaded at Sportsbook startup and toggled by `OnBetStopGlobal`.

### Mappings — provider↔internal id resolution
- `MarketMapping` (`MaId`←`PrFiId`/`MaTeId`/`MaSp`), `SelectionMapping` (`SeId`←…`SeTeId`/`MaSp`):
  resolve provider specifiers to internal ids. (Cleanup currently leaves these in place — its
  mapping-delete code is commented out.)

---

## Entity → ownership / flow summary

| Entity | Source of truth | Created/Updated in | Consumed by | Rendered in |
|---|---|---|---|---|
| Fixture | Mongo Sportsbook (OEv2) | Sportsbook.Svc; closed by Settlement; purged by Cleanup; stream by LiveStream | platform-final (read), Websocket (push) | platform-web |
| Market | Mongo Sportsbook (OEv2) | Sportsbook.Svc | platform-final, Websocket | platform-web |
| Selection / Odds | Mongo Sportsbook (OEv2) | Sportsbook.Svc; client formats by UOF.Client | platform-final, Websocket | platform-web |
| Sport/Segment/League + templates | provider; materialized in Mongo (OEv2) | Sportsbook.Svc + Job.Lookup | platform-final | platform-web |
| Translation | SQL Translation (external svc) | external; **OEv2 read path removed 2026-06-30** (repo orphaned/uncalled) — UI localizes via platform-web REST `GetTranslations()` | platform (REST) | platform-web |
| Client feed config (Trading) | SQL Trading (platform/ops) | platform/ops | UOF.Client | — |
| Cashout (status) | Mongo (OEv2) | Sportsbook.Svc | platform, Websocket | platform-web |
| Cashout (transaction) | SQL Betting (**platform**) | platform CashOut.Svc | platform | platform-web |
| Bet / Bet Builder | SQL Betting (**platform**) | platform | platform | platform-web |
| Global bet-stop | Mongo BettingStatus (OEv2) | Sportsbook.Svc | OEv2/Websocket | platform-web (banner) |
