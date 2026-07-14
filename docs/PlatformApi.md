# Platform API Integration (REST seam)

The HTTP/REST integration between odds-engine-2 and **platform-final**. Two directions:
**(A)** OEv2 *calls* the Platform API; **(B)** the Platform API *serves OEv2-authored data* to the UI
(odds-engine-2 doesn't call B, but it's the read path for everything OEv2 writes, so it's documented
for completeness). Classification: **[Verified]** · **[Verified-sibling:platform-final]** ·
**[Verified-sibling:platform-web]** · **[Unknown]**.

## A. Calls OEv2 makes to the Platform API — [Verified]

| Caller (OEv2) | Method + path | Auth headers | Purpose | Handler |
|---|---|---|---|---|
| OE.UOF.Client | `GET v1/sportsbook/client/markettemplateplayerprops?sportId=` | `x-api-key` (`Custom:APIKey`) | player-prop market templates (cached 1 year) | `IntegrationController` → `Platform.Sportsbook.API` [Verified-sibling:platform-final] |
| OE.Websocket.Service | Platform API templates/metadata (`Custom:PlatformAPI`, e.g. `https://platform-api.dev.song88.com`) | `x-client-id` + `x-api-key` | client/template metadata for push shaping | [Verified] call; exact endpoints [Unknown] |
| (OE.Shared `RabbitMQRepository`) | `POST v1/broker/publish` | — | HTTP→RabbitMQ publish bridge (`Platform.RabbitMQ.API`) | [Verified] client; [Verified-sibling:platform-final] handler |

- Base URLs from config (`API:PlatformAPI` / `Custom:PlatformAPI`), keys from `Custom:APIKey`/`API:APIKey`.
- UOF.Client uses a hardcoded 5 s HttpClient timeout and swallows errors on the player-props call
  (`async void`) — see [TechnicalDebt.md](./TechnicalDebt.md) H5/M4.

## B. The Platform read surface over OEv2 data — [Verified-sibling:platform-final/platform-web]

platform-web (server-side BFF) calls these read endpoints; platform-final serves them by **reading
the MongoDB Sportsbook catalogue OEv2 authored** (plus SQL for bet/wallet). OEv2 does not call these,
but they are how its data reaches users — relevant when reasoning about end-to-end effects.

| Endpoint (platform-web `PlatformRepository`) | platform-final handler | Backing store |
|---|---|---|
| `GET v1/sportsbook/sports?sportGroup=0` | `v1/sportsbook/sports` | Mongo lookups/fixtures |
| `GET v1/sportsbook/selectormarkettemplates` | same | Mongo/config |
| `GET v1/sportsbook/sports/{live,today,daily,outright}fixtures` | same | Mongo Fixture |
| `GET v1/sportsbook/sports/segments[/leagues]/fixtures` | same | Mongo |
| `GET v1/sportsbook/activefixtures/details?fixtureId=` (web: `GetFixtureDetails`) | `v1/sportsbook/activefixtures/details` | Mongo Fixture/Market/Selection |
| `GET v1/sportsbook/selectiondetails` | [Unknown] | Mongo |
| `GET v1/sportsbook/search` | same | Mongo |
| `GET v1/sportsbook/translations` | same | SQL Translation (OEv2 also reads) |
| `GET v1/bet/config` | same | SQL (platform) |
| `GET v1/wallet/balance` | same | SQL Wallet (platform) |
| (also: `v1/sportsbook/client/topfixtures`, `…/sports`, `…/announcements`, etc.) | `IntegrationController` | mixed |

backoffice-final also calls the Platform API (`v1/clients`, `v1/client/*`, `v1/bet/details`,
`v1/bet/manualsettlement`, …) with `x-api-key` — [Verified-sibling:backoffice-final].

## Conventions — [Verified-sibling:platform-web/platform-final]
- **Envelope:** every response is `BaseResponseDTO<T> { StatusCode, Message, Result }` (the
  `StatusCode` enum is shared via `OE.Shared`).
- **Auth headers:** `x-api-key` (origin), `x-client-id` (tenant), `x-operator-token` (user session,
  issued by Auth), `x-language-code`, `x-country-code`, `x-user-ip`, `x-is-ssr`, and `x-og-*`
  (Oriental Gaming) when applicable.
- **Retry:** platform-web retries each call up to 2×; `Operator_Error`/`103` → Unauthorized event;
  `205` → Maintenance.
- **Client caching:** platform-web caches several GETs ~30 s in-process; UOF.Client caches
  player-props 1 year.

## Base URL / environment resolution — [Verified-sibling:platform-web]
platform-web resolves the API base per request host (`X-Forwarded-Host` → `UrlMappings:{host}` →
`API:PlatformAPI`), enabling multi-tenant domains (e.g. `platform-api.jmj19876.com`). OEv2 uses a
single configured base per deployment.

## What is NOT a REST contract
OEv2's **primary** outputs are **not** REST — they are RabbitMQ messages, the Mongo catalogue, and
the SignalR hub. The REST seam is narrow (player-props + a publish bridge). The bulk of
OEv2↔platform integration is messaging + shared databases. See
[OddsEngineIntegration.md](./OddsEngineIntegration.md).

## Unknowns
- Exact Websocket.Service Platform API endpoints/payloads. [Unknown]
- `v1/sportsbook/selectiondetails` platform handler. [Unknown]
- Whether `markettemplateplayerprops` reads Mongo or SQL on the platform side. [Unknown]
