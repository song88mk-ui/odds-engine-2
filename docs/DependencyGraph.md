# Dependency Graph

## 1. Project references (compile-time) — [Verified]

Every executable references **only** `OE.Shared`. There are **no worker→worker** references.

```
OE.Shared  (netstandard2.1)
   ▲  ▲  ▲  ▲  ▲  ▲  ▲  ▲  ▲
   │  │  │  │  │  │  │  │  │   (ProjectReference)
   │  │  │  │  │  │  │  │  └── OE.Job.KeepAlive
   │  │  │  │  │  │  │  └───── OE.Job.Lookup
   │  │  │  │  │  │  └──────── OE.Job.LiveStream
   │  │  │  │  │  └─────────── OE.Job.Settlement
   │  │  │  │  └────────────── OE.Job.Cleanup
   │  │  │  └───────────────── OE.Websocket.Service
   │  │  └──────────────────── OE.UOF.Client
   │  └─────────────────────── OE.Sportsbook.Service
   └────────────────────────── OE.Adapter.Service
```

Evidence: each `*.csproj` has exactly one `<ProjectReference Include="..\OE.Shared\OE.Shared.csproj" />`.

## 2. NuGet package references — [Verified]

### OE.Shared (the kernel)
| Package | Version | Why |
|---|---|---|
| Microsoft.EntityFrameworkCore (+ .SqlServer) | 3.1.32 | SQL Server contexts (Trading, Translation) |
| MongoDB.Bson | 3.4.0 | Mongo document models / `[BsonElement]` mapping |
| RabbitMQ.Client | 5.2.0 | The broker (`MessageBroker`) |
| OpenTelemetry.Api | 1.14.0 | Activity/trace propagation |
| System.Runtime.Caching | 9.0.5 | `MemCache` (MemoryCache) |
| System.Text.Json | 9.0.5 | (de)serialization |
| Microsoft.Extensions.Configuration.Abstractions | 9.0.5 | config access |

> Note: **EF Core 3.1** (end-of-life) is pinned because OE.Shared targets netstandard2.1.
> Tracked in [TechnicalDebt.md](./TechnicalDebt.md).

### Common across services
- **Serilog** stack (4.3.0) + `Serilog.AspNetCore` 10.0.0 + Compact formatter + Console sink.
- **Infra.Extensions.Telemetry** `0.1.1-5` — private package providing `AddTelemetry(...)`.
- **Microsoft.Extensions.Hosting** 10.0.11 — unified across all services by GM-751.

### Service-specific
| Package | Used by | Why |
|---|---|---|
| MongoDB.Driver 3.4.0/3.6.0 | Sportsbook, Lookup, Settlement, Cleanup, LiveStream | Mongo access (driver versions differ) |
| Microsoft.AspNetCore.SignalR.Client 10.0.13 (+ MessagePack protocol) | Websocket.Service | SignalR client to `/platform` |
| Microsoft.Extensions.Http 10.0.11 | Lookup, Settlement | `IHttpClientFactory` |
| IdGen 3.0.7 | Sportsbook.Service | **referenced but unused** (snowflake id gen) |
| System.Security.Cryptography.Pkcs 9.0.21 | OE.Shared (flows to every project and the published package) | security pin over Microsoft.Data.SqlClient 7.0.x's vulnerable 8.0.1/9.0.13 |

> **Inconsistency:** `MongoDB.Driver` split 3.4.0 vs 3.6.0 remains (deliberately untouched by
> GM-751 — the driver upgrade is a separate effort). TFMs, `Microsoft.Extensions.Hosting`, and
> SDK images were unified on .NET 10 / 10.0.11 by GM-751. See debt doc.

## 3. External infrastructure dependencies — [Verified] / [Verified-argocd]

| Dependency | Used by | Where |
|---|---|---|
| **RabbitMQ** (`e.central` topic exchange) | Adapter, Sportsbook, UOF.Client, Websocket, Lookup, Settlement, KeepAlive | AWS MQ (stg/prd) or in-cluster `rabbitmq-main-cluster.bet-genius-apps.svc` (dev) |
| **External RabbitMQ** (vhost `ext`) | UOF.Client | white-label egress |
| **MongoDB `Sportsbook`** | Sportsbook, Lookup, Settlement, Cleanup, LiveStream, Websocket | `10.49.100.55:27017` (stg/prd) / in-cluster (dev) |
| **SQL Server `Translation`** | Sportsbook.Service | AWS RDS (`stg-song88-mssql-server…`) |
| **SQL Server `Trading`** | UOF.Client | per-env |
| **BetGenius Video API** | Job.LiveStream (via OE.Shared provider) | `https://api.geniussports.com/` |
| **Platform REST API** | UOF.Client, Websocket.Service | `platform-final` |
| **Platform SignalR `/platform`** | Websocket.Service | `platform-final` |

## 4. Message-flow (runtime) dependency graph — [Verified] for OEv2 keys

The **real** coupling between services is the routing-key namespace, not code references:

```
 r.bg.receiver.*  ─┐
 r.uof.*          ─┴─► OE.Adapter.Service ─► r.oe.adapter.<type>.<NN>
                                              │
        ┌─────────────────────────────────────┼───────────────────────────────┐
        ▼                                     ▼                                ▼
 OE.Sportsbook.Service(NN)            OE.Job.Lookup                    OE.UOF.Client
   │  └─► MongoDB Sportsbook           └─► MongoDB *Lookup              └─► EXTERNAL broker
   ├─► r.oe.sb.engine.on_odds_change                                       (vhost ext)
   └─► r.oe.bet.settlement
        │                    │
        ▼                    ▼
 OE.Websocket.Service   platform-final (Settlement.Service: q.bet.settlement
   └─► /platform hub       binds r.bet.settlement|r.oe.bet.settlement|r.of.bet.settlement)

 OE.Job.KeepAlive ─► r.oe.uof.heartbeat ─► OE.UOF.Client
```

Routing-key reference table is in [ConfigurationOverview.md](./ConfigurationOverview.md).

## 5. Build/release dependency — [Verified]

- **CI (`ci.yml`)** builds each service from its **directory** via a matrix + changed-file
  detection, pushing images to per-env AWS ECR. It does **not** depend on `OddsEngine.sln`,
  which is why `OE.Job.KeepAlive` (absent from the .sln) still builds.
- **`oe-shared-npkg.yml`** publishes `OE.Shared` as a versioned NuGet package on changes under
  `OE.Shared/**`. Services consume `OE.Shared` via **ProjectReference** in-repo, but the
  package also feeds **bet-genius** (cross-repo contract sharing) via GitHub Packages.
- Dockerfiles authenticate to GitHub Packages with `NUGET_PASSWORD` build-arg to restore
  `Infra.Extensions.Telemetry` (and potentially `OE.Shared`).
