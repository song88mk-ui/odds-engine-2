# Odds Engine Integration Boundary

The canonical **"how to integrate with odds-engine-2"** spec: everything OEv2 consumes, everything
it produces, and who is on the other end. This is the integration contract surface — the things you
cannot change without coordinating across repos.

Classification: **[Verified]** in odds-engine-2 · **[Verified-sibling:X]** confirmed in repo X ·
**[Historical:Reference]** only in the Reference snapshot · **[Unknown]**. Never infer external behavior.

## 1. Integration surface at a glance

```
            INBOUND (OEv2 consumes)                         OUTBOUND (OEv2 produces)
 ┌──────────────────────────────────────┐     ┌─────────────────────────────────────────────┐
 bet-genius  ── r.bg.receiver.* ───►            ──► MongoDB Sportsbook (catalogue)  → platform-final reads, backoffice reads
 (UOF src?)  ── r.uof.* ───────────►  OEv2  ──► r.oe.sb.engine.* (RabbitMQ) → OE.Websocket, …
 backoffice  ── r.oe.cachesync.* (cfg) ►        ──► r.oe.bet.settlement → platform Settlement.Service
 KeepAlive   ── r.oe.uof.heartbeat ──►          ──► /platform SignalR hub (via OE.Websocket) → platform-web
 SQL Trading/Translation (read)                 ──► external RabbitMQ vhost `ext` (uof.*) → white-label clients
 BetGenius Video API (HTTP, read)               ──► SQL: none written by OEv2 (read-only) [see note]
 Platform API (HTTP, read)                       
 └──────────────────────────────────────┘     └─────────────────────────────────────────────┘
```
> Note: OEv2 **reads** SQL (Trading client-config, Translation names); it does **not** write them —
> those are written by **backoffice-final** (see §4). OEv2 **owns** the MongoDB catalogue.

## 2. Inbound integrations (what OEv2 consumes)

| Source | Transport | Contract | Classification |
|---|---|---|---|
| **bet-genius** | RabbitMQ `r.bg.receiver.onfixturechan/onmarketchange/onmarketsettlement/ononmatchsummary` on `e.central` | `OE.Shared` `Outgoing*Message` DTOs | [Verified-sibling:bet-genius] |
| **UOF source** (`oddsfeed`/legacy `oddsengine`?) | RabbitMQ `r.uof.#` | same `Outgoing*Message` shapes | [Unknown] producer; [Verified] OEv2 binds it |
| **OE.Job.KeepAlive** → OE.UOF.Client | RabbitMQ `r.oe.uof.heartbeat` | `OnHeartbeatMessage {IsHealthy, Timestamp}` | [Verified] |
| **backoffice-final** (config/cache) | RabbitMQ `r.oe.cachesync.*` on `e.central` | `OnClientChange`/`OnParlayBonusChange`/`OnMarketSortOrderChange`/`OnSportSortOrderChange`/(`OnDynamicTranslationChange`) | [Verified-sibling:backoffice-final] publishes; OEv2 handles `OnDynamicTranslationChange` [Verified]; exact binding into Sportsbook [Unknown] |
| **(feed recovery)** | RabbitMQ `r.of.recovery.#.tso` → UOF.Client | snapshot/replay [Unknown shape] | [Unknown] |
| **SQL `Trading`** | EF Core read | `ClientOddsFeedConfiguration`, `ClientOddsFeedLiveFixture` (written by backoffice) | [Verified] read; [Verified-sibling:backoffice-final] write |
| **SQL `Translation`** | EF Core read **and write** | `TranslationCache2` (read: full preload by Adapter at startup; write: row seeding + `PROCESSED`/`HAS_BET_PLACEMENT` + retention deletes). The legacy external `TranslationCache` is **no longer read** — GM-784 retired the `CacheBackfill` bridge (2026-09-14) | [Verified]. `TARGET_TEXT_*` written by the **external translation pipeline only** |
| **BetGenius Video API** | HTTPS | `BGAuth`/`BGEvent(s)` (token + fixtures) | [Verified] |
| **Platform API** | HTTPS `x-api-key` | `v1/sportsbook/client/markettemplateplayerprops` (UOF.Client), template/metadata (Websocket) | [Verified] OEv2 call; [Verified-sibling:platform-final] handler |

## 3. Outbound integrations (what OEv2 produces & who consumes)

| Output | Transport / key | Consumer | Classification |
|---|---|---|---|
| **MongoDB `Sportsbook` catalogue** | Mongo write (Fixture/Market/Selection/BettingStatus/`*Lookup`) | platform-final (read), backoffice-final (read, secondary) | [Verified] write; [Verified-sibling] reads |
| **Canonical odds change** | `r.oe.sb.engine.onoddschange` | OE.Websocket.Service | [Verified] |
| **Bet-stop fixture/global** | `r.oe.sb.engine.onbetstopfixture` / `onbetstopglobal` | OE.Websocket.Service | [Verified] |
| **Bet settlement (per selection)** | `r.oe.bet.settlement` | platform `Settlement.Service` (binds `r.bet.settlement|r.oe.bet.settlement|r.of.bet.settlement`) | [Verified] publish; [Verified-sibling:platform-final] consume |
| **Sharded normalized stream** | `r.oe.adapter.<type>.<NN>` | OE.Sportsbook(NN), OE.Job.Lookup, OE.UOF.Client (intra-OEv2) | [Verified] |
| **Real-time UI push** | `/platform` SignalR hub methods (`PublishOddsChange*`, `PublishBetStop*`, `PublishBetStatus`, `PublishCashOutChange`, `PublishWalletChange`, `PublishPayableChange`) | the `/platform` hub gateway → platform-web | [Verified] OEv2 calls; [Verified-sibling:platform-web] receives; hub host repo [Unknown] |
| **White-label feed** | external RabbitMQ vhost `ext`: `uof.onoddschange.*`, `uof.onmarketsettlement.*`, `uof.onbetstop*`, `uof.onheartbeat`, … | external clients (e.g. `TSO`) | [Verified] publish; consumers external [Unknown] |
| **Fixture close** | `r.bg.receiver.onfixturecchange` (Settlement) | [Unknown] (note the `cchange` typo) | [Verified] publish; consumer [Unknown] |
| **Liveness** | `r.oe.uof.heartbeat` | OE.UOF.Client (+ monitoring?) | [Verified] |
| **Error sink** | `r.error.<domain>.<service>` (`r.error.oe.adapter`, `r.error.oe.translation`, `r.error.oe.translation.job`, `r.error.engine.uof.client`) | [Unknown] | [Verified] publish |

## 4. backoffice-final ↔ odds-engine-2 (the config/admin seam) — [Verified-sibling:backoffice-final]

This is the **write side** of what OEv2 reads — important and previously under-documented:

- backoffice-final is the **.NET admin app** (API net3.1 + Blazor net8 + worker services) that
  **reuses OE.Shared's Sportsbook schemas** (it carries the `OE.Shared.Data.Sportsbook` namespaces
  inline rather than via NuGet).
- It **reads** the Mongo `Sportsbook` catalogue (read preference Secondary) to display fixtures/markets.
- It **writes** the SQL config OEv2 consumes: `ClientOddsFeedConfiguration` (UOF.Client client
  entitlement), boosted/priceboost config, market-template groups, sort orders, mappings, and
  translations (`Translation` DB).
- It **publishes** config-change/cache-invalidation events to `e.central` on `r.oe.cachesync.*`
  (`OnClientChange`, `OnParlayBonusChange`, `OnMarketSortOrderChange`, `OnSportSortOrderChange`) and
  `r.bo.config.*`. platform-final's `CacheSync.Service` consumes `r.oe.cachesync.#`; OEv2's
  `OE.Sportsbook.Service` handles `OnDynamicTranslationChange` to purge its translation cache.
- It is deployed via the **same** CI→ECR→argocd pipeline (image names `backoffice-*`).
- It does **not** connect to the `/platform` odds hub (its WebSocket is internal admin UI).

> Net: **backoffice writes config → OEv2 reads config + invalidates caches on the published events;
> OEv2 writes the catalogue → backoffice reads it.** The coupling is via the **shared SQL config
> schema**, the **shared Mongo schema**, and the **`r.oe.cachesync.*` event contract**.

## 5. Cross-repo change rules (what breaks what)
Changing any of these is a **coordinated multi-repo release**:
- `OE.Shared` message/DTO shapes → bet-genius, backoffice-final, platform.
- MongoDB document schema (abbreviated fields) → platform-final, backoffice-final readers.
- SQL `ClientOddsFeedConfiguration` schema → backoffice-final (writer).
- SQL `TranslationCache2` schema → **the external translation pipeline and OEv2 both write it**
  (different columns — see [Contracts.md](./Contracts.md) C9). A column change here breaks
  `OE.Adapter.Service`'s startup preload, `OE.Translation.Service` and `OE.Translation.Job`
  simultaneously. The legacy `TranslationCache` table is externally owned and read-only to OEv2.
- `e.central` routing-key namespace (`r.oe.*`, `r.oe.cachesync.*`, `r.bet.*`) → all subscribers.
- `/platform` hub method signatures + SignalR message models → platform-web, the hub gateway.
- External `uof.*` feed → external white-label clients.

See [Contracts.md](./Contracts.md) for the full contract catalog and
[CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md) for the per-repo matrix.
