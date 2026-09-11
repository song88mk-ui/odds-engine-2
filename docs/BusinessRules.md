# Business Rules

Domain rules **encoded in odds-engine-2**. Each rule cites evidence and a confidence tag. Rules
enforced *outside* OEv2 (bet placement, wallet, payout) are noted as cross-repo. ⚠️ marks rules
that are **currently disabled** in code (intent uncertain — see [Unknowns.md](./Unknowns.md)).

## Catalogue & identity
- **BR-1 [Verified]** Every fixture/market/selection is keyed by an **internal id** (`FiId`/`MaId`/
  `SeId`) distinct from the **provider id** (`PrFiId`/`PrMaId`/`PrSeId`); provider ids are resolved
  to internal ids via `MarketMapping`/`SelectionMapping`. A document's identity also includes the
  **channel** (`ChTyId`: BG/EM/OD) — the same provider id from different channels is distinct.
- **BR-2 [Verified]** Writes are **idempotent upserts** keyed on the natural id; the catalogue is
  eventually consistent and tolerant of message redelivery (at-least-once).
- **BR-3 [Verified]** A **fixture is a Match or an Outright** (`FiTyId`); outrights have no Home/Away
  participants and express candidates as selections. Templates are scoped by `FiTyId`.

## Sharding & ordering
- **BR-4 [Verified]** Every event for a fixture is routed to a **fixed Sportsbook instance**
  determined by the **last two digits of the provider fixture id** (`Helper.GetInstanceName` → 00–09);
  instance **10 is reserved for global bet-stop**. This guarantees per-fixture ordering and prevents
  cross-instance write races (Phase 1 ADR-003).

## Betting availability (bet-stop)
- **BR-5 [Verified]** **Global bet-stop** (`BettingStatus("betstop").IsBeSo`, toggled by
  `OnBetStopGlobal`) suspends all betting; state is loaded at Sportsbook startup. `StatusCode.Betstop_On_Global`
  communicates this to callers.
- **BR-6 [Verified]** **Per-fixture bet-stop** (`OnBetStopFixture`, `Fixture.IsSu`) suspends a single
  fixture; `SetActiveMarketsToInactive=true` additionally flips that fixture's active markets to Inactive.
- **BR-7 [Verified]** A **suspended/inactive market** does not accept bets (`MarketStatus.Suspended/Inactive`);
  `StatusCode.Market_Is_Suspended`/`Market_Has_Been_Suspended`/`Market_Is_Not_Offered` are the
  corresponding rejection codes (enforced platform-side at bet placement).

## Odds & pricing
- **BR-8 [Verified]** The catalogue stores **master decimal odds** (`Od`) plus probabilities
  (`Pr`/`WoPr`/`LoPr`/`VoPr`/`HaWoPr`/`HaLoPr`).
- **BR-9 [Verified]** For the external feed, `OE.UOF.Client` applies a **+2% margin** to master odds
  and converts to **client odds formats** (Decimal/Malaysian/Hong Kong/Indonesian) for
  `AsianClient`s (`ClientOdds`, `GetMasterOddsWithAddedMargin`). The 2% is **hardcoded** (not configurable).
- **BR-10 [Verified]** **Boosted odds** (`IsBo`/`ClBo`/`ClBoOd`/`ClBoSeNa`) are **client-scoped** —
  a selection can carry different boosted odds and display names per client. platform-web enforces
  boost rules at placement (`StatusCode.Boosted_Odds_*`): no parlay with boosts, no duplicate bets on
  the same boost, decimal-only, not allowed once a fixture goes live, not combinable with bet-builder.
- **BR-11 [Verified]** **Odds change is the highest-frequency event**; selections toggle `IsAc`
  rather than being deleted when unavailable.

## Settlement
- **BR-12 [Verified]** Settlement is emitted **per selection** (`OnMarketSettlement`/`OnBetSettlement`
  → `r.oe.bet.settlement`) with a `SettlementStatus` and a **dead-heat factor** (`DeHeFa`).
- **BR-13 [Verified]** **Void handling** maps status → `VoidFactor` via `Helper.GetVoidFactor`:
  Won/Lost → Zero (no void), Void → One (fully voided), HalfWon/HalfLost → Half (50%). Used to compute
  stake refunds.
- **BR-14 [Verified, cross-repo]** **Bet-level outcome** (a bet across many selections, incl.
  parlays/bet-builder) is composed by **platform** `Settlement.Service` from the per-selection
  settlements; `BetStatus` (Won/Lost/Void/HalfWon/…/CashedOut) is platform's.

## Cashout
- **BR-15 [Verified]** OEv2 maintains **cashout availability** per market (`CaStId`:
  Available/Unavailable/Closed); the **cashout transaction** (offer value, accept) is platform's
  (`StatusCode.CashOut_Is_Unavailable`, `CashOut_Stake_Changed_From_X_To_Y`).

## Multi-tenancy / client visibility
- **BR-16 [Verified]** The catalogue is **multi-tenant**: fixtures/markets carry client codes/masks
  (`ClCo`/`ClMa`/`ClCoMa`); delivery is **per-client** (per-client `OE.Websocket.Service` and
  `OE.UOF.Client` instances, `AsianClientCodes`).
- **BR-17 ⚠️ [Verified — DISABLED]** **Client feed entitlement** (`ClientOddsFeedConfiguration`:
  `PrematchOdds`/`LiveOdds`/`MaxFixtures`) is meant to gate what each external client receives, but
  the validation and `MaxFixtures` enforcement in `OE.UOF.Client` are **commented out** — currently
  **not enforced** (TechnicalDebt H5).

## Translation / localization
- **BR-18 [Verified]** Translations are embedded in the pipeline as `*Tr` lists, sourced from SQL
  `TranslationCache2`. `OE.Adapter.Service` preloads the table at startup and stamps `*Tr` onto every
  outgoing message; `OE.Sportsbook.Service` forwards them **delta-gated**; `OE.Websocket.Service` pushes
  them on SignalR; `OE.Translation.Service` persists them onto Mongo documents as `*NaTr`.
- **BR-18a [Verified]** **An absent or empty `*Tr` list means "unchanged since last publish" OR "not
  translated yet" — never "the translation is empty".** Consumers must fall back to the English `*Na`
  source. This is a consequence of `GateTranslations`, which publishes a key's translations only when
  they differ from what that Sportsbook instance last sent.
- **BR-18b [Verified]** OEv2 **never writes** `TARGET_TEXT_*` — those columns belong to the external
  translation pipeline. OEv2 owns the source columns, `PROCESSED`, and `HAS_BET_PLACEMENT`.
- **BR-18c [Verified]** `MaTeP:` (player-placeholder) rows are seeded `PROCESSED = 1` deliberately, to
  keep them **off** the external DeepL worklist — they are translated by hand via backoffice. This is the
  one sanctioned exception to the "never set `PROCESSED = 1`" rule
  (`TranslationRepository.cs:468-478`).
- **BR-18d [Verified]** Static **UI** strings are a separate system: platform-web's own REST
  `GetTranslations()`. Entity names come from OEv2; labels do not.

## Livestream
- **BR-19 [Verified]** Livestream availability is synced only for **BetGenius channel** fixtures
  (`ChannelType.BG`), every 60s, and is **geo-restricted** (`GeAl`, device/region permissions from
  the BetGenius Video API). `StatusCode.Fixture_Has_No_Livestream` / `Livestream_Is_Not_Available_In_Your_Region`.

## Retention / housekeeping
- **BR-20 [Verified]** Fixtures (and their markets/selections) are **purged after `SportRetention`
  days** (default 30) once in a terminal status, only during the configured **UTC window**, only when
  `Custom:Enabled` (default **false**). Deletion is throttled (~60s per 1000 fixtures).
- **BR-21 [Verified]** A fixture is **auto-closed** when it has started, or 6h+ after a match ended
  (`OE.Job.Settlement`, gated by `Custom:EditSportsbook`).

## Liveness
- **BR-22 [Verified]** A heartbeat is published every **5s** (`r.oe.uof.heartbeat`); ⚠️ `IsHealthy`
  is **hardcoded true** — it is a liveness ping, not a real dependency health check (TechnicalDebt H4).

## Rules NOT in OEv2 (boundary — enforced in platform-final/web)
- Bet placement validation: min/max stake, max payout, insufficient funds, currency mismatch,
  odds-changed, market-suspended, betting restrictions by sport/segment/league
  (`StatusCode` 104–130 series) — **enforced at placement in platform**, using the catalogue OEv2 owns.
- Bet Builder constraints (2–10 selections, same fixture, not combinable with boosts) — platform.
- Wallet/payout, parlay bonus, max liability by tier — platform.

> These `StatusCode` values live in OEv2's shared enum because the codes are a **shared contract**,
> but the *enforcement* is platform-side. Listed here so the full rule surface is visible.
