# Hosting

How the services are hosted, packaged, deployed, scaled, and managed at runtime. All [Verified]
in-repo / [Verified-argocd] for deployment.

## 1. Process hosting model — .NET Generic Host

- Each service is a **single-process console app** (`OutputType=Exe` / `Sdk.Worker`) hosting the
  Generic Host (`Host.CreateDefaultBuilder`). One `IHostedService` (`Worker`) per process.
- **No web server.** No Kestrel, no listening sockets for app traffic. The only network I/O is
  outbound (RabbitMQ, MongoDB, SQL, HTTP to BetGenius/Platform, SignalR to platform-final).
- Lifecycle: `Run()`/`RunAsync()` blocks until SIGTERM/SIGINT → host fires `StopAsync` on the
  worker → dispose. (Shutdown completeness varies — see [StartupFlow.md](./StartupFlow.md) §7.)

## 2. Container packaging — [Verified]

Each service has a multi-stage Dockerfile:
- **Build stage:** `mcr.microsoft.com/dotnet/sdk:8.0` (or `9.0` for Cleanup/KeepAlive), restores
  with GitHub Packages auth (`NUGET_PASSWORD` build-arg, for `Infra.Extensions.Telemetry` /
  `OE.Shared`), publishes.
- **Runtime stage:** `mcr.microsoft.com/dotnet/aspnet:8.0` (or 9.0). *Note:* the `aspnet` runtime
  image is used even though these aren't web apps — harmless but heavier than `runtime`.
- 7/9 images download the **AWS RDS global certificate bundle** for TLS to DocumentDB/Mongo + RDS.
- `appsettings.*.json` (env-specific) are stripped at publish to force runtime env injection.
- Entrypoint: `dotnet OE.<Service>.dll` (mix of `ENTRYPOINT`/`CMD` across services — inconsistent).

## 3. Orchestration — Kubernetes via argocd GitOps — [Verified-argocd]

- Images pushed by CI (`ci.yml`) to per-environment **AWS ECR** (`oev2-<service>` names).
- Deployed by **argocd** using Helm chart `argocd/charts/betgenius-app` + per-service
  `apps/oddsengine/<service>/{dev,stg,prd}.yaml`. **argocd-image-updater** auto-bumps image tags
  (`main-sha-<sha>`) → continuous deployment.
- Namespace (dev): `bet-genius-apps` (e.g. RabbitMQ svc `rabbitmq-main-cluster.bet-genius-apps.svc`).
- Service config (queues, hosts, feature flags) injected as **env vars** with `__` delimiter;
  secrets via **External Secrets** from **AWS Secrets Manager**. (The committed `appsettings.json`
  is overridden — see [Configuration.md](./Configuration.md).)

## 4. Scaling & the sharding contract — [Verified]

Horizontal scale = **more instances of the same worker**, not threads/hosted-services per process.
The critical case:

- **OE.Sportsbook.Service runs as 11 instances `00`–`10`.** Each instance is a distinct argocd
  app (`fullnameOverride` like `sportsbook-service-00`) with its own queue
  (`q.sb.engine.<NN>`), consumer tag, and routing-key binding (`r.oe.adapter.#.<NN>`).
- The Adapter routes each fixture to a fixed instance via `Helper.GetInstanceName` (00–09) with
  `10` reserved for global bet-stop. **Changing the instance count requires changing the
  sharding math + all bindings together** (see Phase 1 [ArchitectureDecisions.md](./ArchitectureDecisions.md) ADR-003).
- **OE.Websocket.Service** runs **per-client** instances (krj/qte/g0x/oge/gsa), each bound to that
  client's routing keys (`*.s88`, etc.) and pushing to that client's hub groups.
- **UOF.Client** runs per external client (e.g. `TSO`, `oge-krj`).
- Stateless jobs (KeepAlive/Cleanup/Settlement/LiveStream/Lookup) run as single replicas
  (`replicaCount: 1` default). **Note:** several jobs are *not* safe to run multi-replica as-is
  (no leader election — duplicate publishes/deletes). [Likely] kept single-replica intentionally.

## 5. Health, liveness & observability — [Verified] (a gap)

- **No HTTP health/readiness endpoints** (no web host). Kubernetes liveness/readiness probes, if
  configured, would have to be exec/tcp-based — *not verified in argocd values*; see
  [Unknowns.md](./Unknowns.md).
- The closest thing to a health signal is **OE.Job.KeepAlive** publishing `r.oe.uof.heartbeat`
  every 5s — but `IsHealthy` is **hardcoded true**, so it confirms the *process is looping*, not
  that dependencies are healthy. Treat as a liveness ping only.
- Metrics are exported via OpenTelemetry (`Telemetry__MetricsPort: 8080`, `/metrics`, scrape
  interval 30s in the Helm values) to the cluster's collector; traces to Tempo
  (`OTEL_EXPORTER_OTLP_ENDPOINT=http://tempo.monitoring.svc.cluster.local:4318`). This is the
  real operational visibility — see [Logging.md](./Logging.md).

## 6. Runtime environment variables (argocd) — [Verified-argocd]

| Env var | Meaning |
|---|---|
| `ASPNETCORE_URLS` / `DOTNET_URLS` | set to `:3000` by the shared chart (unused by non-web workers) |
| `OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, `OTEL_EXPORTER_OTLP_ENDPOINT` | telemetry export |
| `Telemetry__Enabled`, `Telemetry__Environment`, `Telemetry__MetricsPort` | telemetry config |
| `MessageBroker__*` | broker host/vhost/exchange/queue/consumer/routingkey per service |
| `MessageBroker__Username/Password` | from External Secrets (`/<env>/apps/rabbitmq/*`) |
| `Custom__MongoDB-Sportsbook` | from External Secrets (`/<env>/apps/mongodb-instance/connection-string/sportsbook`) |
| `Serilog__WriteTo__0__Args__formatter` | Compact JSON formatter |

## 7. Hosting topology summary

```
AWS ECR (oev2-*)  ──CI push──►  argocd (Helm betgenius-app) ──►  EKS namespace bet-genius-apps
                                                                    │
   per-service Deployments (env vars + External Secrets):          │
   • adapter-service              (1)        ──┐                    │
   • sportsbook-service-00..10    (11)         │  RabbitMQ (AWS MQ / in-cluster)
   • uof-client-<client>          (n)          │  MongoDB (10.49.100.55 / in-cluster)
   • websocket-service-<client>   (n)          │  SQL Server (AWS RDS)
   • job-lookup / settlement / cleanup / livestream / keepalive (1 each)
                                              ──┘  → Tempo (traces) + metrics scrape :8080
```

## 8. Local hosting — [Likely]
`dotnet run` per service uses committed `appsettings.json` (+ `appsettings.Development.json`,
git-ignored) and needs local RabbitMQ + MongoDB (+ SQL for Sportsbook/UOF). No in-repo
docker-compose was found — [Unknown] whether a local stack is documented elsewhere.
