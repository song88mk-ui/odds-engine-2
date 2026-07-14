# Component Hierarchy (platform-web)

> Summary of **platform-web's** Blazor component tree — odds-engine-2 has no UI/components.
> Authoritative: `platform-web/docs/ComponentHierarchy.md`. Tags: **[Verified-pw]** / **[Doc-pw]**.
> The OEv2-output → component mapping is in [ComponentDataFlow.md](./ComponentDataFlow.md).

## Two-skin structure — [Verified-pw]
Most components have **AL** and **EL2** variants under their folder (e.g.
`Components/Header/AL/AppHeader.razor` vs `Components/Header/EL2/HeaderContent.razor`,
`Components/MainMarketsTable/AL/` vs `…/EL2/`). Shared variants live under `Components/Shared/…`.

## Base classes — [Doc-pw]
- **`LayoutBase`** (`Shared/Base/LayoutBase.cs`) — base for both layouts; wires state containers,
  subscribes `PropertyChanged → InvokeAsync(StateHasChanged)`, enforces the connection cap
  (`ValidateConnection`), recovers the `ErrorBoundary` on navigation.
- **`BasePage`** (`Pages/Base/BasePage.cs`) — base for `Index`/`Index2`/EL2 pages; owns the hub
  connection, scope routing (`OnParametersSetAsync`), SignalR handlers, fixture-list/detail loading,
  the `RenderComponent`/`ShouldRender()` optimization, and `IAsyncDisposable` cleanup.

## AL tree (Index) — [Doc-pw] (abridged)
```
MainLayout
└─ Index (BasePage)
   ├─ Preloader / SessionExpired / CurrencyMismatch / UnexpectedError
   ├─ AppHeader (AL)  ──► Search (InputSearch, SearchResult, ConfirmModal), UserInfo, BettingRulesModal
   ├─ ToastComponent · AnnouncementsModal
   ├─ SideBar
   │  └─ BetSection ── Single/Parlay · MyBets…Item → MyBetsCashoutButton · BetHistory (Filter→DatePicker,
   │                    SingleBetItem/ParlayBetItem/BetBuilderItem, PartialCashoutModal)
   ├─ SubHeader (AL) · BetRecommendation → BetRecommendationItem → BetRecommendationSelection
   ├─ SportsCarousel (AL) → SportItem/SegmentItem/LeagueItem
   ├─ TableHeader (AL) · EarlyPresetDate · FilterGroup → LeagueFilter → Checkbox
   └─ [scope block]
      ├─ Sport (AL) → League (AL) → Fixture (AL) → Selection (AL)     # favorites grouping / lists
      ├─ Outrights → OutrightsMarket → OutrightsSelection
      └─ FixtureDetails (AL) → FixtureDetailsMarket → FixtureDetailsSelection
```

## EL2 tree (Live) — [Doc-pw] (abridged)
```
Euro2Layout
├─ HeaderContent (EL2) → UserBalance, SportsMenu (SportItem→SegmentItem→LeagueItem), GenericModal
└─ Live (BasePage)
   ├─ BetRecommendation → BetRecommendationItem → BetRecommendationSelection
   ├─ MainNavPill → PillButton ×3 (Live/Upcoming/PriceBoost)
   ├─ SportsCarousel (EL2) · MatchEndedModal · Dropdown / DropdownMultiSelect
   └─ CascadingValue<OddsUpdateType> → League (EL2) → Fixture (EL2) → Selection (EL2)
```

## The data-bearing spine (what renders OEv2 data) — [Doc-pw]
`Layout → Page → FixtureList/League → Fixture → Selection` renders the catalogue + live odds; the
**`Selection`** component is the leaf that shows the four odds formats (`DeOd/MaOd/HoOd/InOd`),
active/suspended styling, and the up/down animation. `BetSection` shows bet slip + my-bets +
cashout. `BetRecommendation`, `Outrights`, `PriceBoost`, `FixtureDetails` are the other data surfaces.
Mapping to OEv2 messages: [ComponentDataFlow.md](./ComponentDataFlow.md).

## Large-list rendering — [Doc-pw]
EL2/Index2 use Blazor `Virtualize` for large league/fixture sets; the `Counter` field on
fixture/league models is used as a `[Parameter]` to drive precise child change-detection (see
[Rendering.md](./Rendering.md)).

## Catalogue of component groups — [Doc-pw]
Headers (AppHeader/SubHeader/TableHeader/HeaderContent/UserBalance) · Navigation
(SportsCarousel/SportsMenu/SportsTree/MainNavPill/EarlyPresetDate) · Fixtures/Markets
(Fixture/League/Selection/FixtureDetails*) · Bets (BetSection, BetSelectionItem, BetHistory, MyBets*)
· Special (BetRecommendation, PriceBoost, Outrights, Search, FilterGroup) · UI primitives
(Dropdown, DropdownMultiSelect, PillButton, Checkbox, BorderedMessage, Preloader, ConfirmModal,
GenericModal, BetBuilderSwitch) · Placeholders (OddsTable/BetHistory/FixtureDetails/Outrights).
Reusable details: [SharedComponents.md](./SharedComponents.md).
