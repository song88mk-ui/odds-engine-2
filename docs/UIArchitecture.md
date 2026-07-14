# UI Architecture

> **Scope note (read first).** `odds-engine-2` has **no UI** — it is headless RabbitMQ worker
> services (no Blazor, no routing, no components, no CSS, no JS interop). Unlike Phase 2's
> middleware/auth topics, there is **no worker analogue** for UI concerns. The Blazor application in
> this platform is **platform-web** (Blazor Server, .NET 7), which already has its own complete UI
> docs. These Phase-7 docs are therefore **cross-repo summaries of platform-web's UI** for
> orientation, so an odds-engine-2 engineer can see where their data surfaces. **Authoritative source:
> `platform-web/docs/` + `platform-web` source.** Tags: **[Verified-pw]** confirmed in platform-web
> source · **[Doc-pw]** from platform-web docs · **[V]** this repo. Never inferred.

## Where odds-engine-2 meets the UI
OEv2 produces the data; platform-web renders it. The seam is already documented from OEv2's side in
[ComponentDataFlow.md](./ComponentDataFlow.md), [RenderingPipeline.md](./RenderingPipeline.md),
[StateFlow.md](./StateFlow.md), [WebSocket.md](./WebSocket.md). This doc set adds the **UI-internal**
view (how platform-web is built) for completeness.

## platform-web at a glance — [Verified-pw]
- **Framework:** Blazor **Server** (.NET 7), server-rendered components over a `/_blazor` SignalR
  circuit; browser holds only the DOM. Multi-tenant single binary.
- **Project:** `Platform/Platform.Child.Web` (a thin `Platform.Parent.Web` shell host also exists).
- **Two UI "layouts"/skins** selected per operator/route:
  - **AL** ("Asian Layout") — `MainLayout`, the `Index` single-page app (scopes via path + query).
  - **EL2** ("Euro 2") — `Euro2Layout`, dedicated route components (`/el2/live`, `/el2-fixture-details`, …).
- **Host page:** `Pages/_Host.cshtml` runs server-side first — reads Cloudflare headers, loads
  per-operator **CDN config** (`CdnConfigurationProvider`), picks theme/CSS, then boots the Blazor
  circuit with a custom `Blazor.start` (WebSockets-only, custom reconnection). [Verified-pw]
- **Routing:** `App.razor` `Router` scans the assembly for `@page` routes, default layout
  `MainLayout`, wraps everything in a fixed `CascadingValue<ClientInfo>`. Server fallback
  `MapFallbackToPage("/_Host")`. See [Navigation.md](./Navigation.md).
- **Composition root:** `Program.cs` registers UI services via extension methods
  `AddClientRepositories/AddClientModels/AddClientStates/AddClientServices/AddHubConnectionBuilder`.
  See [StateManagement.md](./StateManagement.md).

## Architectural layers (platform-web) — [Doc-pw]
```
_Host.cshtml (server prerender, theme/CDN, Blazor.start)
   └─ App.razor (Router + CascadingValue<ClientInfo>)
       └─ Layout (MainLayout / Euro2Layout : LayoutBase)
           └─ Page (Index / Index2 / EL2/*  : BasePage)
               └─ Components (Header, SportsCarousel, FixtureList→League→Fixture→Selection,
                              BetSection, FixtureDetails, BetRecommendation, modals, …)
   State containers (OperatorState/TranslationState/PageState/DeviceState : BaseState<T>)
   Services (Platform REST repo, Toast/Modal/Currency, PreRender, HubConnection)
   JS interop (jsUtilities.v33.js) + postMessage bridge to parent frame
```

## Theming & multi-tenancy — [Doc-pw]
- AL themes (red/blue/gray/orange-dark/orange-light) + EL2 (white), served from CDN or local
  `wwwroot/css`, chosen in `_Host.cshtml` before the circuit starts.
- Per-operator feature flags (`OGAuth`, `DarkMode`, `PartialCashout`, `IsEuroLayout`, language
  options, betting rules) hydrated by `CdnConfigurationProvider.LoadConfigurationAsync(clientId)`.
- Styling is **mostly global SCSS** compiled via `compile_scss.bat` → CSS; component-scoped
  `.razor.css` isolation is **not extensively used**. [Doc-pw] (see [Rendering.md](./Rendering.md)).

## What odds-engine-2 contributes to this UI
- The catalogue (fixtures/markets/selections/odds) the pages render — via platform-final REST reads
  of the Mongo catalogue OEv2 writes.
- The real-time pushes (`OnOddsChange`, `OnBetStop*`, `OnBetStatus`, `OnCashOutChange`,
  `OnWalletChange`) the components react to — via the `/platform` hub (see [WebSocket.md](./WebSocket.md)).

## Document map (this Phase-7 set, all platform-web summaries)
| Doc | platform-web authoritative source |
|---|---|
| [Navigation.md](./Navigation.md) | `platform-web/docs/Navigation.md`, `UIArchitecture.md` |
| [ComponentHierarchy.md](./ComponentHierarchy.md) | `platform-web/docs/ComponentHierarchy.md` |
| [SharedComponents.md](./SharedComponents.md) | `platform-web/docs/SharedComponents.md` |
| [StateManagement.md](./StateManagement.md) | `platform-web/docs/StateManagement.md` |
| [Rendering.md](./Rendering.md) | `platform-web/docs/RenderingPipeline.md` |
| [JavaScriptInterop.md](./JavaScriptInterop.md) | `platform-web/docs/JavaScriptInterop.md` |

> If you need to **modify** the UI, work in `platform-web` and use its own docs as the source of
> truth — these summaries can lag. They exist so the odds-engine-2 onboarding is end-to-end complete.
