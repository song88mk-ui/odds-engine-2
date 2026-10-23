# Configuration Overview

## How configuration actually resolves — [Verified] / [Verified-argocd]

1. **Baked-in `appsettings.json`** ships in each container (most values are `localhost`/dev
   stubs; a few hold real AWS hosts + live credentials).
2. **argocd** (`charts/betgenius-app` + per-service `{dev,stg,prd}.yaml`) **overrides** these at
   deploy time via environment variables using the .NET `__` (double-underscore) convention,
   e.g. `MessageBroker__Host`, `MessageBroker__Queue`, `Telemetry__Environment`.
3. **Secrets** (RabbitMQ user/pass, MongoDB connection string) are injected by **External
   Secrets** from **AWS Secrets Manager** paths like `/dev/apps/rabbitmq/password` → env vars
   like `MessageBroker__Password`.

> Therefore the committed `appsettings.json` is **not** the production configuration. To know
> what a service actually runs with in an environment, read
> `argocd/apps/oddsengine/<service>/<env>.yaml`.

## Consolidated config matrix (committed appsettings.json) — [Verified]

| Service | Internal broker queue / routing key | Mongo | SQL | Key Custom:* | API endpoints |
|---|---|---|---|---|---|
| Adapter.Service | `q.oe.adapter*` / `r.uof.#` (`r.bg.receiver.#|r.uof.#` in argocd) | — | — | — | — |
| Sportsbook.Service | `q.sb.engine` / `r.oe.adapter.#.11` (dev stub; prod `.00–.10`) | `Sportsbook` @ 10.49.100.55 | `Translation` @ AWS RDS | `DbContextTimeout=180` | — |
| UOF.Client | `q.uof.engine.client.TSO` / `r.oe.adapter.*.*|r.oe.uof.heartbeat|r.of.recovery.#.tso` + **external** vhost `ext` | — | `Trading` | `ClientId`, `ClientCode=TSO`, `AsianClient` | PlatformAPI + `x-api-key` |
| Websocket.Service | `q.sb.engine.websocket.S88` / 9 piped keys (oddschange, betstop*, betstatus, cashout, wallet, favorites, boosted, sortorder) | reads Sportsbook | — | `ClientCode=S88`, `UseMessagePack`, `IsOEEnabled`, `IsPriority/IsExclusive` | WebSocketAPI `/platform`, PlatformAPI |
| Job.Lookup | `q.oe.job.lookup` / `r.oe.adapter.onmarketchange.#` (`PrefetchCount=10`) | `Sportsbook` @ 10.49.100.55 | - | `ConsumerCheckSeconds=30`, `ConsumerDeadChecks=2`, `StatusLogSeconds=60`, `SlowMessageMilliseconds=5000` | - |
| Job.Settlement | publisher only | `Sportsbook` | — | `EditSportsbook=true`, `ApiKey`(unused) | — |
| Job.LiveStream | — (no broker) | `Sportsbook` @ localhost stub | — | `Enabled`, `BetGenius:{Uri,Env,User,Password,Key}` | `https://api.geniussports.com/` |
| Job.Cleanup | `q.oe.job.cleanup` / `r.bet.placement` (bet-fixture protection, 2026-07-13) | `Sportsbook` | `Betting` (preload fixtures with bets) | `Enabled=false`, `SportRetention=-30`, `UtcWindowHours:{Start,End}`, `BatchSize`, `BatchDelayMs`, `OrphanCleanupDryRun=false`, `OrphanSweepIntervalHours=24`, `DbContextTimeout` | — |
| Job.BoostedOddsCap | `q.oe.job.boostedoddscap` (exclusive, auto-delete) / `r.oe.sb.engine.onoddschange` | — | `Trading` (read-only: `PriceBoostConfig`, `BoostedSettings`, `Client`) via `ConnectionStrings:Trading` | `Enabled=false`, `RefreshSeconds=60`, `HysteresisPercent=0.25`, `StartupSilentSeconds=120`, `AlertBufferSize=1000`, `BatchWindowSeconds=5`, `ConsumerCheckSeconds=30`, `ConsumerDeadChecks=2`, `StatusLogSeconds=60`, `PlayerPropMarketTemplateIds`, `Notification:*`, `Email:*` (`Email:Enabled=false`) | SMTP relay only when email is on |
| Job.KeepAlive | publisher only | — | — | — | — |
| Translation.Job | `q.oe.translation.job` / `r.bet.placement` | — | `Translation` | `DbContextTimeout=180`, `Cleanup:{Enabled,IntervalMinutes,StartupDelaySeconds,RetentionDays,BatchSize,Prefixes}` | — |

## Standard config sections — [Verified]

### `MessageBroker` (`MessageBroker.Connect`, `MessageBroker.cs:112-212`)
| Key | Default | Meaning |
|---|---|---|
| `Host` / `Port` | localhost / 5672 | broker; **Port 5671 ⇒ TLS auto-enabled** |
| `Username` / `Password` | — | credentials (injected via secrets in prod) |
| `VirtualHost` | `/` | vhost |
| `Exchange` | `e.central` | topic exchange (durable) |
| `Queue` | — | queue name; suffixed with MD5(hostname)[..5] if `IsExclusive` |
| `Consumer` | — | consumer tag prefix (`{Consumer}.{Guid}`) |
| `RoutingKey` | "" | `|`-separated bind patterns (lowercased on bind) |
| `PrefetchCount` | 1000 | QoS |
| `IsExclusive` / `IsPriority` | false | exclusive autodelete queue / `x-max-priority=10` |

### `ExternalMessageBroker` (UOF.Client only) — same shape, separate connection, vhost `ext`.

### `Custom:*` — service-specific (Mongo connection `Custom:MongoDB-Sportsbook`, client codes,
feature toggles, retention, BetGenius creds, etc.).

### `ConnectionStrings:Translation` (Sportsbook) / `Custom:Trading` (UOF.Client) — SQL Server.

### `Serilog` — console sink, Compact JSON formatter (argocd sets the formatter explicitly).

### `Telemetry` — `Enabled`, `Environment`, `MetricsPort` (argocd env).

## Routing-key namespace reference — [Verified]

| Prefix | Producer → Consumer | Meaning |
|---|---|---|
| `r.bg.receiver.*` | bet-genius → Adapter | provider-normalized events |
| `r.uof.*` | (oddsfeed? [Unknown]) → Adapter | UOF-source events |
| `r.of.recovery.#.tso` | ? → UOF.Client | feed recovery (client TSO) |
| `r.oe.adapter.<type>.<NN>` | Adapter → Sportsbook(NN)/Lookup/UOF.Client | sharded normalized stream |
| `r.oe.adapter.on_bet_stop_global.10` | Adapter → Sportsbook(10) | global bet-stop (reserved shard) |
| `r.oe.sb.engine.on_odds_change` | Sportsbook → Websocket | canonical internal odds change |
| `r.oe.sb.engine.on_bet_stop_*` | Sportsbook → Websocket | bet-stop fixture/global |
| `r.oe.bet.settlement` | Sportsbook → platform-final Settlement.Service | bet settlement |
| `r.bet.betstatus.s88`, `r.bet.cashoutchange.s88`, `r.wallet.s88` | platform → Websocket | user/account events for push |
| `r.oe.uof.heartbeat` | KeepAlive → UOF.Client | heartbeat |
| `r.error.<domain>.<service>`: `r.error.oe.adapter`, `r.error.oe.translation`, `r.error.oe.translation.job`, `r.error.engine.uof.client` | various | error sink (one `r.error.#` binding catches all; GM-960) |
| `uof.*` (vhost ext) | UOF.Client → external | white-label outbound feed |

## Environments — [Verified-argocd]

| | Dev | Staging | Production |
|---|---|---|---|
| AWS account | 418541840646 | 172065883315 | 525375333516 |
| Region | ap-south-1 | ap-south-1 | ap-east-1 |
| RabbitMQ | in-cluster svc | AWS MQ ap-south-1 (5671 TLS) | AWS MQ ap-east-1 (5671 TLS) |
| MongoDB | in-cluster | 10.49.100.55:27017 | 10.49.100.55:27017 ([Likely]) |
| Secrets | AWS SM `/dev/apps/*` | `/stg/apps/*` | `/prd/apps/*` |

## ⚠️ Secret hygiene — [Verified] (security finding)

`.gitignore` excludes `appsettings.Development.json` but **not** `appsettings.json`. Several
committed `appsettings.json` contain **live credentials**: RabbitMQ user/pass (Adapter,
Sportsbook with real AWS host), BetGenius API key/password (LiveStream), SQL RDS password
(Sportsbook `Translation`), platform API keys (UOF.Client, Websocket). Even though prod uses
secret injection, these are in git history and several point at real AWS resources. **Rotate and
remove.** Detailed list in [TechnicalDebt.md](./TechnicalDebt.md) (do not paste secret values
into new files).
