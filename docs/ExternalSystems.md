# External Systems

Third-party / out-of-workspace systems odds-engine-2 depends on (beyond the sibling repos, which are
covered in [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md)). Classification:
**[Verified]** (in odds-engine-2) · **[Verified-argocd]** · **[Unknown]**.

## 1. Genius Sports / BetGenius — the data provider

| Aspect | Detail | Class |
|---|---|---|
| **Role** | Origin of all sportsbook data (fixtures/markets/odds/settlement) + livestreams | [Verified-sibling:bet-genius] |
| **Feed ingestion** | Genius Sports → **bet-genius** repo (XML `Updategram` over HTTP) → RabbitMQ → OEv2. OEv2 does **not** call the trading feed directly. | [Verified-sibling:bet-genius] |
| **Video API (direct from OEv2)** | `OE.Job.LiveStream` via `BetGeniusRepository` calls `https://api.geniussports.com/`: `POST Auth-v1/PROD/login` (→ `BGAuth` bearer), `GET Video-v3/{env}/fixtures[/{id}]?bookingStatus=BOOKED` (→ `BGEvent(s)`). | [Verified] |
| **Auth** | login (user/password) → bearer token cached to `ExpiresIn`; `x-api-key` header (`Custom:BetGenius:Key`). ⚠️ login path hardcodes `PROD` while fixtures use `Custom:BetGenius:Env`. | [Verified] |
| **Config/env** | `Custom:BetGenius:{Uri,Env,User,Password,Key}`; staging env `UAT`. Credentials committed (security H1). | [Verified] |
| **Caching** | token TTL=ExpiresIn (`bg:auth`); fixtures ~5h (`bg:fixture:*`). | [Verified] |
| **Channel scope** | only `ChannelType.BG` fixtures get livestream sync. | [Verified] |

## 2. AWS — cloud platform — [Verified] / [Verified-argocd]

| Service | Use by OEv2 | Evidence |
|---|---|---|
| **Amazon MQ (RabbitMQ)** | the `e.central` broker in stg/prd (`b-…mq.ap-south-1.on.aws:5671`, TLS) | appsettings + argocd |
| **MongoDB / DocumentDB** | Sportsbook catalogue (`10.49.100.55:27017` stg/prd; in-cluster dev). 7/9 Dockerfiles pull the **AWS RDS global cert bundle** → suggests **DocumentDB** (TLS) in some envs | Dockerfiles, appsettings |
| **RDS SQL Server** | `Translation` DB (`stg-song88-mssql-server….rds.amazonaws.com`); `Trading` per-env | appsettings |
| **ECR** | container images `oev2-*` per env account | ci.yml |
| **Secrets Manager** | runtime secrets via External Secrets (`/<env>/apps/...`) | argocd |
| **IAM / OIDC** | GitHub Actions assumes roles via OIDC for ECR push | ci.yml |
| **EKS** | runs the workers (namespace `bet-genius-apps`) | argocd |
| **Accounts/regions** | dev 418541840646/ap-south-1, stg 172065883315/ap-south-1, prd 525375333516/ap-east-1 | ci.yml/argocd |

## 3. Observability backend — [Verified-argocd]
- **OpenTelemetry → Tempo** for traces (`OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo.monitoring.svc.cluster.local:4318`,
  `http/protobuf`). Metrics scraped on `:8080/metrics` (30 s). Provided by the cluster's monitoring
  stack (Grafana/Tempo). [Verified-argocd]. The exporter is wired by the private
  `Infra.Extensions.Telemetry` package. [Verified]

## 4. Edge / CDN — [Verified-sibling:platform-web] (not an OEv2 dependency)
- **Cloudflare** fronts platform-web (TLS termination, `CF-Connecting-IP`, HTTP→HTTPS at edge). OEv2
  has no edge/CDN dependency (no inbound HTTP). Listed for end-to-end completeness.

## 5. Azure SignalR Service — [Unknown]
- platform-web has an `EnableAzureSignalRService` flag; whether the `/platform` hub gateway uses an
  Azure SignalR backplane (and where its connection string lives) is **unverified**. If used, it is
  the scale-out backplane for the hub OEv2 publishes to. [Unknown] (#22)

## 6. Private package feeds — [Verified]
- **GitHub Packages (org FlyingCockatoo)**: hosts `OE.Shared` and the private
  `Infra.Extensions.Telemetry` package; Dockerfiles authenticate with `NUGET_PASSWORD`. [Verified]

## 7. The external white-label consumers — [Unknown]
- OEv2 **produces** a Unified Odds Feed to an external RabbitMQ vhost `ext` (`uof.*` keys) for
  white-label clients (e.g. `TSO`). The consuming systems are outside the workspace and their
  contracts/SLAs are **unverified**. [Unknown] (this is an *outbound* external dependency).

## External-dependency risk notes
- **Single MongoDB IP** (`10.49.100.55`) for stg/prd with no DNS indirection — operational coupling
  ([TechnicalDebt.md](./TechnicalDebt.md)).
- **BetGenius credentials + env-path mismatch** (`PROD` login vs configurable fixtures env) — verify
  per environment ([Unknowns.md](./Unknowns.md)).
- **Committed third-party secrets** (BetGenius key/password) — rotate (H1).
- All external calls are **outbound** except the provider feed, which arrives via bet-genius, not
  directly — so OEv2's external attack surface is small, but its **availability** depends on AWS MQ,
  Mongo, RDS, and (for livestream) the BetGenius Video API.
