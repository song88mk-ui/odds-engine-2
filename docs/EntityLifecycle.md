# Entity Lifecycle

State machines and lifecycle for the key sportsbook entities. Status values are [Verified] from
`OE.Shared/Enums/*`; transitions are [Verified] where a code path drives them, else [Likely].

## 1. Fixture lifecycle

**Statuses** (`FixtureStatus`): Prematch=0, Live=1, Suspended=2, Ended=3, Closed=4, Cancelled=5,
Abandoned=6, Delayed=7, Unknown=8, Postponed=9, Interrupted=10, MatchAboutToStart=11, NotMapped=99.

```
   (provider OnFixtureChange creates the doc)
        │
        ▼
   ┌──────────┐  start nears   ┌───────────────────┐  kickoff   ┌──────┐
   │ Prematch │ ─────────────► │ MatchAboutToStart │ ─────────► │ Live │
   └────┬─────┘                └───────────────────┘            └──┬───┘
        │ (Cancelled/Postponed/Delayed/Abandoned can occur from any pre-end state)
        │                                                          │ match ends
        ▼                                                          ▼
   ┌───────────┐                                              ┌────────┐
   │ Cancelled │                                              │ Ended  │
   └───────────┘                                              └───┬────┘
        Live can also be Suspended (temporary) / Interrupted      │ +6h or post-settlement
                                                                  ▼
                                                            ┌────────┐  retention window  ┌────────┐
                                                            │ Closed │ ─────────────────► │ PURGED │
                                                            └────────┘   (Job.Cleanup)    └────────┘
```

- **Create/update:** `OE.Sportsbook.Service` upserts from `OnFixtureChange`/`OnMarketChange`
  (status comes from the provider via bet-genius). `IsOfLi` flips when both match-state and
  trading-state are licensed.
- **Close:** `OE.Job.Settlement` finds fixtures started / 6h+ after a match ended and publishes a
  close (`FiStId=Closed`) — gated by `Custom:EditSportsbook`. (Note M8: it publishes but doesn't
  write Mongo itself — the actual write happens via the resulting message path.)
- **Purge:** `OE.Job.Cleanup` deletes Fixture + its Markets + Selections when status ∈
  {Ended,Closed,Cancelled,Abandoned,…} **and** older than `SportRetention` (default 30 days),
  during the UTC window, when `Custom:Enabled`. Since 2026-07-13 deletes run in batches
  (`DeleteMany` with `Filter.In` on `FiId`; `Custom:BatchSize`/`Custom:BatchDelayMs`) and the UTC
  window is re-checked per batch, so a long backlog stops at window end and resumes next window.
  Fixtures with **bet placements are never purged** (2026-07-13): fixture ids with bets are
  preloaded from the platform Betting SQL DB and kept current by consuming `r.bet.placement`;
  cleanup refuses to run until the preload has succeeded.
  ⚠ Prod has `Custom__Enabled: false` since 2026-02-05 — the purge has never run in prod.
- **Bet-stop overlay:** `IsSu` (per-fixture bet-stop) and the global flag suspend betting *without*
  changing `FiStId` — see §5.
- **Live state:** `Sc`, `CuPe`/`CuMi`/`CuSe`, `PeSc` update continuously while Live.

## 2. Market lifecycle

**Statuses** (`MarketStatus`): Cancelled=-4, Settled=-3, HandedOver=-2, Suspended=-1, Inactive=0,
Active=1, NotMapped=99.

```
            ┌──────────┐  open   ┌────────┐  pause/resume  ┌───────────┐
 created ──►│ Inactive │ ──────► │ Active │ ◄────────────► │ Suspended │
            └──────────┘         └───┬────┘                └───────────┘
                                     │ provider hands market to another feed
                                     ▼
                                ┌────────────┐
                                │ HandedOver │
                                └────────────┘
   terminal: ──► Settled (-3, results in)   or   Cancelled (-4, voided)
```

- **Create/update:** `OE.Sportsbook.Service` from `OnMarketChange` (upsert by `MaId`); status set to
  Inactive on certain transitions.
- **Suspend/resume:** driven by provider market status + bet-stop logic.
- **Settle:** `OnMarketSettlement` → per-selection results published to `r.oe.bet.settlement`.
- **Cancel:** `OnMarketCancel` (carries `VoRe` void reason + time window).
- **Cashout availability** rides alongside as `CaStId` (`CashoutStatus`: Available=1, Unavailable=-1,
  Closed=-2) — independent of `MaStId`.

## 3. Selection lifecycle

```
 created ──► Active (IsAc=true, Od live) ──► (odds change many times) ──► Inactive (IsAc=false)
                                          └─► Settled with SeStId = SettlementStatus
```
- **Odds churn:** `Od` (+ probabilities) update on every `OnOddsChange` — the highest-frequency
  transition. Per-client formats (`ClOd`) and boosted odds (`ClBoOd`) derived downstream (UOF.Client).
- **Settle:** `SeStId` set from settlement; `DeHeFa` (dead-heat factor) applied where relevant.
- **Active flag** (`IsAc`) toggles availability without deleting the selection.

## 4. Settlement & Bet outcome lifecycle (cross-repo)

**SettlementStatus** (OEv2, per selection): Pending=0, Lost=1, Won=2, Void=3, HalfWon=4, HalfLost=5,
Cancelled=6, CashedOut=7. **VoidFactor** (applied): Zero/Half/One.
**BetStatus** (platform, per bet): Waiting=1000, Running=2000, Won=3000, HalfWon=3001, Lost=4000,
HalfLost=4001, Void=5000, Rejected=6000, Cancelled=7000, CashedOut=8000.

```
 Selection result (OEv2 SettlementStatus)
        │  OnBetSettlement → r.oe.bet.settlement
        ▼
 platform Settlement.Service  → resolves each Bet's BetStatus (Won/Lost/Void/HalfWon/…)
        │                       (Helper.GetVoidFactor maps status → VoidFactor)
        ▼
 Wallet credited / Bet closed (SQL Betting, platform)  → OnWalletChange / OnBetStatus → UI
```
- OEv2 emits **selection-level settlement**; platform composes **bet-level outcome** (a bet may span
  many selections, esp. parlays/bet-builder).

## 5. Bet-stop lifecycle (overlay state, not a status enum)

```
 Global:  BettingStatus("betstop").IsBeSo  ── OnBetStopGlobal(IsBetStop) ──► all betting suspended
          (loaded at Sportsbook startup; in-memory _IsBetStopGlobally; routed via reserved shard 10)
 Fixture: Fixture.IsSu  ── OnBetStopFixture(IsBetStop, SetActiveMarketsToInactive) ──► fixture paused
          (optionally flips that fixture's active markets to Inactive)
```
- Bet-stop is a **suspension overlay**: it gates whether bets are accepted; it does not change
  `FiStId`/`MaStId` per se (though fixture bet-stop can inactivate markets). ⚠️ The global flag is
  read/written without synchronization (TechnicalDebt H3).

## 6. Cashout availability lifecycle (status only in OEv2)
```
 Market.CaStId:  Available (1) ◄──► Unavailable (-1) ──► Closed (-2)
```
- OEv2 maintains availability; the **cashout transaction** lifecycle (offer → accept → settle) is
  platform-final's (`Platform.CashOut.Service`).

## 7. Translation lifecycle (2026-07 redesign)
```
 OE.Translation.Service seeds/updates SQL TranslationCache2 rows from the live feed
   (STRING_ID, SOURCE_TEXT, PROCESSED=0)          [Custom:Translation:CacheSeed]
        │
        ▼
 external translation pipeline fills TARGET_TEXT_ZH / _ES / _FR   (OEv2 never writes these)
        │
        ├──► OE.Adapter.Service preloads the whole table at STARTUP into its in-process cache
        │      └─► TradingRepository stamps *Tr onto outgoing messages (SpTr/SegTr/LeTr/HoTr/AwTr,
        │          MaTeTr, SeNaTr) — no per-message SQL
        │             ▼
        │          OE.Sportsbook.Service forwards *Tr through GateTranslations (delta gate: a key's
        │          translations are published only when they changed since this instance last sent them)
        │             ▼
        │          OE.Websocket.Service → ToOutgoingTranslations → SignalR OutgoingOddsChange.*Tr
        │
        └──► OE.Translation.Service stamps *NaTr onto the Mongo documents (update-only, with retry)
               │  live path on ON_FIXTURE_CHANGE / ON_MARKET_CHANGE
               │  startup: SeedActiveSnapshot (Mongo → SQL) + BackfillActiveSnapshot (cache → Mongo)
               ▼
             ON_TRANSLATION_CHANGE → persist the *NaTr delta, then republish TRANSLATION-ONLY odds
             changes to r.oe.sb.engine.on_odds_change so live clients update without an odds move

 OE.Translation.Job: flags HAS_BET_PLACEMENT on r.bet.placement; batch-deletes rows past RetentionDays

 Retired: the SportsbookDynamic read path in OE.Sportsbook.Service. Its sb:translation:* MemCache is
 never populated, so the surviving ON_DELETE_CACHE purge handler is a NO-OP.

 Surviving UI path: platform-web REST GetTranslations() → TranslationState (30s cache)
```

## 8. Livestream enrichment lifecycle
```
 Job.LiveStream (60s loop) → BetGenius Video API (token cached to ExpiresIn; fixtures cached ~5h)
        │  matches prematch/live fixtures
        ▼
 sets Fixture.LiId / GeAl (and stream URL StUr) → platform-web livestream UI (geo-restricted)
```

## 9. Lookup/dimension lifecycle
```
 OnMarketChange → Job.Lookup upserts SportLookup/SegmentLookup/LeagueLookup/MarketLookup/SelectionLookup
   (IsMap reflects mapping completeness; last-writer-wins on name, unordered writes)
```
