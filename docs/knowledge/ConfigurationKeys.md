# Configuration Keys

Complete config-key reference. **Reminder:** committed `appsettings.json` = dev stubs; prod values
come from argocd env (`__` delimiter) + AWS Secrets Manager. Full mechanics:
[../Configuration.md](../Configuration.md). All [Verified].

## `MessageBroker` (internal RabbitMQ) — broker-using services
| Key | Default | Meaning |
|---|---|---|
| `MessageBroker:Host` / `:Port` | localhost / 5672 | broker; **5671 ⇒ TLS** |
| `MessageBroker:Username` / `:Password` | — | creds (secrets in prod) |
| `MessageBroker:VirtualHost` | `/` | vhost |
| `MessageBroker:Exchange` | `e.central` | topic exchange |
| `MessageBroker:Queue` | — | queue (MD5-hostname suffix if exclusive) |
| `MessageBroker:Consumer` | — | consumer-tag prefix |
| `MessageBroker:RoutingKey` | "" | `|`-separated bind patterns |
| `MessageBroker:PrefetchCount` | 1000 | QoS |
| `MessageBroker:IsExclusive` / `:IsPriority` | false | exclusive autodelete / `x-max-priority=10` |

## `ExternalMessageBroker` (UOF.Client only) — same shape, vhost `ext`, publish-only.

## `Custom:*` (service-specific)
| Key | Service(s) | Meaning |
|---|---|---|
| `Custom:MongoDB-Sportsbook` | Sportsbook, Lookup, LiveStream, Settlement, Cleanup, Websocket | Mongo conn string |
| `Custom:DbContextTimeout` | Sportsbook | SQL command timeout (180s) |
| `Custom:Trading` | UOF.Client | Trading SQL conn string |
| `Custom:ClientId` / `Custom:ClientCode` | UOF.Client (`TSO`), Websocket (`S88`) | tenant id/code |
| `Custom:AsianClient` / `Custom:AsianClientCodes` | UOF.Client / Websocket | enable Asian odds formats |
| `Custom:APIKey` / `Custom:ApiKey` | UOF.Client, Websocket, Settlement | Platform API key (note casing varies) |
| `Custom:PlatformAPI` / `Custom:WebSocketAPI` | Websocket | Platform REST / SignalR `/platform` URLs |
| `Custom:UseMessagePack` | Websocket | MessagePack vs JSON for SignalR |
| `Custom:IsOEEnabled` | Websocket | OE vs legacy odds path |
| `Custom:IsPriority` / `Custom:IsExclusive` | Websocket | queue options (alt to MessageBroker:*) |
| `Custom:Enabled` | LiveStream (true), Cleanup (**false**) | job master toggle |
| `Custom:EditSportsbook` | Settlement | gate before processing |
| `Custom:ApiKey` | Settlement | **unused** (reserved) |
| `Custom:SportRetention` | Cleanup | retention days (default −30) |
| `Custom:UtcWindowHours:Start` / `:End` | Cleanup | cleanup window (1–10 UTC), re-checked per batch |
| `Custom:BatchSize` | Cleanup | fixtures per `DeleteMany` batch (default 5000) |
| `Custom:BatchDelayMs` | Cleanup | pause between delete batches (default 2000 ms) |
| `ConnectionStrings:Betting` | Cleanup | platform Betting SQL DB — preload of fixture ids with bets (2026-07-13) |
| `Custom:DbContextTimeout` | Cleanup (180) | SQL command timeout for the bet preload |
| `Custom:BetGenius:Uri` / `:Env` / `:User` / `:Password` / `:Key` | LiveStream (via OE.Shared) | BetGenius Video API |

## `API:*`
| Key | Service | Meaning |
|---|---|---|
| `API:PlatformAPI` | UOF.Client | Platform API base URL |
| `API:APIKey` | UOF.Client | Platform API key |

## `ConnectionStrings:*`
| Key | Service | Meaning |
|---|---|---|
| `ConnectionStrings:Translation` | Sportsbook | Translation SQL (AWS RDS) |

## `Serilog` / `Logging` / `Telemetry`
| Key | Meaning |
|---|---|
| `Serilog:*` | sink (Console), level, enrich (`FromLogContext`); argocd sets Compact JSON formatter |
| `Logging:LogLevel:*` | MS log levels (mostly superseded by Serilog) |
| `Telemetry:Enabled` / `:Environment` / `:MetricsPort` | OTel toggle/env/`:8080` |

## argocd env-var equivalents (prod)
`__`-delimited: `MessageBroker__Host`, `MessageBroker__Password` (External Secret), `Custom__MongoDB-Sportsbook`
(External Secret), `Telemetry__Environment`, `Serilog__WriteTo__0__Args__formatter`,
`OTEL_EXPORTER_OTLP_ENDPOINT`, `ASPNETCORE_URLS`. Per-service values:
`argocd/apps/oddsengine/<service>/<env>.yaml`.

## ⚠️ Secret keys (currently committed — rotate; [../TechnicalDebt.md](../TechnicalDebt.md) H1)
`MessageBroker:Password`, `ExternalMessageBroker:Password`, `ConnectionStrings:Translation` (pwd),
`Custom:Trading` (pwd), `Custom:BetGenius:Password`/`:Key`, `Custom:APIKey`/`API:APIKey`,
`Custom:ApiKey` (Settlement). Do not paste real values into new files.
