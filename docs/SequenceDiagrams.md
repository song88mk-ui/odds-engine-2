# Sequence Diagrams — End-to-End Flows

End-to-end traces through the full stack. Confidence tags: **[V]** = verified in odds-engine-2,
**[Vpw]** = verified in platform-web, **[Vpf]** = verified in platform-final, **[L]** likely,
**[U]** unknown. Companion narratives: [RequestLifecycle.md](./RequestLifecycle.md).

## Actors / layers
```
Browser ── Blazor circuit(/_blazor) ── platform-web (Blazor Server BFF)
   │                                         │  server-side HttpClient (REST)        │  HubConnection (SignalR client)
   │                                         ▼                                        ▼
   │                                  platform-final REST (platform-api)      /platform SignalR hub  ◄── OE.Websocket.Service
   │                                         │  reads                                 ▲                     (SignalR client, publisher)
   │                                         ▼                                        │ RabbitMQ r.oe.sb.engine.* / r.bet.* / r.wallet.*
   │                                  MongoDB Sportsbook  ◄── OE.Sportsbook.Service ──┘
   │                                                              ▲ RabbitMQ r.oe.adapter.#.NN
   │                                            OE.Adapter.Service ◄── bet-genius (r.bg.receiver.*) / UOF (r.uof.*)
```

> ⚠️ **Hub host note.** The `/platform` hub is a **separate gateway service** (Phase 1:
> `Platform.Websocket.API2`), **not** hosted inside platform-web — both `OE.Websocket.Service` and
> the platform-web server connect to it as clients. platform-web's own docs contradict themselves on
> this point; see [Unknowns.md](./Unknowns.md). Diagrams below use the resolved (separate-gateway) model.

---

## 1. Live odds update (the signature real-time flow) — push

```
bet-genius          OE.Adapter      OE.Sportsbook(NN)     RabbitMQ        OE.Websocket     /platform hub   platform-web      Browser
  │ r.bg.receiver.*    │                  │                e.central          │                 │            (server)            │
  ├───────────────────►│                  │                                   │                 │               │                │
  │        [Vpf/L]     │ normalize+shard  │                                   │                 │               │                │
  │                    ├─ r.oe.adapter.OnMarketChange.NN ─►│                  │                 │               │                │
  │                    │           [V]    │ upsert Market/Selection (Mongo)   │                 │               │                │
  │                    │                  │ enrich translations (SQL→MemCache)│                 │               │                │
  │                    │                  ├─ publish OnOddsChange ───────────►│                 │               │                │
  │                    │                  │   r.oe.sb.engine.onoddschange [V] │ consume         │               │                │
  │                    │                  │                                   ├─ PublishOddsChangeForMainMarkets/AllMarkets ─►│  │
  │                    │                  │                                   │   (client SendAsync) [V]        │ route to group │
  │                    │                  │                                   │                 ├─ OnOddsChange ►│ HubConnection.On│
  │                    │                  │                                   │                 │   group:       │ OnOddsChangeAsyncV2/
  │                    │                  │                                   │                 │   endpoint:S88:sports:1:livefixtures
  │                    │                  │                                   │                 │      [Vpw]     │ update fixture state
  │                    │                  │                                   │                 │               │ InvokeAsync(StateHasChanged)
  │                    │                  │                                   │                 │               ├─ Blazor diff ─►│ DOM patch
  │                    │                  │                                   │                 │               │   over /_blazor│ (odds + up/down anim)
```
Key points: market-template filter decides MainMarkets vs AllMarkets hub method [Vpf]; group name =
`endpoint:{clientCode}:...` (OE prefixes clientCode) [V/Vpw]; UI gates re-render via `RenderComponent`/
`ShouldRender` and resets the odds-direction animation after 3000 ms [Vpw].

---

## 2. Initial application load (read path) — request/response

```
Browser        platform-web (Kestrel + Blazor)                 platform-final REST        MongoDB
  │ GET /?clientId&token…   │                                          │                      │
  ├────────────────────────►│ UseStaticFiles → MaintenanceMiddleware → UseRouting            │
  │                         │ MapFallbackToPage("/_Host")  [Vpw]       │                      │
  │                         │ _Host.cshtml: ClientInfo, CDN config, CSS, App(ServerPrerendered)
  │◄── HTML + blazor.server.js ──┤                                     │                      │
  ├─ WS upgrade /_blazor ──►│ create circuit + scoped DI (HubConnection, PlatformRepository, States)
  │                         │ Layout.OnInitializedAsync → Index.OnInitializedAsync [Vpw]      │
  │                         │  Task.WhenAll:                                                   │
  │                         │   GetTranslations() ───────────────────►│ v1/sportsbook/translations
  │                         │   GetSportsTree() ─────────────────────►│ v1/sportsbook/sports ─► read Mongo (lookups/fixtures)
  │                         │   GetSelectorMarketTemplates() ────────►│ v1/sportsbook/selectormarkettemplates
  │                         │   LoadWalletBalance() (if authed) ─────►│ v1/wallet/balance     │
  │                         │◄─ BaseResponseDTO<T> (30s cache, 2x retry) [Vpw]                │
  │                         │ SubscribeAsync(): HubConnection.StartAsync → GetConnectionId
  │                         │   InvokeAsync("SubscribeToEndpoint", endpoint:…:livefixtures) ─► /platform hub
  │                         │ components render with initial data                              │
  │◄── DOM (over /_blazor) ─┤                                                                  │
```
The catalogue rendered here was authored by OEv2 into MongoDB; platform-final only **reads** it [Vpf].

---

## 3. Fixture / market list load on navigation — read + (re)subscribe

```
Browser    platform-web                                   platform-final     /platform hub
  │ click sport/scope │                                        │                 │
  ├──────────────────►│ NavigateTo(uri?sportId&scope)          │                 │
  │                   │ OnParametersSetAsync → LoadPage()       │                 │
  │                   │ DisconnectChannel(): UnSubscribeToEndpoint(prev keys) ───►│ leave groups
  │                   │ reset PrematchLeagueFixtures/LiveLeagueFixtures=null      │
  │                   │ GetLiveFixtures(sportId) ──────────────►│ v1/sportsbook/sports/livefixtures ─► Mongo
  │                   │◄─ List<LeagueFixtureModel> ─────────────┤                 │
  │                   │ ConnectToChannelAsync(SubscribeToEndpoint, sports:{id}:livefixtures) ─►│ join group
  │                   │ StateHasChanged → render list           │                 │
  │◄── DOM ───────────┤                                         │                 │
  │                   │ (subsequent odds updates arrive via Flow 1 to the joined group)
```
Fixture **details** (`v1/sportsbook/activefixtures/details`) and per-fixture subscription
(`endpoint:{cc}:fixturedetails:{FiId}`) follow the same shape [Vpw/Vpf].

---

## 4. Bet placement → settlement (cross-repo round trip)

```
Browser   platform-web   platform-final (Betting)     OE.Sportsbook   RabbitMQ   platform Settlement.Svc   /platform hub  Browser
  │ place bet │             │                              │             │              │                    │             │
  ├──────────►│ POST v1/bet/place ─────────►│ validate (stake/odds/betstop) write Bet (SQL)                  │             │
  │           │◄─ BetStatus=Running ─────────┤             │             │              │                    │             │
  │  ... event finishes; OE settles ...      │             │             │              │                    │             │
  │           │             │     OE.Sportsbook: OnMarketSettlement → per-selection OnBetSettlement          │             │
  │           │             │                              ├─ r.oe.bet.settlement ─────►│ resolve bet outcome│             │
  │           │             │                              │             │   (Won/Lost/Void, VoidFactor)      │             │
  │           │             │  platform: wallet credit (SQL) → publish OnBetStatus/OnWalletChange ──────────►│ Publish*     │
  │           │             │                              │             │              │   r.bet.betstatus.s88 / r.wallet.s88 →│ OE.Websocket → hub
  │           │◄──────────────────── OnBetStatus / OnWalletChange (group: onbetstatus:{cc}:{userId}) ───────►│ update BetSlip/MyBets, balance
```
OEv2 owns **selection settlement**; platform composes **bet outcome** + wallet; OE.Websocket relays
the resulting bet/wallet events back to the UI [V/Vpf]. (`r.bet.*`/`r.wallet.*` producers are platform.)

---

## 5. Cashout value update — push

```
platform CashOut.Svc ── r.bet.cashoutchange.s88 ──► OE.Websocket ── PublishCashOutChange ──► /platform hub ──► platform-web ──► Browser
                                                                    group: oncashoutchange:{cc}:{userId}     OnCashOutChangeAsync → update MyBets CaOuSt
```

## 6. Translation (OEv2 read path REMOVED 2026-06-30) + platform REST path
```
(Corrected 2026-06-30: translations removed from pipeline)
   OEv2 read path:  REMOVED — OE.Sportsbook no longer reads SQL SportsbookDynamic, no *Tr embedded in messages,
                    sb:translation:* MemCache never populated (TranslationRepository/Context orphaned in DI)
   invalidate:      OnDynamicTranslationChange → purges empty/unused MemCache → effectively a NO-OP (handler retained)
   surviving path:  platform-web GetTranslations() (REST, 30s cache) → TranslationState  ← sole UI translation source
```

## 7. Authentication (OGAuth) — no token on SignalR
```
Browser GET /play?…&signature  → platform-web OGAuth.OnParametersSetAsync
   → compute MD5-HMAC over fields with PrivateAPIKey → compare to signature
       match → PlatformRepository.AuthenticateOG (REST) → success → NavigateTo("/…operatorToken=…")
       mismatch/fail → NavigateTo("/unauthorized")
```
The SignalR `/platform` connection carries **no** auth token; user scoping is by group
(`{clientCode}:{userId}`) and REST calls carry `x-api-key`/`x-operator-token`/`x-og-*` headers [Vpw].
odds-engine-2 itself has **no inbound auth** (see [Authentication.md](./Authentication.md)).

## 8. Reconnect (transport resilience)
```
/platform drop  → HubConnection.WithAutomaticReconnect → Reconnected → re-invoke SubscribeToEndpoint/Favorites/Wallet/CashOut/BetStatus for _connectedChannels  [Vpw]
/_blazor drop   → Blazor reconnect (3×5s) → else location.reload() → full OnInitializedAsync again  [Vpw]
```
> Updates that occurred during a `/platform` outage are **not replayed** — data is stale until the
> next push or a navigation re-fetch [Vpw].
