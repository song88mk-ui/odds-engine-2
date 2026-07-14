---
name: blazor-development
description: Work on the Blazor UI that consumes OEv2 data. NOTE odds-engine-2 has NO UI — the Blazor app is the sibling platform-web. Use to orient before editing UI, but make UI changes in platform-web.
---

# Blazor Development (cross-repo → platform-web)

⚠️ **odds-engine-2 has no UI.** The Blazor Server app is **`platform-web`** (sibling repo), which has
its own authoritative `docs/`. This skill orients you to the seam; **make UI changes in platform-web**,
not here.

## What OEv2 controls (and you might need to change here)
- The **data + events** the UI renders: Mongo catalogue (read by platform via REST) + SignalR pushes
  (`OnOddsChange`/`OnBetStop*`/`OnBetStatus`/`OnCashOutChange`/`OnWalletChange`).
- The **SignalR message contract** (`OE.Websocket.Service/Messages/Outgoing*`) — mirrors platform-web
  `EngineMessages`. **MessagePack name-keyed → rename = breaking; deploy both together.**

## Orientation docs (platform-web summaries from OEv2's vantage)
[docs/UIArchitecture.md](../../../docs/UIArchitecture.md), [docs/ComponentDataFlow.md](../../../docs/ComponentDataFlow.md),
[docs/StateManagement.md](../../../docs/StateManagement.md), [docs/Rendering.md](../../../docs/Rendering.md),
[docs/Navigation.md](../../../docs/Navigation.md), [docs/JavaScriptInterop.md](../../../docs/JavaScriptInterop.md).
For UI internals, use platform-web source + its `docs/` (tag [Verified-pw]/[Doc-pw]).

## Rule
If the task is "render OEv2 data differently", split it: contract/data work in OEv2 (this repo),
component work in platform-web. Don't invent platform-web behavior.
