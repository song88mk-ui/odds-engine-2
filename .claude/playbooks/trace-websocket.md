# Playbook — Trace a WebSocket Event (OEv2)

Use the `/trace-websocket` command for a single event; this is the deeper procedure.

## 1. Pick the event + direction
- OEv2-origin (`r.oe.sb.engine.*`) or platform-origin relayed (`r.bet.*`/`r.wallet.*`)?
  [docs/knowledge/WebSocketMessages.md](../../docs/knowledge/WebSocketMessages.md).

## 2. RabbitMQ → OE.Websocket.Service
- Find the consumed routing key (binding), the handler, and the hub method it calls
  (`PublishOddsChange*`/`PublishBetStatus`/…) + target group (`endpoint:{cc}:…` / `on{x}:{cc}:{userId}`).
  Note the `clientCode` prefix.

## 3. Hub gateway → platform-web
- The `/platform` hub is a separate gateway (`Platform.Websocket.API2`, Unknown #19). It routes to
  subscribed platform-web circuits.
- platform-web `HubConnection.On<Model>(event)` handler → state mutation → `InvokeAsync(StateHasChanged)`
  → DOM diff over `/_blazor`. (Sibling; tag [Verified-pw].)

## 4. Verify the contract
- The `Outgoing*` model (OEv2) must match platform-web `EngineMessages` (**MessagePack name-keyed**).
  Diff field names. Rename = breaking; deploy together.

## 5. Reliability checks
- Group subscription correct? Hub outage (no replay → stale)? Service health (H2 threads)?

Output: the full event chain with file:line + confidence; flag any contract/contract-parity risk.
