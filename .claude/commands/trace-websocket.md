---
description: Trace a real-time SignalR event from RabbitMQ to the browser
argument-hint: [event e.g. OnOddsChange | OnBetStatus | OnCashOutChange]
---

Trace SignalR event `$ARGUMENTS` end-to-end. file:line evidence; verified / assumption / unknown.

1. **RabbitMQ source:** which routing key feeds it — `r.oe.sb.engine.*` (OEv2-origin) or `r.bet.*`/
   `r.wallet.*` (platform-origin, relayed). See [docs/knowledge/WebSocketMessages.md](../../docs/knowledge/WebSocketMessages.md).
2. **OE.Websocket.Service:** consumes it, maps to the hub method (`PublishOddsChange*`,
   `PublishBetStatus`, `PublishCashOutChange`, `PublishWalletChange`, `PublishBetStop*`) on the
   `/platform` hub, targeting group `endpoint:{cc}:…` or `on{x}:{cc}:{userId}`.
3. **Hub gateway** (separate `Platform.Websocket.API2` — [docs/Unknowns.md](../../docs/Unknowns.md) #19)
   routes to subscribed platform-web circuits.
4. **platform-web:** `HubConnection.On<Model>("$ARGUMENTS")` handler → mutates circuit state →
   `InvokeAsync(StateHasChanged)` → DOM diff over `/_blazor`.

Note the **MessagePack contractless** name-keying (rename = breaking; deploy together). Reliability:
no SignalR-layer replay — missed pushes are lost. Refs: [docs/WebSocket.md](../../docs/WebSocket.md),
[docs/SequenceDiagrams.md](../../docs/SequenceDiagrams.md).
