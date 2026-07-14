# Rendering Lifecycle & CSS (platform-web)

> Summary of **platform-web's** Blazor rendering — odds-engine-2 renders nothing (headless). The
> OEv2-event→pixels pipeline is in [RenderingPipeline.md](./RenderingPipeline.md); this doc covers the
> Blazor-internal lifecycle + CSS. Authoritative: `platform-web/docs/RenderingPipeline.md`. Tags:
> **[Verified-pw]** / **[Doc-pw]**.

## Blazor Server rendering model — [Verified-pw]
Components render **on the server**; the browser receives DOM diffs over the `/_blazor` SignalR
circuit. `_Host.cshtml` server-prerenders the shell (`ServerPrerendered`) before the circuit starts.

## Lifecycle hooks (how platform-web uses them) — [Doc-pw]
| Hook | Typical use |
|---|---|
| `OnInitializedAsync` | hub connect + subscribe handlers, operator model init, parallel data load (translations/sports/templates/wallet), bet config |
| `OnParametersSetAsync` | read route/scope params, decide `_shouldRender`, call `LoadPage()` on nav |
| `OnAfterRenderAsync` | JS interop (resize/scroll/observers, DotNetObjectReference registration), **reset `_shouldRender=false`** |
| `ShouldRender` | return `_shouldRender`/`RenderComponent` to suppress diffs |
| `Dispose`/`DisposeAsync` | hub disconnect, unsubscribe state handlers, cancel tokens |

## Render gating (the key performance pattern) — [Doc-pw]
A real-time odds feed would re-render constantly; platform-web dampens it:
- **`ShouldRender()` override** in ~44 components returns a `_shouldRender`/`RenderComponent` flag,
  set true only on meaningful change and reset to false in `OnAfterRenderAsync`. Prevents re-render
  on every parent `StateHasChanged` from unrelated state.
- **`Counter` pattern:** SignalR handlers increment a `Counter` field on fixture/league models;
  child components take it as a `[Parameter]` so Blazor's change-detection re-renders only the
  fixtures/selections that actually changed.
- **`InvokeAsync(StateHasChanged)`:** SignalR handlers run on background threads, so re-render must
  be marshaled to the circuit's synchronization context.
- **Pre-render guard:** `PreRenderService.IsPreRendering` skips JS/API during SSR (JS runtime
  unavailable), avoiding double work.

## Odds-change animation — [Doc-pw]
`Selection`/`BetRecommendationSelection` compare new vs previous odds and add `odds-up`/`odds-down`
CSS classes; after a delay (~3–4 s) they call JS (`removeClassName`) to clear it. Style variant
(`OddsUpdateType.Corner` for OG vs `.Border`) is chosen at init.

## Connection cap & health — [Doc-pw]
`Custom:MaximumConnections = 300` per instance (circuit count via `ConnectedUserList`); over-limit
users are redirected and `/healthcheck` returns 500 so the LB sheds load. Circuit timeout ~3 min →
`_Host.cshtml` reconnection (3×5 s) then `location.reload()`.

## CSS / styling — [Doc-pw]
- **Mostly global SCSS** compiled to CSS via `compile_scss.bat` (`PreBuild` target); per-operator
  themes (AL: red/blue/gray/orange-dark/orange-light; EL2: white) served from CDN or `wwwroot/css`,
  selected in `_Host.cshtml` before the circuit boots.
- **Component-scoped CSS isolation (`.razor.css`) is NOT extensively used** — styling is global +
  layout/theme-directory-based. [Doc-pw]
- Two-skin component variants (AL/EL2 folders) carry their own markup/classes.

## odds-engine-2 relevance
OEv2 **triggers** these renders (every push = a server render on every subscribed circuit) but
controls none of the gating — see [RenderingPipeline.md](./RenderingPipeline.md) for OEv2's influence
on render *volume* (publish frequency, hub method/group choice, payload size).
