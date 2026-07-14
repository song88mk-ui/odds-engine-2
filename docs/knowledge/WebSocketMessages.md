# WebSocket Messages

Quick lookup for the SignalR `/platform` hub contract. Full detail: [../WebSocket.md](../WebSocket.md).
Tags: **[V]** odds-engine-2 · **[Vpw]** platform-web · **[Vpf]** platform-final.

## Hub methods OE.Websocket.Service CALLS (outbound push) — [V]
| Method | Triggered by RabbitMQ key | Target group |
|---|---|---|
| `PublishOddsChangeForMainMarkets` | `r.oe.sb.engine.onoddschange` (main filter) | `endpoint:{cc}:sports:{SpId}:…fixtures`, `fixturedetails:{FiId}` |
| `PublishOddsChangeForAllMarkets` | `…onoddschange` (all) | betslip/top-markets/main groups |
| `PublishBetStopFixture` | `r.oe.sb.engine.onbetstopfixture` | fixture groups |
| `PublishBetStopGlobal` | `r.oe.sb.engine.onbetstopglobal` | `onbetstopglobal:{cc}` |
| `PublishBetStatus` | `r.bet.betstatus.s88` | `onbetstatus:{cc}:{userId}` |
| `PublishCashOutChange` | `r.bet.cashoutchange.s88` | `oncashoutchange:{cc}:{userId}` |
| `PublishWalletChange` | `r.wallet.s88` | `onwalletchange:{cc}:{userId}` |
| `PublishPayableChange` | (payable) | `onpayablechange:{cc}:{userId}` |

## Hub events the UI subscribes to (inbound) — [Vpw]
| Event string | Model | UI handler | State |
|---|---|---|---|
| `OnOddsChange` | `OddsChangeFixtureMessageModel` | `OnOddsChangeAsyncV2`/`OnOddsChangeAsync` | fixture lists |
| `OnBetslipOddsChange` | same | `OnBetSlipOddsChangeAsync` | bet slip |
| `OnTopMarketsOddsChange` | same | `BetRecommendation` | recommendations |
| `OnBetStopFixture` / `OnBetslipBetStopFixture` / `OnTopMarketsBetStopFixture` | `BetStopFixtureMessageModel` | `OnBetStopFixtureAsync` | fixture/betslip |
| `OnBetStopGlobal` | `BetStopGlobalMessageModel` | `OnBetStopGlobalAsync` | global flag |
| `OnBetStatus` | `BetStatusMessageModel` | `OnBetStatusChangeAsync` | bet slip (by `BeId`) |
| `OnCashOutChange` | `CashOutChangeMessageModel` | `OnCashOutChangeAsync` | my bets (by `BeId`) |
| `OnWalletChange` | `WalletChangeModel` | `OnWalletChangeAsync` | balance |

## Subscription methods the UI invokes — [Vpw]
`SubscribeToEndpoint`/`UnSubscribeToEndpoint`, `SubscribeToFavorites`, `SubscribeToBetslip`,
`SubscribeToTopMarkets`, `SubscribeToBetStatus`, `SubscribeToCashOutChange`,
`SubscribeToWalletChange` (+ `Unsubscribe*`), `GetConnectionId`.

## Group naming — [V/Vpw]
`endpoint:{clientCode}:sports:{SpId}:livefixtures|todayfixtures|dailyfixtures`,
`…:segments:{SegId}[:leagues:{LeId}]:fixtures`, `…:outrightfixtures`, `…:topfixtures`,
`endpoint:{cc}:fixturedetails:{FiId}`, `endpoint:{cc}:boostedfixtures`,
`onfavorites|onbetstatus|oncashoutchange|onwalletchange|onpayablechange:{cc}:{userId}`,
`ontopmarketsoddschange:{cc}:{FiId}`. OEv2 prefixes `endpoint:{clientCode}:`.

## Message model fields (most-used)
- **`OddsChangeFixtureMessageModel`**: fixture meta (`spId/segId/leId/fiId/fiNa/sc/fiStId/maCo/isBeBu`)
  + `Ma[]` `OutgoingMarket`(`maTeId/maId/li/maStId/maNa/soOr/isBeBu`) → `Se[]`
  `OutgoingSelection`(`seTeId/seId/seNa/deOd/maOd/hoOd/inOd/isAc/soOr`) + `*Tr` translations.
- **`BetStatusMessageModel`**: `UsId/BeId/BeStId/BeStNa/Me`.
- **`CashOutChangeMessageModel`**: `UsId/BeId/CaOuStId/CaOuStNa/CaOuSt`.
- **`WalletChangeModel`**: `UsId/Ba`.
- **`BetStopFixtureMessageModel`**: `FiId/IsBeSt/SeAcMaToIn`. **`BetStopGlobalMessageModel`**: `IsBeSt`.

## Gotchas — [Vpw]
- **MessagePack `ContractlessStandardResolver`** keys by **property name** → renaming a property is a
  breaking wire change; deploy OEv2 + platform-web together.
- No SignalR-layer ack/redelivery; missed pushes during a hub outage are **not replayed**.
- Hub host is a separate gateway (`Platform.Websocket.API2`), not platform-web — [../Unknowns.md](../Unknowns.md) #19.
