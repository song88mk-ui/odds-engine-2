# Request / Event Lifecycle (End-to-End)

Narrative companion to [SequenceDiagrams.md](./SequenceDiagrams.md). Traces each flow across all
layers without stopping at service boundaries. Tags: **[V]** odds-engine-2, **[Vpw]** platform-web,
**[Vpf]** platform-final, **[L]** likely, **[U]** unknown.

> **Two kinds of "request" in this system.** odds-engine-2 itself is **event-driven** (no HTTP
> request lifecycle of its own — see [MiddlewarePipeline.md](./MiddlewarePipeline.md)). The
> end-to-end flows are therefore either **push** (provider event → … → browser) or **read/request**
> (browser → platform API → MongoDB that OEv2 authored). Both are traced below.

## The two transport layers (don't conflate them)
1. **`/_blazor` circuit** — WebSocket between **browser ↔ platform-web server** (Blazor Server UI
   sync, binary protocol, 5 MB max, 15 s keep-alive). [Vpw]
2. **`/platform` hub** — SignalR on a **separate gateway** (`Platform.Websocket.API2`); both
   `OE.Websocket.Service` (publisher) and the platform-web **server** (subscriber) are clients.
   MessagePack (or JSON), WebSockets-only, auto-reconnect. [V/Vpw/Vpf]

Because platform-web is **Blazor Server**, component code runs on the **server**; the browser only
receives DOM diffs. So a push update lands in platform-web's server process first, mutates
server-side state, and is then projected to the browser over `/_blazor`.

---

## Flow 1 — Live odds update (push; the defining flow)
**Origin → catalogue:** bet-genius publishes `r.bg.receiver.onmarketchange` (or a UOF source publishes
`r.uof.*`) [Vpf/L] → `OE.Adapter.Service` deserializes, normalizes, and republishes to
`r.oe.adapter.onmarketchange.<NN>` where `<NN>` = sticky shard from the provider fixture id [V].
**Catalogue write + fan-out:** `OE.Sportsbook.Service(NN)` upserts Market/Selection into MongoDB
(unordered BulkWrite), enriches names from SQL translations via MemCache, and publishes the canonical
`OnOddsChange` to `r.oe.sb.engine.onoddschange` [V].
**Push to UI:** `OE.Websocket.Service` consumes it and, depending on the market-template filter,
calls `PublishOddsChangeForMainMarkets` or `…AllMarkets` on the `/platform` hub, targeting group
`endpoint:{clientCode}:sports:{SpId}:livefixtures` (and related) [V/Vpf].
**UI update:** the hub routes to the platform-web server circuits subscribed to that group;
`HubConnection.On<OddsChangeFixtureMessageModel>("OnOddsChange")` → `OnOddsChangeAsyncV2`
(Index) / `OnOddsChangeAsync` (BasePage) updates `LiveLeagueFixtures`/`PrematchLeagueFixtures`,
increments `Counter` fields, sets `RenderComponent=true`, and calls `InvokeAsync(StateHasChanged)` [Vpw].
**Render:** Blazor diffs; `Fixture`/`League`/`Selection` components re-render; the browser receives a
DOM patch over `/_blazor`; the selection shows an up/down animation reset after 3000 ms [Vpw].
See [RenderingPipeline.md](./RenderingPipeline.md) for the render half in detail.

## Flow 2 — Initial application load (read)
Browser GET → Kestrel middleware (`UseStaticFiles → MaintenanceMiddleware → UseRouting →
MapFallbackToPage("/_Host")`) [Vpw] → `_Host.cshtml` builds `ClientInfo`, loads CDN/operator config,
renders `App` with `ServerPrerendered`. Browser loads `blazor.server.js`, upgrades `/_blazor`,
server creates the **circuit + scoped DI** (`HubConnection`, `PlatformRepository`, state containers)
[Vpw]. A **pre-render guard** (`PreRenderService.IsPreRendering`) short-circuits lifecycle methods
during SSR [Vpw]. Then `Layout.OnInitializedAsync` → `Index.OnInitializedAsync` runs `Task.WhenAll`:
`GetTranslations`, `GetSportsTree`, `GetSelectorMarketTemplates`, `LoadWalletBalance`,
`LoadAnnouncements` — all server-side REST to platform-final, which **reads MongoDB** (the catalogue
OEv2 wrote) [Vpw/Vpf]. Finally `SubscribeAsync()` starts the `/platform` `HubConnection` and joins
groups. Components render with initial data.

## Flow 3 — Fixture / market list load on navigation (read + resubscribe)
`NavigateTo(?sportId&scope)` → for the AL layout `Index` stays mounted; `OnParametersSetAsync` →
`LoadPage()` calls `DisconnectChannel()` (UnSubscribe previous groups, preserving wallet/cashout),
nulls the fixture lists, fetches the scope's data (`GetLiveFixtures`/`GetTodayFixtures`/
`GetFixturesByLeague`/`GetFixtureDetails`), then `ConnectToChannelAsync(SubscribeToEndpoint, key)`
joins the new group(s) so subsequent updates arrive via Flow 1 [Vpw]. EL2 routes mount/unmount
distinct components. **Market loading** is part of the fixture payload (`FixtureResponseDTO` carries
markets+selections); fixture **details** add the full market set via `v1/sportsbook/activefixtures/details`.

## Flow 4 — Bet placement → settlement (cross-repo round trip)
Browser places a bet → platform-web `POST v1/bet/place` → platform validates against the catalogue
(stake/odds/bet-stop/`BetConfig`) and writes the Bet (SQL) [Vpf]. When the event settles,
`OE.Sportsbook.Service` emits per-selection `OnBetSettlement` → `r.oe.bet.settlement`, consumed by
platform `Settlement.Service`, which composes the **bet outcome** (`BetStatus` Won/Lost/Void via
`VoidFactor`) and updates the wallet [V/Vpf]. platform then publishes `OnBetStatus`/`OnWalletChange`
(`r.bet.betstatus.s88`/`r.wallet.s88`) → `OE.Websocket.Service` relays them to the user's groups
(`onbetstatus:{cc}:{userId}`) → UI updates BetSlip/MyBets/balance [V/Vpw]. OEv2 owns settlement
*signals*; bets/wallet are platform's (see [DomainModel.md](./DomainModel.md)).

## Flow 5 — Cashout value update (push)
platform `CashOut.Service` publishes `r.bet.cashoutchange.s88` → `OE.Websocket.Service`
`PublishCashOutChange` → group `oncashoutchange:{cc}:{userId}` → `OnCashOutChangeAsync` updates the
matching bet's cashout fields in `MyBets` [V/Vpw]. The cashout **action** itself is a platform REST
call (`POST v1/bet/cashout`).

## Flow 6 — Translation (OEv2 read path REMOVED 2026-06-30) + platform REST path
**(Corrected 2026-06-30: translations removed from pipeline.)** `OE.Sportsbook.Service` **no longer
reads SQL `SportsbookDynamic`**: `LoadTranslationsAsync`/`MapTranslations` were deleted, the `*Tr`
lists were removed from the published messages, and the `sb:translation:*` MemCache is no longer
populated (`TranslationRepository`/`TranslationContext` retained in DI but never resolved). So OEv2 no
longer embeds translations into any message and the `OnDynamicTranslationChange` handler now purges an
empty/unused cache — effectively a **no-op** (handler retained) [V].
**Surviving path:** platform-web calls its own REST `GetTranslations()` (30 s in-process cache) into
`TranslationState`, accessed in markup via `_translationState.GetValue("key")` [Vpw]. Language chosen
from the `languageCode` query param (default EN) → `x-language-code` header [Vpw]. With the wire
`*NaTr` gone, the UI relies on this REST path (or falls back to default-language names like `SpNa`/`FiNa`).

## Flow 7 — Authentication (OGAuth)
Browser → `/play?…&signature` → platform-web `OGAuth.OnParametersSetAsync` computes an MD5-HMAC over
the request fields with the operator's `PrivateAPIKey` and compares to `signature`; on match it sets
OG headers and calls `AuthenticateOG` (REST), then navigates into the app; on mismatch →
`/unauthorized` [Vpw]. **No token is attached to the SignalR connection** — user scoping is by hub
group; REST carries `x-api-key`/`x-operator-token`/`x-og-*` [Vpw]. **odds-engine-2 has no inbound
auth** ([Authentication.md](./Authentication.md)); its only credentials are outbound.

## Flow 8 — User navigation (state transitions)
AL layout: same-page param change → `OnParametersSetAsync` → `LoadPage()` resets fixture/filter state
but **preserves** bet slip and `OperatorState` for the circuit lifetime [Vpw]. EL2: route change
mounts/unmounts components; `Dispose` sends `UnSubscribeToEndpoint`. See
[StateFlow.md](./StateFlow.md) for what persists vs resets.

## Flow 9 — Reconnect
`/platform` reconnect (`WithAutomaticReconnect`) → `Reconnected` re-invokes all subscription methods
for `_connectedChannels` (+ BetStatus if authed) [Vpw]; **missed updates are not replayed** — stale
until next push/navigation [Vpw]. `/_blazor` drop → 3×5 s retries → else `location.reload()` →
full `OnInitializedAsync` again [Vpw]. The Blazor **circuit preserves server-side state** across a
transport blip (no re-init) [Vpw].

---

## REST surface OEv2 data is served through (platform-final) — [Vpf]
Read endpoints platform-web calls (all return `BaseResponseDTO<T>`, 2× retry, 30 s client cache on
several): `v1/sportsbook/sports`, `…/selectormarkettemplates`, `…/sports/{live,today,daily,outright}fixtures`,
`…/sports/segments[/leagues]/fixtures`, `…/activefixtures/details`, `…/selectiondetails`, `…/search`,
`…/translations`, `v1/bet/config`, `v1/wallet/balance`. All ultimately read the **MongoDB Sportsbook**
catalogue authored by OEv2 (plus SQL Betting/Wallet for bet/wallet data). Auth via `x-api-key` +
`x-operator-token` + (optional) `x-og-*` headers.
