# Architecture Decisions (inferred)

These ADRs are **reverse-engineered** from the code — they were not found written down. Each
states the decision, the evidence, the likely rationale, and the consequences. Treat rationale as
**[Likely]** unless noted.

---

## ADR-001 — Event-driven worker pipeline over RabbitMQ (not a monolith / not RPC)
- **Decision:** Decompose odds processing into independent worker processes coordinated only via a
  RabbitMQ topic exchange.
- **Evidence:** 9 `Exe` workers, zero worker→worker code references, all coupling via routing keys
  (`MessageBroker.cs`, all `Worker.cs`). [Verified]
- **Rationale [Likely]:** independent scaling of hot stages, fault isolation, and absorbing bursty
  real-time feed load with queue buffering + at-least-once delivery.
- **Consequences:** eventual consistency; the routing-key namespace is the de-facto API; debugging
  spans processes (mitigated by OTel trace propagation through message headers).

## ADR-002 — `OE.Shared` shared kernel as netstandard2.1, published as NuGet
- **Decision:** One library holds contracts/models/enums/utilities; shared in-repo via
  ProjectReference and cross-repo (bet-genius) via a GitHub Packages NuGet.
- **Evidence:** `OE.Shared.csproj` (PackageId/Version), `oe-shared-npkg.yml`, bet-genius consumes
  the same types. [Verified]
- **Rationale [Likely]:** guarantee bet-genius and OEv2 serialize/deserialize identical message
  shapes; netstandard2.1 maximizes consumer-TFM compatibility.
- **Consequences:** EF Core pinned to 3.1 (netstandard2.1 ceiling) → carries EOL dependency; any
  contract edit is a coordinated multi-repo release.

## ADR-003 — Deterministic sticky sharding of fixtures across Sportsbook instances ⭐
- **Decision:** Route every fixture's events to a fixed instance `00–09` derived from the last two
  digits of the provider fixture id; reserve instance `10` for global bet-stop. Run 11 Sportsbook
  instances.
- **Evidence:** `Helper.GetInstanceName` (`Helper.cs:8-23`); adapter publishes `…<NN>`
  (`TradingRepository.cs:58,121`); global → `.10` (`:75`); argocd `dev-00…dev-10`. [Verified]
- **Rationale [Likely]:** eliminate cross-instance write races on a fixture's Mongo documents
  while distributing load; keep per-fixture ordering without a global lock.
- **Consequences:** the digit math is **load-balancing-by-id-suffix** — distribution quality
  depends on id digit uniformity (`(last/2)*2 + secondLast%2`). The router and consumer binding are
  tightly coupled; changing shard count or formula requires synchronized redeploy of Adapter +
  Sportsbook + argocd. Global bet-stop on a dedicated shard avoids broadcasting to all shards.

## ADR-004 — MongoDB as operational catalogue, SQL Server for translations/trading config
- **Decision:** Hot, high-churn catalogue (fixtures/markets/selections) in MongoDB; relatively
  static reference data (translations, client trading config) in SQL Server.
- **Evidence:** Mongo collections + `MongoIndexBootstrapper`; `TranslationContext`/`TradingContext`
  (EF Core). [Verified]
- **Rationale [Likely]:** Mongo upserts + flexible schema suit rapid odds churn; SQL suits
  relational reference data and existing platform data.
- **Consequences:** two persistence models to operate; translations bridged via MemCache to avoid
  per-message SQL hits.

## ADR-005 — Abbreviated field naming for documents & wire messages
- **Decision:** Use 2–6 char abbreviations (`FiId`, `MaStId`, …) everywhere on the wire and in Mongo.
- **Evidence:** every collection/message DTO. [Verified]
- **Rationale [Likely]:** shrink very high-volume payloads (storage + network + SignalR
  MessagePack frames).
- **Consequences:** steep readability cost; a glossary is mandatory (see SharedLibraries.md). New
  fields must follow the convention to stay consistent.

## ADR-006 — Dedicated external broker/vhost for the white-label feed (UOF.Client)
- **Decision:** Egress to external clients goes to a **separate** RabbitMQ connection/vhost (`ext`),
  not the internal `e.central`.
- **Evidence:** `ExternalConnect`/`ExternalPublish`, `ExternalMessageBroker` config. [Verified]
- **Rationale [Likely]:** isolate external consumers from internal topology/security; allow
  per-client transformation (margin, Asian odds) at the boundary.
- **Consequences:** UOF.Client manages two connections; external contract (`uof.*`) is yet another
  versioned surface.

## ADR-007 — Push to UI via a SignalR client bridge rather than direct UI subscription to RabbitMQ
- **Decision:** OE.Websocket.Service consumes RabbitMQ and **calls into** platform-final's SignalR
  hub, which fans out to browsers.
- **Evidence:** SignalR.Client usage + hub methods in platform-final. [Verified]/[Verified-in-platform-final]
- **Rationale [Likely]:** keep RabbitMQ internal; let the platform own connection/group/auth
  management for end users; allow per-client websocket instances.
- **Consequences:** an extra hop and an extra contract (hub method signatures); per-client
  Websocket deployments.

## ADR-008 — GitOps deploy via argocd with env-var + secret overrides
- **Decision:** Images built per-service to ECR; argocd Helm values inject env/secrets per env;
  image-updater auto-bumps tags.
- **Evidence:** `ci.yml`, `argocd/charts/betgenius-app`, per-service `{env}.yaml`. [Verified-argocd]
- **Rationale [Likely]:** standard GitOps; keep secrets out of images; continuous deploy.
- **Consequences:** committed appsettings are dev stubs, not prod truth (a frequent source of
  confusion — call it out when onboarding).

---

## Decisions that look accidental rather than intentional (flag for review)
- Mixed TFMs (net8 vs net9) and package versions — [Likely] drift, not a decision.
- `OE.Job.KeepAlive` missing from the `.sln`; `OE.API` left as an empty shell — [Likely] oversight.
- `KeepAlive.IsHealthy` hardcoded true — [Likely] unfinished, not intended as final design.
See [TechnicalDebt.md](./TechnicalDebt.md).
