# Navigation & Routing (platform-web)

> Summary of **platform-web's** Blazor routing for cross-repo orientation — odds-engine-2 has no UI.
> Authoritative: `platform-web/docs/Navigation.md` + `UIArchitecture.md`. Tags: **[Verified-pw]** /
> **[Doc-pw]**.

## Router — [Verified-pw]
`App.razor` hosts a Blazor `Router` that scans the app assembly for `@page` routes, renders matches
via `RouteView` with `DefaultLayout = MainLayout`, and wraps the tree in a fixed
`CascadingValue<ClientInfo>`. Unmatched server paths fall through `MapFallbackToPage("/_Host")`
(`Program.cs`); unmatched client routes render `NotFoundPage`.

## Two layouts, two route sets — [Verified-pw]
| Layout | Routes | Component |
|---|---|---|
| **MainLayout (AL)** | `/`, `/{ParamScope}`, `/segment`, `/league`, `/fixture`, `/maintenance` | `Index` |
| | `/play` | `OGAuth` (HMAC-MD5 sign-in) |
| | `/result` | `Result` (standalone bet result) |
| | `/unauthorized` | `AuthenticationErrorPage` (401) |
| | `/notfound` | `NotFoundPage` (404, also Router NotFound) |
| **Euro2Layout (EL2)** | `/el`, `/{CurrentScope}`, `/el/segment` | `Index2` |
| | `/el2/live` | `Live` |
| | `/el2/upcoming`, `/el2/boss-boosts` | `Upcoming`, `BossBoosts` (stubs) |
| | `/el2-fixture-details` | `FixtureDetails` |

EL2 pages set `@layout Euro2Layout`; AL pages inherit the default. [Verified-pw]

## Query-param-driven navigation (the AL model) — [Doc-pw]
The AL `Index` page is a **single mounted component**; "navigation" between scopes/sports/leagues
changes the **path segment + query parameters**, not the component:
- `[SupplyParameterFromQuery]` params on `BasePage`: `clientId`, `currencyCode`, `languageCode`,
  `minuteOffset`, `operatorToken`, `userId`, `theme`, `sportId`, `segmentId`, `leagueId`,
  `fixtureId`, `date`, plus OG params (`poolId`, `betLimit`, `timestamp`, `signature`, `gameCode`).
- `NavigationManagerExtensions` preserves existing query params and replaces only the five nav keys
  (`sportId/segmentId/leagueId/fixtureId/date`), then `NavigationManager.NavigateTo(uri)` (no force-load).
- On each navigation Blazor calls `Index.OnParametersSetAsync`, which reads `ParamScope`, sets the
  active tab/view, and calls `LoadPage()` (resets fixture lists, fetches the scope's data,
  re-subscribes hub groups). See [RequestLifecycle.md](./RequestLifecycle.md) Flow 3/8.
- EL2 routes are real route changes (mount/unmount distinct components).

## Scopes (AL) — [Doc-pw]
`Today`, `Live`, `Early`, `Outrights`, `Favorites`, `Priceboost`, plus drill-downs `Segment` /
`League` / `Fixture`. Each maps to a data fetch (`GetTodayFixtures`/`GetLiveFixtures`/… — see
[PlatformApi.md](./PlatformApi.md)) and a hub group (`endpoint:{cc}:sports:{SpId}:…`).

## Disposal / re-subscribe on navigation — [Doc-pw]
- `LoadPage()`/`DisconnectChannel()` unsubscribe previous hub groups (preserving wallet/cashout),
  null the fixture lists, then subscribe the new scope's groups.
- Bet slip + `OperatorState` persist for the circuit lifetime; fixture/filter state resets.
- EL2 `Dispose` sends `UnSubscribeToEndpoint`. See [StateFlow.md](./StateFlow.md).

## Parent-frame navigation bridge — [Doc-pw]
platform-web typically runs inside an operator iframe. Navigation can be driven from the parent via
`postMessage` (`navigate-to`) routed to `.NET` (`indexReference.invokeMethodAsync('HandleNavigateTo', …)`),
and the child emits `on-navigate-to`/`on-page-height-change` back to the parent. See
[JavaScriptInterop.md](./JavaScriptInterop.md).

## odds-engine-2 relevance
Navigation determines **which hub groups** a circuit joins, hence which OEv2 pushes it receives. The
group-name contract (`endpoint:{clientCode}:…`, `fixturedetails:{FiId}`) is shared with
`OE.Websocket.Service` — see [WebSocket.md](./WebSocket.md).
