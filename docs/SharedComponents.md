# Shared Components (platform-web)

> Summary of **platform-web's** reusable components/services — odds-engine-2 has no UI.
> Authoritative: `platform-web/docs/SharedComponents.md`. Tags: **[Verified-pw]** / **[Doc-pw]**.

## Layout bases & layouts — [Doc-pw]
- **`LayoutBase`** (`Shared/Base/LayoutBase.cs`) — shared layout behavior: state wiring,
  `PropertyChanged → InvokeAsync(StateHasChanged)`, connection-cap `ValidateConnection`,
  `ErrorBoundary.Recover()` on navigation.
- **`MainLayout`** (AL) — full-viewport container; `ErrorBoundary` around `@Body`; sets
  `PageState.Layout = AL`; header/sidebar rendered by the page, not the layout.
- **`Euro2Layout`** (EL2) — centered max-width layout with persistent SportsMenu sidebar +
  HeaderContent + bet-section container; **owns the `/platform` HubConnection** and exposes
  `LoadPage()/PrepareCurrentSportModel()/ConnectToChannel()/DisconnectToChannel()`; cascades itself
  as `Name="Layout"` so EL2 pages call its methods; sets `PageState.Layout = EL2`.

## Cross-cutting service-driven components — [Doc-pw]
| Service (Scoped) | Paired component | Purpose |
|---|---|---|
| `ToastService` (`OnToasterChanged`) | `ToastComponent` | non-blocking toast notifications |
| `ModalService` (`OnOpen(RenderFragment)/OnClose`) | `GenericModal` | overlay rendering arbitrary fragments (e.g. bet-builder confirm) |
| `CurrencyService` | (used by bet/wallet components) | currency formatting / redenomination |
| `PreRenderService` | (all pages) | `IsPreRendering` guard to skip JS/API during SSR |

## The BetSection (the most important shared component) — [Doc-pw]
`Components/BetSection/BetSection.razor(.cs)` — the bet slip + my-bets + bet history panel.
Single/Parlay tabs, bet placement/removal, cashout buttons, partial-cashout modal. Params include
`ActiveOddsType`, `BetSectionModel`, `PlayerModel`, `IsBetProcessing/Waiting`. Implements
`ShouldRender()` so constant odds pushes don't re-render the whole slip. Receives OEv2-originated
`OnBetslipOddsChange`/`OnBetStatus`/`OnCashOutChange` updates (see [ComponentDataFlow.md](./ComponentDataFlow.md)).

## Navigation components — [Doc-pw]
`SportsCarousel` (AL/EL2/Shared variants), `SportsMenu`/`SportsTree` (Sport→Segment→League drill-down,
EL2), `MainNavPill` + `PillButton` (scope tabs), `EarlyPresetDate` (Early-scope date presets).

## UI primitives — [Doc-pw]
`Dropdown`, `DropdownMultiSelect`, `PillButton`, `Checkbox`, `BorderedMessage`, `Preloader`,
`ConfirmModal`, `GenericModal`, `AnnouncementsModal`, `BettingRulesModal` (renders Markdown rules),
`BetBuilderSwitch`. Skeleton placeholders: `OddsTablePlaceholder`, `BetHistoryPlaceholder`,
`FixtureDetailsPlaceholder`, `OutrightsPlaceholder`.

## Error handling — [Doc-pw]
Both layouts wrap `@Body` in a Blazor `ErrorBoundary` whose `ErrorContent` renders `Error.razor`
(with a Reload link); `LayoutBase` recovers it on each navigation so a prior error doesn't stick.

## Shared render fragments — [Doc-pw]
`RenderFragmentCollections` provides a static library of reusable `RenderFragment`s used across
components.

## odds-engine-2 relevance
None directly — these are UI building blocks. The only cross-repo coupling is the **data/event
contract** (`Outgoing*` SignalR models + REST DTOs) the data-bearing components consume; that
contract is owned partly by OEv2 (see [Contracts.md](./Contracts.md), [WebSocket.md](./WebSocket.md)).
