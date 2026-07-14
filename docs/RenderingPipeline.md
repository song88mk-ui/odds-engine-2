# Rendering Pipeline (Event → Pixels)

How an odds-engine-2 event ends up as a visible change in the browser. odds-engine-2 **renders
nothing itself** (headless workers); this documents the **downstream** pipeline so an OEv2 engineer
understands the consequences of what they publish. Deep Blazor internals live in
`platform-web/docs/RenderingPipeline.md`; this is the OEv2-anchored view. Tags: **[V]** odds-engine-2,
**[Vpw]** platform-web.

## The pipeline (push update)

```
OEv2 publishes OnOddsChange (snapshot)                                    [V]
   → /platform hub routes to group endpoint:{cc}:sports:{SpId}:…          [V/gateway]
      → platform-web HubConnection.On("OnOddsChange") handler             [Vpw]
         → mutate server-side circuit state (fixture lists)               [Vpw]
            → bump Counter fields (force child change-detection)          [Vpw]
               → RenderComponent = true                                   [Vpw]
                  → InvokeAsync(StateHasChanged)  (marshal to circuit)    [Vpw]
                     → Blazor builds render tree + diffs                  [Vpw]
                        → DOM patch sent over /_blazor WebSocket          [Vpw]
                           → browser applies patch; CSS odds-direction animation [Vpw]
```

Because platform-web is **Blazor Server**, rendering happens **on the server**; the browser only
receives a minimal DOM diff over the `/_blazor` circuit. There is no client-side re-render of
component logic.

## What OEv2 controls that affects rendering — [V]
- **What** renders: the message **payload** (which markets/selections, status, odds in 4 formats
  `DeOd`/`MaOd`/`HoOd`/`InOd`, `IsAc`, `MaStId`).
- **Where** it renders: the **routing key / hub method / group** (MainMarkets vs AllMarkets vs
  betslip vs top-markets) determines which UI surface and which subscribed clients get it.
- **How often**: OEv2's publish frequency directly drives UI render frequency — every state-changing
  message triggers a `StateHasChanged` on every subscribed circuit [Vpw]. High odds churn = high
  render load. (Payload size matters too — abbreviated fields keep frames small; 5 MB hub cap.)

## Render-gating on the UI side (so you know what dampens it) — [Vpw]
- **`ShouldRender()` override** in `BasePage`/`BetRecommendation` gates re-render on a
  `RenderComponent`/`_shouldRender` flag; `OnAfterRenderAsync` resets it to false. This prevents a
  render storm from unrelated state changes.
- **`Counter` pattern**: handlers increment `leagueFixture.Counter` / `fixture.Counter`; child
  components take these as `[Parameter]`, so Blazor's change detection re-renders only the affected
  fixtures/selections rather than the whole list.
- **Odds-direction animation**: selection components compare new vs previous odds and apply an
  up/down CSS class; the indicator resets after `ODDS_UPDATE_DIRECTION_RESET_IN_MILISECONDS = 3000`.
  Style is `OddsUpdateType.Corner` (OG) or `.Border` (default).
- **Connection cap**: `Custom:MaximumConnections = 300` per instance; over-limit users are redirected
  and `/healthcheck` returns 500 so the LB sheds load.

## Component render targets per OEv2 message — [Vpw]
| OEv2-originated event | Hub event | Handler | Components re-rendered |
|---|---|---|---|
| OnOddsChange (main) | `OnOddsChange` | `OnOddsChangeAsyncV2`/`OnOddsChangeAsync` | fixture-list `Fixture`/`League`/`Selection` |
| OnOddsChange (all/betslip) | `OnBetslipOddsChange` | `OnBetSlipOddsChangeAsync` | `BetSection` bet-slip selections |
| OnOddsChange (top markets) | `OnTopMarketsOddsChange` | `BetRecommendation` handler | `BetRecommendation` |
| BetStopFixture | `OnBetStopFixture` | `OnBetStopFixtureAsync` | fixture row (disable bet buttons) / remove |
| BetStopGlobal | `OnBetStopGlobal` | `OnBetStopGlobalAsync` | global bet-stop banner / disabled state |
| (settlement→) BetStatus | `OnBetStatus` | `OnBetStatusChangeAsync` | `BetSection` BetSlip status |
| CashOutChange | `OnCashOutChange` | `OnCashOutChangeAsync` | `BetSection` MyBets cashout value |
| WalletChange | `OnWalletChange` | `OnWalletChangeAsync` | header balance |

See [ComponentDataFlow.md](./ComponentDataFlow.md) for the full message→state→component map.

## Initial-render (read path) — [Vpw]
On load, `_Host.cshtml` server-prerenders the shell (`ServerPrerendered`), the circuit starts, and
`OnInitializedAsync` fetches catalogue data (REST → Mongo authored by OEv2) before the first
interactive render. A **pre-render guard** (`PreRenderService.IsPreRendering`) suppresses API/JS
during SSR so nothing runs twice.

## Implications for OEv2 work — [V] (practical guidance)
- **Throttling/coalescing at the source matters**: every published snapshot is a server render on
  every subscribed circuit. If OEv2 emits redundant `OnOddsChange` (no real delta), it still costs UI
  renders. Coalescing or change-detection before publish reduces UI load.
- **Choosing the right hub method/group** keeps updates off surfaces that don't need them.
- **Payload minimization** (the abbreviated schema) keeps frames under the hub size cap and reduces
  serialization cost on both ends.
- OEv2 cannot influence render *gating* (that's UI) but it owns render *triggering volume*.
