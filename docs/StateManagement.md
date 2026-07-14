# State Management (platform-web)

> Summary of **platform-web's** Blazor state model — odds-engine-2 has no UI state (its persistence
> state is in [StateFlow.md](./StateFlow.md)/[Caching.md](./Caching.md)). Authoritative:
> `platform-web/docs/StateManagement.md`. Tags: **[Verified-pw]** / **[Doc-pw]**.

## Pattern: `BaseState<T>` + `INotifyPropertyChanged` — [Verified-pw]
State containers extend `States/Base/BaseState.cs` (implements `INotifyPropertyChanged`). Assigning
the `.Model` property fires `OnPropertyChanged()` **on reference change** (not field mutation). Layouts
subscribe each state's `PropertyChanged` and turn it into `await InvokeAsync(StateHasChanged)`
(`LayoutBase.InitializeStates`), so a model swap re-renders the tree; components then gate their own
re-render via `ShouldRender()`.

## The four state containers — [Verified-pw]
| State | Model | Holds |
|---|---|---|
| `OperatorState` | `OperatorModel` | operator/session: clientId, operator token, language, currency, minute offset |
| `TranslationState` | `Dictionary<string,string>` (+ `GetValue()`) | localized UI strings |
| `PageState` | `PageModel` | current URL/scope + layout variant (AL/EL2) |
| `DeviceState` | `DeviceModel` | IsMobile/IsMedium/IsLarge + screen width |

Injected globally via `_Imports.razor` (`@inject`), used as `_operatorState`/`_translationState`/etc.

## DI lifetimes — [Doc-pw]
- **Scoped (per circuit = per browser tab):** all four states, `ToastService`, `ModalService`,
  `CurrencyService`, `ConnectedUser`, `HubConnection`, `PlatformRepository`.
- **Singleton (per server process):** `ConnectedUserList` — counts active circuits to enforce the
  `MaximumConnections` cap (300) and drive the `/healthcheck` load-shedding.
- Registered via `AddClientStates/AddClientModels/AddClientServices/AddClientRepositories/
  AddHubConnectionBuilder` in `Program.cs`.

## Working-set state (instance fields, not containers) — [Doc-pw]
The live data sets live as **instance fields** on `Index`/`BasePage`: `LiveLeagueFixtures`,
`PrematchLeagueFixtures`, `FavoriteLeagueFixtures`, `OutrightsFixtures`, `FixtureDetailsModel`,
`BetSectionModel` (bet slip + MyBets), `PlayerModel`. These are mutated in place by SignalR handlers
and REST loads. Cascading values: `ClientInfo` (fixed, from `App.razor`), `OddsUpdateType`,
`CurrentSportModel`.

## How OEv2 data enters UI state — [Doc-pw] (the cross-repo link)
SignalR handlers registered in `BasePage`/`Index.SubscribeAsync` mutate the working-set fields and
balance, set `RenderComponent = true`, and call `InvokeAsync(StateHasChanged)` (required — handlers
run on background threads):
| OEv2-originated event | State mutated |
|---|---|
| `OnOddsChange` | `Live/PrematchLeagueFixtures` (odds/status), `Counter++` |
| `OnBetStopFixture/Global` | `fixture.IsBetStop` / `BasePage.IsBetStopGlobal` |
| `OnBetStatus` | bet in `BetSectionModel.BetSlip` (by `BeId`) |
| `OnCashOutChange` | bet in `MyBets` (by `BeId`) |
| `OnWalletChange` | `PlayerModel.Balance` |
Full map: [ComponentDataFlow.md](./ComponentDataFlow.md); state ladder: [StateFlow.md](./StateFlow.md).

## Persistence vs reset — [Doc-pw]
- **Reset on navigation** (`LoadPage`): fixture lists, league filters, boosted odds, current ids.
- **Persist for circuit lifetime:** `BetSectionModel`, `OperatorState`, wallet/cashout subscriptions.
- **Survive transport reconnect:** entire circuit state retained; only the socket reconnects (no re-init).

## Browser-side storage — [Doc-pw]
Minimal: `ProtectedSessionStorage` is injected in a few components (e.g. active line-view session);
not a primary state mechanism. Most state is server-side circuit state. (odds-engine-2: N/A — no browser.)

## Contrast with odds-engine-2
OEv2 has **no per-user/session state** — its "state" is the durable Mongo catalogue + in-process
caches + the non-thread-safe global bet-stop flag ([StateFlow.md](./StateFlow.md),
[Caching.md](./Caching.md)). The UI's per-circuit, `INotifyPropertyChanged`-driven model is a
different paradigm entirely.
