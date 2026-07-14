---
name: websocket-investigation
description: Investigate the real-time SignalR push path (RabbitMQ → OE.Websocket → /platform hub → platform-web). Use for "updates not reaching UI", group routing, or message-contract questions.
---

# WebSocket Investigation (OEv2)

`OE.Websocket.Service` is a SignalR **client** that relays RabbitMQ → the `/platform` hub. Reference:
[docs/WebSocket.md](../../../docs/WebSocket.md), [docs/knowledge/WebSocketMessages.md](../../../docs/knowledge/WebSocketMessages.md).

## Map
- RabbitMQ in: `r.oe.sb.engine.*` (OEv2-origin), `r.bet.*`/`r.wallet.*` (platform-origin, relayed).
- Hub methods out: `PublishOddsChangeForMainMarkets/AllMarkets`, `PublishBetStop*`, `PublishBetStatus`,
  `PublishCashOutChange`, `PublishWalletChange`, `PublishPayableChange`.
- Groups: `endpoint:{clientCode}:sports:{SpId}:…` / `fixturedetails:{FiId}` /
  `on{betstatus|cashoutchange|walletchange}:{cc}:{userId}`.
- Hub host = separate gateway (`Platform.Websocket.API2`), not platform-web (Unknown #19).

## Watch for
- **MessagePack `ContractlessStandardResolver`** name-keying → property rename = breaking; deploy
  OEv2+platform-web together.
- No SignalR-layer replay — missed pushes during a hub outage are lost (stale until next push/nav).
- Wrong group subscription = no delivery (per-client `*.s88` etc.).
- ⚠️ Service stability risks: unmanaged threads + `Environment.Exit(1)` (H2).

## Procedure
Pick a routing key/event → trace the relay to the hub method + group → confirm platform-web
subscribes (sibling, tag [Verified-pw]) → check contract parity. **Production = read-only.**
