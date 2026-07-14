# Component Data Flow

Maps odds-engine-2 outputs to the UI components that consume them — the "last mile" of each flow.
This is the OEv2-anchored view; the full Blazor component hierarchy is in
`platform-web/docs/ComponentDataFlow.md` / `ComponentHierarchy.md`. Tags: **[V]** odds-engine-2,
**[Vpw]** platform-web, **[Vpf]** platform-final.

## End-to-end channel map (which OEv2 output drives which UI surface)

| OEv2 publish (routing key / hub method) | Hub event → handler [Vpw] | UI state mutated | Component(s) rendered |
|---|---|---|---|
| `r.oe.sb.engine.onoddschange` → `PublishOddsChangeForMainMarkets` | `OnOddsChange` → `OnOddsChangeAsyncV2`/`OnOddsChangeAsync` | `Live/PrematchLeagueFixtures`, `Counter++` | `FixtureList`→`League`→`Fixture`→`Selection` |
| `r.oe.sb.engine.onoddschange` → `PublishOddsChangeForAllMarkets` | `OnBetslipOddsChange` → `OnBetSlipOddsChangeAsync` | `BetSectionModel.BetSlip` odds | `BetSection` (bet slip rows) |
| (top-markets group) | `OnTopMarketsOddsChange` → `BetRecommendation` handler | recommendation odds | `BetRecommendation` |
| `r.oe.sb.engine.onbetstopfixture` → `PublishBetStopFixture` | `OnBetStopFixture` → `OnBetStopFixtureAsync` | `fixture.IsBetStop`; maybe `RemoveFixture` | `Fixture` row (bet buttons disabled/removed) |
| `r.oe.sb.engine.onbetstopglobal` → `PublishBetStopGlobal` | `OnBetStopGlobal` → `OnBetStopGlobalAsync` | `BasePage.IsBetStopGlobal` | global banner / disabled betting |
| (settlement→platform) `r.bet.betstatus.s88` → `PublishBetStatus` | `OnBetStatus` → `OnBetStatusChangeAsync` | bet in `BetSlip` by `BeId` | `BetSection` (status) |
| `r.bet.cashoutchange.s88` → `PublishCashOutChange` | `OnCashOutChange` → `OnCashOutChangeAsync` | bet in `MyBets` by `BeId` | `BetSection`/`BetHistory` (cashout) |
| `r.wallet.s88` → `PublishWalletChange` | `OnWalletChange` → `OnWalletChangeAsync` | `PlayerModel.Balance` | header/balance |
| (favorites group) | favorites push | favorite fixtures | favorites surface |

> The `r.bet.*`/`r.wallet.*` events originate in **platform** services; `OE.Websocket.Service` is the
> relay that turns them into hub pushes. The `r.oe.sb.engine.*` events originate in **OEv2**.

## Read-path data flow (REST → component)
```
Component OnInitializedAsync/OnParametersSetAsync
   → IPlatformRepository.GetX()  (scoped HttpClient, x-api-key/x-operator-token headers, 2× retry, 30s cache)  [Vpw]
      → platform-final v1/sportsbook/*  → reads MongoDB Sportsbook (OEv2-authored)  [Vpf]
         → BaseResponseDTO<T> → ResultExtensions.ToResult<T> (case-insensitive)  [Vpw]
            → assign to state container / instance field (List<LeagueFixtureModel>, etc.)
               → PropertyChanged → StateHasChanged → render
```
Endpoint→component examples [Vpw/Vpf]:
- `GetSportsTree` (`v1/sportsbook/sports`) → `SportsTreeList`/`SportsCarousel` → sports navigation.
- `GetLiveFixtures`/`GetTodayFixtures`/`GetFixturesByLeague` → `Live/PrematchLeagueFixtures` →
  `FixtureList`.
- `GetFixtureDetails` (`v1/sportsbook/activefixtures/details`) → `FixtureDetailsModel` →
  `FixtureDetails` markets/selections.
- `GetSelectorMarketTemplates` → which markets each sport shows (drives market column layout).
- `GetTranslations` → `TranslationState` → every component's labels via `GetValue("key")`.
- `GetBetConfig` → `BetSection` stake limits / parlay/bet-builder/priceboost rules.
- `GetWalletBalance` → header balance.

## State containers feeding components — [Vpw]
- `OperatorState` (session/tenant), `TranslationState` (labels), `PageState` (scope/url),
  `DeviceState` (responsive) — injected/cascaded into components; `INotifyPropertyChanged` drives
  re-render.
- Instance fields on `Index`/`BasePage`: `Live/PrematchLeagueFixtures`, `FavoriteLeagueFixtures`,
  `OutrightsFixtures`, `FixtureDetailsModel`, `BetSectionModel` — the live working set.

## Component tree (data-bearing path) — [Vpw]
```
Layout (MainLayout / Euro2Layout)            ← OperatorState, TranslationState, PageState (cascading)
 └─ Page (Index / EL2 Live / FixtureDetails)
     ├─ Header                               ← balance (OnWalletChange), sports carousel
     ├─ SportsTree / filters                 ← GetSportsTree, GetSelectorMarketTemplates
     ├─ FixtureList                          ← Live/PrematchLeagueFixtures
     │   └─ League                           ← leagueFixture.Counter
     │       └─ Fixture                      ← fixture.Counter, IsBetStop
     │           └─ Selection                ← Decimal/HK/Indo/Malay odds, IsActive, MaStId (suspended)
     ├─ FixtureDetails                       ← FixtureDetailsModel (full markets/selections)
     ├─ BetSection (bet slip + MyBets)       ← OnBetslipOddsChange, OnBetStatus, OnCashOutChange
     ├─ BetRecommendation                    ← OnTopMarketsOddsChange + GetBetRecommendations
     └─ PriceBoost / BossBoosts              ← GetBoostedFixtures (+ boosted odds fields)
```

## Field-level binding (odds tick → selection cell) — [Vpw]
`UpdateMarketsAndSelections` maps each `OutgoingSelection` onto the `SelectionModel`:
`DeOd→DecimalOdds`, `HoOd→HongkongOdds`, `InOd→IndonesianOdds`, `MaOd→MalaysianOdds`,
`MaStId→MarketStatusId`, `IsAc→IsActive`. The `Selection` component renders the format chosen for the
operator and applies suspended styling when `MarketStatusId == Suspended`. These four odds formats
trace straight back to OEv2's `ClientOdds` computation (UOF margin/format logic) and the
`OnInternalOddsChangeMessage` shape (see [SharedLibraries.md](./SharedLibraries.md)).

## Why this matters to OEv2 work
- A field added to an OEv2 message is invisible until a UI component binds it — and, under MessagePack
  `ContractlessStandardResolver`, **renaming a property changes the wire key**, so OEv2 and the UI
  must deploy together on contract changes [Vpw]. (See [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md).)
- Choosing the hub method/group (main vs all vs top-markets) decides which component above reacts.
- Suspended/inactive state must be set correctly (`MaStId`/`IsAc`) or the UI will keep bet buttons live.
