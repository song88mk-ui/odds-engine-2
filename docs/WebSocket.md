# WebSocket / SignalR Integration

The real-time push contract between odds-engine-2 and the platform. odds-engine-2's
`OE.Websocket.Service` is a **SignalR client** that calls methods on the `/platform` hub; the hub
fans out to platform-web browser circuits. Classification: **[Verified]** (odds-engine-2) ·
**[Verified-sibling:platform-web]** · **[Verified-sibling:platform-final]** · **[Unknown]**.

## Topology — three SignalR-ish layers (don't conflate)

```
OE.Websocket.Service ──(SignalR client)──►  /platform hub  ◄──(SignalR client)── platform-web server
   (publisher; PublishX methods)            (gateway)         │ HubConnection.On<T> handlers
                                                              ▼
                                                    Blazor circuit /_blazor (browser ↔ platform-web)
```
1. **`/platform` hub** — the odds-distribution bus. OEv2 publishes; platform-web subscribes. **Both
   are clients.** WebSockets-only, MessagePack (or JSON), `WithAutomaticReconnect`.
2. **`/_blazor` circuit** — Blazor Server UI sync (browser ↔ platform-web). Separate connection.
3. **External RabbitMQ vhost `ext`** — *not* SignalR; the UOF.Client egress (mentioned for contrast).

> ⚠️ **Hub host is unresolved.** Phase 1 found `Platform.Websocket.API2` (a standalone gateway in/near
> platform-final) maps `/platform`; platform-web's own docs contradict themselves (some say
> platform-web hosts it). The hub `.cs` is in **neither** platform-web nor odds-engine-2. Working
> model: **separate gateway, possibly Azure SignalR-backed**. See [Unknowns.md](./Unknowns.md) #19/#22.

## Connection (OEv2 side) — [Verified]
- `OE.Websocket.Service` builds a `Microsoft.AspNetCore.SignalR.Client` `HubConnection` to
  `Custom:WebSocketAPI` (e.g. `…/platform`), MessagePack when `Custom:UseMessagePack`.
- **No auth token** on the connection (matches platform-web). Per-client instances (S88, krj, qte, …)
  push only their client's data; user scoping is by **group name**.
- It consumes RabbitMQ (`r.oe.sb.engine.*`, `r.bet.*`, `r.wallet.*`, …) and translates each into a
  hub method call on the appropriate group.
- **Startup and reconnect (GM-791, 2026-09-03):** the initial `StartAsync` is retried with exponential
  backoff (1s to 30s) until the hub answers or the host stops; RabbitMQ consumption starts only after the
  hub connection is up, so the durable queue holds the backlog meanwhile. The `Closed` handler reuses
  the same retry loop when `WithAutomaticReconnect` gives up. Dispatch threads are background threads,
  so a stopped host can exit and be restarted by Kubernetes.
- **Client startup data at scale (GM-791):** per-client startup loads (5 Platform API calls each) run
  with bounded parallelism (`Worker.LoadClientConcurrency`, 16 clients at a time). A client joins the
  odds fan-out (`IsReady`) only once its main-market templates loaded; clients whose load failed are
  retried on the 5-minute client refresh. Bet stops and user-specific messages are not gated.
- **League order stamped on pushed fixtures (GM-851):** every pushed fixture carries `SoOr`, the client's
  league order from the `{clientCode}:fso:{sport}:{segment}:{league}` cache (default 1000 for a league
  with no entry), and platform-web places a league that first appears through a push by it.
  `SportsbookRepository.ProcessFixtureSortOrder` fills the cache from Platform API
  `GET v1/sportsbook/fixturesortorder`, which since GM-851 returns the Oddstable order. It runs at
  startup and when `OnOddstableLeagueSortOrderChange` arrives for the client (routing key
  `r.oe.sb.engine.onoddstableleaguesortorderchange.<clientcode>`, relayed by platform-final's CacheSync
  after it purged Sportsbook.API): `Worker` queues the client on its own dispatch thread
  (`ws-fixture-sort-order`). A reload replaces the client's `fso` entries, so a league the answer no
  longer lists falls back to 1000, and a failed reload keeps the entries it has. The queue only receives
  the message if the routing key is in `MessageBroker__RoutingKey` (argocd). `OnSportSortOrderChange`
  is still not handled, so a Sport Tree order change reaches pushed `SoOr` only after a restart.
- **Client resolution per message (`Worker.ResolveClient`, `ClientRegistry`):** most client-scoped
  message types resolve the target client by SBC client code (`ClientRegistry.ResolveByRoutingKey`,
  the routing key's last dot-segment against `_byCode`). A few message types instead carry the
  client as a Platform GUID in the payload and fall back to `ClientRegistry.ResolveByClientId`
  (`_byId`) when the routing-key lookup misses: `OnBetStatus`, `OnCashOutChange`, `OnWalletChange`,
  `OnPayableChange`, and (since BO-108) `OnWebPropsChange`. `OnWebPropsChange`'s routing key always
  carries a GUID, never an SBC code (backoffice-final's `WebPropsRepository` publishes it that way,
  because webprops-api itself is keyed by the client's Platform GUID), so for that message type the
  routing-key branch always misses and resolution always goes through `ResolveByClientId`. Before
  BO-108, `OnWebPropsChange` didn't deserialize the payload or pass a GUID fallback at all, so every
  push for this message type was silently dropped and Client Site Config toggle changes reached
  players only through platform-web's 60-minute webprops cache.

## Hub methods OEv2 calls (outbound) — [Verified]
| Method | Triggered by (RabbitMQ) | Target group |
|---|---|---|
| `PublishOddsChangeForMainMarkets` | `r.oe.sb.engine.onoddschange` (main-market filter) | `endpoint:{cc}:sports:{SpId}:…fixtures`, `fixturedetails:{FiId}` |
| `PublishOddsChangeForAllMarkets` | `…onoddschange` (all) | betslip/top-markets/main groups |
| `PublishBetStopFixture` | `r.oe.sb.engine.onbetstopfixture` | fixture groups |
| `PublishBetStopGlobal` | `r.oe.sb.engine.onbetstopglobal` | `onbetstopglobal:{cc}` |
| `PublishBetStatus` | `r.bet.betstatus.s88` | `onbetstatus:{cc}:{userId}` |
| `PublishCashOutChange` | `r.bet.cashoutchange.s88` | `oncashoutchange:{cc}:{userId}` |
| `PublishWalletChange` | `r.wallet.s88` | `onwalletchange:{cc}:{userId}` |
| `PublishPayableChange` | (payable) | `onpayablechange:{cc}:{userId}` |

`r.oe.sb.engine.*` originate in OEv2; `r.bet.*`/`r.wallet.*` originate in **platform** services and
OEv2 relays them. [Verified]/[Verified-sibling:platform-final]

## Hub events the UI subscribes to (inbound, platform-web) — [Verified-sibling:platform-web]
`OnOddsChange`, `OnBetStopFixture`, `OnBetStopGlobal`, `OnBetslipOddsChange`,
`OnBetslipBetStopFixture`, `OnTopMarketsOddsChange`, `OnTopMarketsBetStopFixture`, `OnBetStatus`,
`OnCashOutChange`, `OnWalletChange`. Handlers + state effects: [ComponentDataFlow.md](./ComponentDataFlow.md),
[SequenceDiagrams.md](./SequenceDiagrams.md).

## Subscription / group model — [Verified-sibling:platform-web] + [Verified] (OEv2 prefix)
platform-web joins groups via hub methods `SubscribeToEndpoint`, `SubscribeToFavorites`,
`SubscribeToWalletChange`, `SubscribeToCashOutChange`, `SubscribeToBetStatus`, `SubscribeToBetslip`,
`SubscribeToTopMarkets` (+ `Unsubscribe*`, `GetConnectionId`). Channel keys:
`sports:{SpId}:livefixtures|todayfixtures|dailyfixtures`, `sports:{SpId}:segments:{SegId}[:leagues:{LeId}]:fixtures`,
`fixturedetails:{FiId}`. OEv2 computes the group with a **`endpoint:{clientCode}:`** prefix (e.g.
`endpoint:S88:sports:1:livefixtures`). User-scoped groups: `onbetstatus|oncashoutchange|onwalletchange|onfavorites:{cc}:{userId}`.

## Message contracts (SignalR models) — [Verified-sibling:platform-web]
The wire models (`OddsChangeFixtureMessageModel`, `BetStopFixtureMessageModel`,
`BetStopGlobalMessageModel`, `BetStatusMessageModel`, `CashOutChangeMessageModel`, `WalletChangeModel`)
exist on both OEv2 (`OE.Websocket.Service/Messages/Outgoing*`) and platform-web
(`Models/EngineMessages/*`). Field tables (e.g. `OddsChangeFixtureMessageModel` → `Ma[]` →
`OutgoingMarket` → `OutgoingSelection` with `DeOd`/`MaOd`/`HoOd`/`InOd`) are in
[ComponentDataFlow.md](./ComponentDataFlow.md) and the platform-web `WebSocket.md`.

> **MessagePack `ContractlessStandardResolver`** (both sides, `MessagePackSecurity.UntrustedData`):
> keys by **property name**, no `[Key]` indices → **renaming any property is a breaking wire change**;
> OEv2 and platform-web must deploy together. Max message size 5 MB; keep-alive 15 s. [Verified-sibling:platform-web]

## Reliability — [Verified-sibling:platform-web]
- `WithAutomaticReconnect`; on `Reconnected`, platform-web re-subscribes all `_connectedChannels`.
- **Missed messages during a hub outage are not replayed** — UI is stale until the next push or a
  read-path refresh.
- No message ack/redelivery at the SignalR layer (unlike the RabbitMQ hops, which are at-least-once).

## Relationship to RabbitMQ
The SignalR hub is the **last-mile** delivery to browsers. The durable, ack'd transport is RabbitMQ
(`e.central`); SignalR is best-effort fan-out on top. OEv2's reliability guarantees end at the hub
call. See [OddsEngineIntegration.md](./OddsEngineIntegration.md), [WebSocket flows](./SequenceDiagrams.md).
