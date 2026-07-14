# Platform API Endpoints

Quick lookup of the REST seam. Full detail: [../PlatformApi.md](../PlatformApi.md). Tags: **[V]**
odds-engine-2 · **[Vpf]** platform-final · **[Vpw]** platform-web.

## Endpoints odds-engine-2 CALLS — [V]
| Caller | Method + path | Auth | Purpose |
|---|---|---|---|
| OE.UOF.Client | `GET v1/sportsbook/client/markettemplateplayerprops?sportId=` | `x-api-key` | player-prop templates (cached 1y) |
| OE.Websocket.Service | Platform API templates/metadata (`Custom:PlatformAPI`) | `x-client-id`+`x-api-key` | push-shaping metadata (exact paths [Unknown]) |
| OE.Shared `RabbitMQRepository` | `POST v1/broker/publish` | — | HTTP→RabbitMQ bridge (`MessageRequestDTO`) |

## The read surface over OEv2 data (platform-web → platform-final → Mongo) — [Vpf]
> OEv2 doesn't call these; they serve the catalogue OEv2 writes. Listed for end-to-end context.

| platform-web method | path | backing |
|---|---|---|
| GetSportsTree | `GET v1/sportsbook/sports?sportGroup=0` | Mongo |
| GetSelectorMarketTemplates | `GET v1/sportsbook/selectormarkettemplates` | Mongo/config |
| GetLiveFixtures / GetTodayFixtures / GetDailyFixtures | `GET v1/sportsbook/sports/{live,today,daily}fixtures?sportId=` | Mongo Fixture |
| GetOutrightFixtures | `GET v1/sportsbook/sports/outrightfixtures?sportId=` | Mongo |
| GetFixturesBySegment / ByLeague | `GET v1/sportsbook/sports/segments[/leagues]/fixtures` | Mongo |
| GetFixtureDetails | `GET v1/sportsbook/activefixtures/details?fixtureId=` | Mongo Fixture/Market/Selection |
| GetSelectionDetails | `GET v1/sportsbook/selectiondetails?...` | Mongo (platform handler [Unknown]) |
| GetFixtureBySearchText | `GET v1/sportsbook/search?searchText=&sportGroupId=` | Mongo |
| GetTranslations | `GET v1/sportsbook/translations` | SQL Translation |
| GetBetConfig | `GET v1/bet/config?currencyCode=` | SQL (platform) |
| GetWalletBalance | `GET v1/wallet/balance` | SQL Wallet (platform) |

backoffice-final also calls `v1/clients`, `v1/client/*`, `v1/bet/details`, `v1/bet/manualsettlement`
with `x-api-key`. [Verified-sibling:backoffice-final]

## Conventions — [Vpw/Vpf]
- **Envelope:** `BaseResponseDTO<T> { StatusCode, Message, Result }`; `StatusCode` enum shared.
- **Headers:** `x-api-key`, `x-client-id`, `x-operator-token`, `x-language-code`, `x-country-code`,
  `x-user-ip`, `x-is-ssr`, and `x-og-*` (Oriental Gaming) when applicable.
- **Retry:** platform-web 2× retry; `103`/`Operator_Error` → Unauthorized; `205` → Maintenance.
- **Client cache:** several GETs ~30s (platform-web); player-props 1y (UOF.Client).

## Notes
OEv2's REST footprint is **narrow** (player-props + publish bridge) — its primary outputs are
RabbitMQ + Mongo + SignalR, not REST. See [../OddsEngineIntegration.md](../OddsEngineIntegration.md).
