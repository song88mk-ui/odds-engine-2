# Entity Relationships

Cardinalities and ownership across the sportsbook catalogue. All [Verified] from the entity
definitions; cross-repo ownership tagged.

## 1. Containment hierarchy (the spine)

```
Sport (1) ──< Segment (N) ──< League (N) ──< Fixture (N) ──< Market (N) ──< Selection (N)
  SpId          SegId           LeId          FiId            MaId            SeId
```

- **Sport 1 — N Segment** (a sport has many regions/categories).
- **Segment 1 — N League** (a region has many competitions/tournaments).
- **League 1 — N Fixture** (a competition has many matches/outrights).
- **Fixture 1 — N Market** (a fixture offers many markets).
- **Market 1 — N Selection** (a market has many outcomes).

> Implemented by **denormalization**, not foreign keys: each `Market`/`Selection` document carries
> `SpId/SegId/LeId/FiId` (and `MaId`) inline. There is no referential integrity enforced by the DB —
> consistency is maintained by the writer (`OE.Sportsbook.Service`) and the sticky-shard guarantee
> (all of a fixture's docs are written by one instance).

## 2. Typing relationships (templates)

```
Market   (N) ──> MarketTemplate    (1)   via MaTeId   (+ MaTeNa, FiTyId)
Selection(N) ──> SelectionTemplate (1)   via SeTeId   (+ SeTeNa)
MarketTemplate (1) ──< SelectionTemplate (N)   (a market type defines its outcome types)
```

- Templates are **reference data** (dimension), surfaced via `MarketLookup`/`SelectionLookup`
  (keyed within a `SpId` and `FiTyId`). `FiTyId` distinguishes **Match** vs **Outright** template sets.
- Player-props & bet-builder templates are configured/served by **platform** (cross-repo).

## 3. Participant relationships (embedded, not a collection)

```
Fixture (1) ──> Home Participant   via HoId (HoBeId)
Fixture (1) ──> Away Participant   via AwId (AwBeId)
Fixture (1) ──> League ref         via LeBeId
```
- Match fixtures have exactly 2 team participants (Home/Away) embedded.
- Outright fixtures (`FiTyId=Outright`) have **no** Home/Away; participants are expressed as
  selections (e.g. each candidate winner is a selection).
- Player participants (player-props) are referenced through selection templates, not stored as
  first-class entities. [Likely]

## 4. Reference / lookup relationships

```
SportLookup     keyed by SpId
SegmentLookup   keyed by SeLoId, scoped by (SpId, SegId)
LeagueLookup    keyed by LeLoId, scoped by (SpId, SegId, LeId)
MarketLookup    keyed by MaLoId, scoped by (SpId, MaTeId, FiTyId)
SelectionLookup keyed by SeLoId, scoped by (SpId, MaTeId, SeTeId, FiTyId)
```
- These are **materialized projections** of the dimensions, maintained by `OE.Job.Lookup` (upsert,
  unordered). Each carries `IsMap` (mapping completeness). Used by platform-web for navigation/filters.

## 5. Mapping relationships (provider ↔ internal)

```
MarketMapping     MaId  ← (ChTyId, PrFiId, MaTeId, MaSp)
SelectionMapping  SeId  ← (ChTyId, PrFiId, MaTeId, SeTeId, MaSp)
```
- Resolve provider specifiers/ids to internal `MaId`/`SeId`. One mapping per internal id.

## 6. Translation relationships — Mongo schema fields remain, but NEVER WRITTEN / no longer embedded

```
Fixture   (1) ──< SpNaTr / SegNaTr / LeNaTr / HoNaTr / AwNaTr   (written by OE.Translation.Service)
Market    (1) ──< MaNaTr                                        (written by OE.Translation.Service)
Selection (1) ──< SeNaTr                                        (written by OE.Translation.Service)
Fixture   (1) ──< FiNaTr                                        (schema field — NEVER written; superseded
                                                                 by HoNaTr/AwNaTr in the 2026-07 redesign)
Selection (1) ──< ClBoSeNaTr  (boosted)                         (schema field — still unwritten)
```
- These `*NaTr` fields **are written**, but not by `OE.Sportsbook.Service` — its `.Set()`/
  `.AddToSetEach` calls remain commented out. `OE.Translation.Service` owns every `*NaTr` write
  (update-only, with a short retry against the upsert race) — see [MongoDb.md](./MongoDb.md).
- The per-language embedding into **published messages** is the `*Tr` lists
  (`List<TranslationMessage> {LaCo, Tx}`), stamped by `OE.Adapter.Service` from its startup-preloaded
  `TranslationCache2` snapshot, forwarded delta-gated by Sportsbook, and projected onto SignalR by
  `OE.Websocket.Service`.
- **Cardinality caveat:** a `*NaTr` / `*Tr` list is **0..N per entity, one entry per language** — and a
  *missing* entry does **not** mean the entity has no translation. It means "unchanged since last
  publish" (the Sportsbook delta gate) or "not translated yet" (empty `TARGET_TEXT_*`). Always fall back
  to the English `*Na`.

## 7. Boosted-odds relationships (client-scoped)

```
Fixture/Market/Selection (1) ──< boosted-for-Client (N)
   Selection.ClBo (client list), ClBoOd (odds), ClBoSeNa[ClCo→name], ClBoSeNaTr[ClCo+LaCo→name]
```
- A selection can be boosted for **specific clients** with per-client odds and display names.

## 8. Client-visibility relationships (multi-tenant)

```
Fixture.ClCo / Fixture.ClMa        which clients see the fixture
Market.ClCo / Market.ClCoMa        which clients see the market (mask)
Websocket per-client instances + AsianClientCodes   per-client delivery/format
```
- The catalogue is **multi-tenant**: documents carry client codes/masks; delivery (`OE.Websocket.Service`,
  `OE.UOF.Client`) is per-client.

## 9. Cross-repo relationships (catalogue ↔ betting)

```
Selection (odds)  ──(OnOddsChange)──►  platform-web (render)        [via Websocket→/platform hub]
Selection (result)──(OnBetSettlement)─►  platform Settlement.Service ─► Bet (SQL Betting)   [platform]
Market (CaStId)   ──(OnCashOutChange)─►  platform-web (cashout UI)
Bet / Bet Builder / Wallet / Cashout txn          owned by platform-final (SQL), NOT OEv2
```
- OEv2 produces **catalogue + odds + settlement signals**; platform-final owns **bets, wallet,
  cashout transactions** and joins them to the catalogue it reads from Mongo.

## 10. ER summary diagram

```
            ┌────────┐ 1     N ┌─────────┐ 1   N ┌────────┐ 1    N ┌─────────┐
            │ Sport  │────────►│ Segment │──────►│ League │───────►│ Fixture │
            └───┬────┘         └─────────┘       └────────┘        └────┬────┘
                │ (lookups + denormalized on every doc)                 │ 1
                │                                              ┌─────────┼─────────┐
                ▼                                              │ N       │ embeds  │ N
          SportLookup / SegmentLookup / LeagueLookup     ┌────▼───┐  Home/Away  ┌─▼──────────┐
                                                          │ Market │  Participant │ PeriodScore│
                                          MaTeId ────────►│        │              └────────────┘
                                       (MarketLookup,     └───┬────┘
                                        MarketMapping)        │ 1
                                                              │ N
                                          SeTeId ────────►┌───▼──────┐
                                       (SelectionLookup,  │Selection │── Odds (Od, ClOd, ClBoOd)
                                        SelectionMapping) │          │── (SeNaTr: written by Translation.Service)
                                                          └──────────┘── Settlement (SeStId, DeHeFa)

   Global: BettingStatus("betstop")     SQL: TranslationCache2 (translations — read+written by OEv2), Trading (client feed config)
```
