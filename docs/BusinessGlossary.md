# Business Glossary — Sportsbook Domain

Domain terms as used in odds-engine-2 (and across the platform). For the **abbreviated field codes**
(`FiId`, `MaStId`, …) see [SharedLibraries.md](./SharedLibraries.md); this glossary is **concept-level**.
Tags: [Verified] in code, [Likely] inferred.

## Hierarchy & catalogue
- **Sport** — top-level category (Soccer, Basketball, Tennis…). `SpId`. [Verified]
- **Segment** — region/category grouping under a sport (e.g. "England", "International"). `SegId`.
  Roughly the "Competition group" tier. [Verified]
- **League** — a competition/tournament under a segment (e.g. "Premier League"). `LeId`. The domain's
  "Tournament"/"Competition". [Verified]
- **Fixture** — a single bettable event: a **Match** or an **Outright**. `FiId`. The aggregate root. [Verified]
- **Match** — a fixture between two participants (Home vs Away). `FiTyId=Match`. [Verified]
- **Outright** — a non-match fixture (e.g. "Tournament Winner"); candidates are selections.
  `FiTyId=Outright`. [Verified]
- **Market** — a betting market on a fixture (e.g. "Match Result", "Total O/U"). `MaId`. [Verified]
- **Market Template** — the **type** of a market (`MaTeId`/`MaTeNa`). Defines what the market means
  and its selection set. [Verified]
- **Market Group** — UI grouping of related markets (by category/template). **Not a stored OEv2
  entity**; a platform/UI concept (markets carry `MaSoOr` sort order). [Likely]
- **Selection** — an outcome within a market (e.g. "Home", "Over 2.5"). `SeId`. Carries the odds. [Verified]
- **Selection Template** — the **type** of a selection (`SeTeId`/`SeTeNa`). [Verified]
- **Participant** — a competitor: team (Home/Away on a fixture, `HoId`/`AwId`) or player
  (player-props, via selection templates). Embedded, not a standalone entity. [Verified]
- **Line / Handicap** — the threshold/handicap of a market (`Li`), e.g. O/U 2.5, AH −1.5. [Verified]

## Odds & pricing
- **Odds** — the price of a selection (`Od`, decimal). [Verified]
- **Probability** — implied probabilities (`Pr` and won/lost/void/half variants) used for settlement
  math. [Verified]
- **Margin** — bookmaker overround added to fair odds (+2% hardcoded in the external feed). [Verified]
- **Odds format** — presentation: Decimal / Malaysian / Hong Kong / Indonesian (`ClientOdds`,
  `DeOd`/`MaOd`/`HoOd`/`InOd`); "Asian client" toggles non-decimal formats. [Verified]
- **Boosted Odds / Price Boost** — promoted, enhanced odds for **specific clients**, with per-client
  display names (`IsBo`, `ClBo`, `ClBoOd`, `ClBoSeNa`). [Verified]
- **Dead-heat factor** — payout reduction when multiple selections tie a result (`DeHeFa`). [Verified]
- **In-Play / Live** — betting while the event is live (`InPl`, `FixtureStatus.Live`). [Verified]
- **Prematch** — betting before kickoff (`FixtureStatus.Prematch`). [Verified]

## Status & lifecycle
- **Fixture Status** — match lifecycle (Prematch/Live/Ended/Closed/Cancelled/…); `FixtureStatus`. [Verified]
- **Market Status** — Active/Suspended/Inactive/Settled/Cancelled/HandedOver; `MarketStatus`. [Verified]
- **Selection Status** — active/settled outcome state (`SeStId`). [Verified]
- **Bet-stop** — suspension of betting, **global** (all) or **per-fixture** (`IsSu`); an overlay, not
  a status change. [Verified]
- **Settlement** — assigning results to selections after an event (`SettlementStatus`:
  Won/Lost/Void/HalfWon/HalfLost/Cancelled/CashedOut). [Verified]
- **Void / Void Factor** — refund of stake when a selection is voided (Zero/Half/One). [Verified]
- **Settled / HandedOver / Cancelled** — terminal market states (results in / transferred to another
  feed / voided). [Verified]

## Betting (mostly platform-side, contract in OEv2 enums)
- **Bet** — a customer wager on one or more selections; `BetStatus` (Waiting/Running/Won/Lost/Void/
  Rejected/Cancelled/CashedOut). Owned by platform. [Verified]
- **Bet Builder** — a custom multi-selection bet on the **same fixture** with combined pricing;
  OEv2 flags eligible market templates, platform prices/validates it. [Verified]
- **Parlay / Accumulator** — multi-selection bet across fixtures; platform. [Verified]
- **Cashout** — settling a bet early at an offered value; OEv2 owns **availability** (`CashoutStatus`),
  platform owns the **transaction**. [Verified]
- **Payable change** — change in payable amount pushed to the UI (`OnPayableChange`). [Verified]
- **Wallet** — customer balance; platform; OEv2/Websocket only forwards `OnWalletChange`. [Verified]

## Data & feed
- **Channel / Channel Type** — the provider feed source: **BG** (BetGenius, 0), **EM** (1), **OD** (2).
  `ChannelType`. [Verified] (EM/OD provider identities [Unknown])
- **Provider id vs Internal id** — upstream id (`PrFiId`/`PrMaId`/`PrSeId`) vs Song88 id
  (`FiId`/`MaId`/`SeId`). [Verified]
- **Specifier** — provider-format key for a fixture/market/selection (`FiSp`/`MaSp`/`SeSp`). [Verified]
- **Mapping** — provider→internal id resolution (`MarketMapping`/`SelectionMapping`); `IsMap` flags
  completeness on lookups. [Verified]
- **Lookup** — materialized dimension table for navigation/filtering (Sport/Segment/League/Market/
  Selection Lookups). [Verified]
- **Trading** — odds-feed management / client entitlement (SQL `TradingContext`,
  `ClientOddsFeedConfiguration`); also the upstream trading feed (bet-genius / Genius Sports). [Verified]
- **UOF (Unified Odds Feed)** — the outbound white-label odds feed produced by `OE.UOF.Client`. [Likely]
- **Translation** — a localized entity name (sport, segment, league, team, market template, selection),
  sourced from SQL `TranslationCache2` and carried through the pipeline as the `*Tr` lists / Mongo
  `*NaTr` fields. Owned by `OE.Adapter.Service` (preload + stamp) and `OE.Translation.Service` (SQL and
  Mongo writes). Distinct from platform-web's REST `GetTranslations()`, which serves static UI labels.
  [Verified]
- **Dynamic Translation** — legacy name for the above, surviving in the
  `ON_DYNAMIC_TRANSLATION_CHANGE` message type. ⚠️ Do not confuse it with `ON_TRANSLATION_CHANGE`:
  the former drives a **no-op** cache purge in Sportsbook, the latter is the **live** change path into
  `OE.Translation.Service`. [Verified]
- **Worklist** — the set of `TranslationCache2` rows with `PROCESSED = 0` awaiting the external
  translation pipeline. [Verified]

## Match state
- **Score** — current scoreline (`Sc`); per-period scores (`PeSc`). [Verified]
- **Current period / minute / set** — live clock/state (`CuPe`/`CuMi`/`CuSe`). [Verified]
- **Red cards** — `HoReCa`/`AwReCa`. [Verified]
- **Favorite** — which side is favored (`IsHoFa`; `FavoriteSelection`: Home/Away/None). [Verified]
- **Offered Live** — fixture has both match-state and trading-state licensed (`IsOfLi`). [Verified]

## Presentation & delivery
- **Sort order** — display ordering (`FiSoOr`/`MaSoOr`/`SeSoOr`). [Verified]
- **Livestream** — video for a fixture (`LiId`/`StUr`/`GeAl`), BG-only, geo-restricted. [Verified]
- **Favorites (user)** — a customer's bookmarked fixtures/teams (processed by Websocket → Mongo). [Verified]
- **Client / Client Code / Operator** — a tenant of the multi-tenant platform (e.g. `TSO`, `S88`);
  drives visibility (`ClCo`/`ClCoMa`), odds format, boosts, and per-client delivery instances. [Verified]
- **Top fixtures / Top leagues / Boosted fixtures** — curated UI surfaces fed by the catalogue +
  platform config. [Verified]

## Operational
- **e.central** — the central RabbitMQ topic exchange carrying all internal events. [Verified]
- **Instance / Shard (NN)** — a Sportsbook partition (00–10) handling a fixed slice of fixtures
  (10 = global bet-stop). [Verified]
- **Heartbeat** — 5s liveness ping (`r.oe.uof.heartbeat`). [Verified]
