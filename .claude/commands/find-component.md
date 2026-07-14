---
description: Find a UI component / message model that renders an OEv2 output (platform-web)
argument-hint: [thing rendered, e.g. odds cell | bet slip | cashout | wallet balance]
---

Find what renders `$ARGUMENTS`. odds-engine-2 has **no UI** — this is platform-web (Blazor Server).

- Map: OEv2 output (routing key / hub method) → SignalR event → platform-web handler → state →
  component. Use [docs/ComponentDataFlow.md](../../docs/ComponentDataFlow.md),
  [docs/ComponentHierarchy.md](../../docs/ComponentHierarchy.md),
  [docs/knowledge/WebSocketMessages.md](../../docs/knowledge/WebSocketMessages.md).
- For UI internals, point to `platform-web` source + its own `docs/` (authoritative); tag
  sibling facts [Verified-pw]/[Doc-pw]; don't invent platform-web behavior.
- Output: the OEv2→component chain + the platform-web file(s) to look at.
