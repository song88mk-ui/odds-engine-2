# JavaScript Interop (platform-web)

> Summary of **platform-web's** Blazor↔JS interop — odds-engine-2 has none (no browser/JS).
> Authoritative: `platform-web/docs/JavaScriptInterop.md`. Tags: **[Verified-pw]** / **[Doc-pw]**.

## Setup — [Doc-pw]
- `IJSRuntime` injected globally (`_Imports.razor` as `_jsRuntime`) and per-component.
- Primary script: `wwwroot/js/jsUtilities.v33.js` (~812 lines; versioned v25–v33, v33 active). Also
  `lmt.*.js` (Sportradar Live Match Tracker), Hammer.js (gestures), `blazor.server.js`, `boot.js`.
- Calls are **guarded by `PreRenderService.IsPreRendering`** (JS unavailable during SSR) and usually
  made from `OnAfterRenderAsync`.

## .NET → JS (representative) — [Doc-pw]
| Function | Purpose |
|---|---|
| `getTimeZoneOffset()` | browser UTC offset (for date/scope filtering) |
| `ScreenSize()` / `ScreenWidthSize()` | responsive device tier / raw width |
| `detectSwipeRight(id)` | Hammer.js swipe gesture (open bet slip) |
| `smoothScroll(id)` / `scrollToTop(...)` | scrolling on navigation |
| `handleSidebarStatus(bool)` / `handleBetHistory(bool)` | DOM width adjustments for panels |
| `setObservedElement(name)` | `ResizeObserver` → posts height to parent frame |
| `addClassName/removeClassName(id, cls)` | odds up/down animation cleanup |
| `clearSessionStorage()` | on session expiry |
| `resizeListener()/onpopstate()/onscroll()` | window event listeners (theme on popstate, debounced scroll) |
| `Set{Index,Clipboard,SubHeader}Reference(DotNetObjectReference)` | register .NET callbacks |
| `defaultUrlQueryParameterENLanguageCode([...])` | normalize `?languageCode` to uppercase/EN |

## JS → .NET callbacks (`DotNetObjectReference`) — [Doc-pw]
Parent-frame `postMessage` events are received by `jsUtilities.v33.js` and dispatched to stored
references, e.g.:
- `indexReference.invokeMethodAsync('HandleNavigateTo', …)` ← `navigate-to`
- `indexReference.invokeMethodAsync('ShowBetSection'/'HideBetSection')` ← swipe / `expand-bet-slip` / `collapse-bet-slip`
- `indexReference.invokeMethodAsync('HandleUserAuthorization', token, userId, …)` ← `user-authorization`
- `indexReference.invokeMethodAsync('HandleOnChangeLanguage', code)` ← `on-change-language`
- `subHeaderReference.invokeMethodAsync('SetToDarkMode'/'SetToLightMode')` ← theme change

## Parent ↔ child `postMessage` bridge — [Doc-pw]
platform-web runs inside an operator iframe. Communication is via `window.postMessage`:
- **Parent → child events:** `navigate-to`, `user-authorization`, `user-unauthorized`,
  `expand-bet-slip`, `collapse-bet-slip`, `add-bet-selections`, `on-change-language`,
  `on-change-currency`, `collapse-lmt`, `bonus`, `free-bet-assigned`, `gtm-client`, …
- **Child → parent** via `window.platformEventsSender(data)` → `window.parent.postMessage(...)`:
  `on-page-load`, `on-page-height-change`, `on-navigate-to`, `on-odds-format-change`,
  `on-betslip-count-update`, `on-bet-placed-btn-clicked`, `on-api-error`, `on-redirect`.

## Blazor.start customization — [Verified-pw]
`_Host.cshtml` overrides `Blazor.start` with a custom `reconnectionHandler` (3 retries × 5 s, then
`location.reload()`) and `configureSignalR` (`skipNegotiation: true`, WebSockets-only) pointing at the
custom circuit URL.

## odds-engine-2 relevance
**None** — odds-engine-2 is headless and performs no JS interop. This boundary exists only between
the browser and platform-web. (Listed for end-to-end completeness; the only OEv2 link is that JS
drives the odds up/down animation reset for the odds values OEv2 ultimately produces.)
