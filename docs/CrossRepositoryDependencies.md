# Cross-Repository Dependencies (master matrix)

> **Supersedes the Phase 1 version of this file** — expanded with verified analysis of
> **backoffice-final** and **Auth**, and the full contract classification. Companion docs:
> [OddsEngineIntegration.md](./OddsEngineIntegration.md) (boundary spec), [Contracts.md](./Contracts.md),
> [PlatformApi.md](./PlatformApi.md), [WebSocket.md](./WebSocket.md),
> [AuthenticationIntegration.md](./AuthenticationIntegration.md), [ExternalSystems.md](./ExternalSystems.md).

Classification: **[Verified]** (odds-engine-2) · **[Verified-sibling:X]** · **[Historical:Reference]**
· **[Unknown]**. *Never infer external behavior — unverified items are marked Unknown.*

## Workspace map (odds-engine-2's neighbours)

| Repo | What it is | Relationship to OEv2 | Integration channel(s) | Class |
|---|---|---|---|---|
| **bet-genius** | .NET Genius Sports feed integration | **Upstream source** | RabbitMQ `r.bg.receiver.*`; shares `OE.Shared` contracts | [Verified-sibling:bet-genius] |
| **platform-final** | .NET backend (REST + `Platform.Websocket.API2` + workers) | **Downstream consumer** | reads Mongo catalogue; consumes `r.oe.bet.settlement`; serves REST OEv2 calls; hosts/relays the `/platform` hub; `CacheSync.Service` binds `r.oe.cachesync.#` | [Verified-sibling:platform-final] |
| **platform-web** | Blazor Server UI (net7) | **End consumer (2 hops)** | SignalR client of `/platform`; REST client of platform-final | [Verified-sibling:platform-web] |
| **backoffice-final** | .NET admin app (API net3.1 + Blazor net8 + workers) | **Config/admin authority** | writes SQL `Trading`/`Translation`/`Sportsbook` config OEv2 reads; reads Mongo catalogue; publishes `r.oe.cachesync.*`; mirrors `OE.Shared.Data.Sportsbook` | [Verified-sibling:backoffice-final] |
| **Auth** | .NET 8 JWT + operator-token issuer | **No direct dependency** | issues `x-operator-token`/JWT for platform (Platform.Operator.API); OEv2 does not call it | [Verified-sibling:Auth] |
| **argocd** | GitOps manifests (Helm `betgenius-app`) | **Deploys OEv2** | per-service `{env}.yaml`, ECR images, env+secret injection | [Verified-sibling:argocd] |
| **infrastructure** | Helm/helmfile shared infra **+ `flyway/` — the SQL schema source of truth** | **Hosts deps; owns all SQL DDL** | RabbitMQ cluster, MongoDB, secret-stores; `flyway/migrations/<env>/` applied via `just migrate <env>` | [Verified-sibling:infrastructure] |
| **oddsfeed** | not analyzed | **Likely the `r.uof.*` producer** | RabbitMQ `r.uof.*` / `r.of.recovery.*` | [Unknown] |
| **oddsengine** | obsolete v1 | none (superseded) | — | [Verified-sibling:argocd] (v1 deploys exist) |
| **Reference** | pre-AI baseline snapshot | comparison only | — | [Historical:Reference] |

## Per-integration detail

### bet-genius → OEv2 — [Verified-sibling:bet-genius]
- Purpose: ingest the Genius Sports feed (XML `Updategram` over HTTP), normalize, publish.
- Contract: `r.bg.receiver.onfixturechan/onmarketchange/onmarketsettlement/ononmatchsummary` carrying
  `OE.Shared` `Outgoing*Message` DTOs; OE.Adapter binds `r.bg.receiver.#|r.uof.#`.
- Shared library: **`OE.Shared`** (bet-genius depends on the same contracts).
- Shared infra: `e.central` RabbitMQ; possibly translation service.

### platform-final ↔ OEv2 — [Verified-sibling:platform-final]
- Reads the **MongoDB Sportsbook** catalogue OEv2 writes (never writes Mongo).
- Consumes `r.oe.bet.settlement` (`Settlement.Service`, queue `q.bet.settlement`).
- Serves REST OEv2 calls (`markettemplateplayerprops`, etc.) — see [PlatformApi.md](./PlatformApi.md).
- `CacheSync.Service` binds `r.oe.cachesync.#` (consumes backoffice config-change events).
- Relays bet/wallet/cashout to the `/platform` hub that OEv2's Websocket also pushes to.
- **(2026-07-13) OEv2 consumes `r.bet.placement`** (`OnBetPlacement`, published by
  `Platform.Betting.API`): `OE.Translation.Job` (flags translation rows) and `OE.Job.Cleanup`
  (protects fixtures with bets from the retention purge). Both deserialize a **minimal projection**
  of `Platform.Shared.MessageModels.OnBetPlacement` — renaming `BetSelections`/`BetSelection`/
  `FixtureId` (+ template ids for the translation job) is a breaking change.
- **(2026-07-13) OEv2 `OE.Job.Cleanup` reads the platform Betting SQL DB** (read-only,
  `SELECT DISTINCT FixtureId FROM BetSelection` at startup via `ConnectionStrings:Betting`) to
  preload fixtures with bet history. Renaming the `BetSelection` table/columns breaks the preload.
  Note: only the primary Betting DB is preloaded — RedCat/ESport/Casino betting DBs are not
  [Unknown whether their bets reference OEv2 fixtures].

### platform-web → OEv2 (2 hops) — [Verified-sibling:platform-web]
- SignalR client of `/platform` (receives OEv2 pushes); REST client of platform-final (reads the
  catalogue). See [WebSocket.md](./WebSocket.md), [RequestLifecycle.md](./RequestLifecycle.md).

### backoffice-final ↔ OEv2 — [Verified-sibling:backoffice-final]
- **Writes** the SQL config OEv2 reads: `ClientOddsFeedConfiguration` (UOF entitlement), boosted/
  priceboost, market-template groups, sort orders, mappings; and `Translation` names.
- **Reads** the Mongo catalogue (secondary preference) for admin display.
- **Publishes** `r.oe.cachesync.*` (`OnClientChange`/`OnParlayBonusChange`/`OnMarketSortOrderChange`/
  `OnSportSortOrderChange`) + `r.bo.config.*`; consumed by platform `CacheSync.Service`; OEv2
  Sportsbook handles `OnDynamicTranslationChange` (exact binding [Unknown]).
- **Reuses `OE.Shared.Data.Sportsbook` namespaces** (inline copies, not NuGet) — schema-coupled.
- **(2026-10-22) OEv2 `OE.Job.BoostedOddsCap` reads two Trading SQL tables backoffice-final writes** —
  `PriceBoostConfig` and `BoostedSettings` (the Cap %) — through its own read-only context
  (`ConnectionStrings:Trading`, reloaded every `Custom:RefreshSeconds`), and **publishes
  `OnNewNotification` on `r.oe.bm.engine.notifications`**, which `Backoffice.WebSocket.Service` binds and
  pushes to Backoffice.Web as a toast. Breaking for the job: renaming those tables or columns (Flyway
  `V1__Baseline`, `Trading.dbo.*`), changing the `NotificationsDetailsModel` JSON shape, or renaming the Manage
  Boost page (`/hk/odds-engine/manage-boost`) or its permission (`SBC.OE.PriceBoost.Manage`), both of which
  are configuration of the job. See [ProjectOverview.md](./ProjectOverview.md).
- **(2026-10-27) The same job reads `Backoffice.dbo.ClientInformation`** (`Brand`, `Email`, `IsEmailNotification`,
  `IsDeleted`; `ConnectionStrings:Backoffice`, only when email is on) for the clients to email. That is the table
  backoffice-final's Clients page edits, and it replaces `Trading.dbo.Client`, which backoffice-final retired
  (BO-42: infrastructure flyway `V18__RetireTradingClientTable`, a rename to `Client_DELETE_AFTER_BO42` and a later
  drop) because it was only a partial copy of the client list. Renaming those columns, or changing what the Notify
  email switch means, breaks the recipients; the toast keeps working.
- Deployed via the same CI→ECR→argocd pipeline (`backoffice-*` images).
- Full detail: [OddsEngineIntegration.md](./OddsEngineIntegration.md) §4.

### Auth — [Verified-sibling:Auth] (NOT an OEv2 dependency)
- .NET 8 API issuing **JWT** (HS256, issuer `bo.auth.api`) + **operator tokens** (GUID, DB-stored,
  cached) validated at `/v1/authentication/operator`; own API key + own SQL `Auth` DB; **no RabbitMQ**.
- **platform-final** (`Platform.Operator.API`) calls Auth for login/operator-token; **platform-web**
  sends `x-operator-token`. **odds-engine-2 has no code path to Auth** (verified by grep). See
  [AuthenticationIntegration.md](./AuthenticationIntegration.md).

### argocd / infrastructure — [Verified-sibling:argocd]/[Verified-sibling:infrastructure]
- Deployment + shared infra. Detail in Phase 1/2 [Hosting.md](./Hosting.md) and
  [ConfigurationOverview.md](./ConfigurationOverview.md).

## Shared-infrastructure dependency map
| Resource | Shared by |
|---|---|
| RabbitMQ `e.central` | bet-genius, all OE.*, platform-final, backoffice-final |
| MongoDB `Sportsbook` | OEv2 (write) · platform-final, backoffice-final (read) |
| SQL `Trading` | backoffice-final (write) · OEv2 UOF.Client and OE.Job.BoostedOddsCap (read) |
| SQL `Backoffice` (`ClientInformation`) | backoffice-final (read/write) · OEv2 OE.Job.BoostedOddsCap (read, email recipients only) |
| SQL `Translation` | OEv2 Translation.Service (read/write) · Translation.Job (write) · Adapter (read) · the external translation pipeline (writes `TARGET_TEXT_*`) · backoffice · **plus unrelated tenants in the same DB** (`SportsApiTranslationCache`, `SportsbookStatic`, `BackofficeStatic`) |
| SQL `Sportsbook` (order/template config) | backoffice-final (read/write) · platform [Likely] |
| SQL `Auth` | Auth only |
| `OE.Shared` contracts | OEv2 + bet-genius (NuGet) + backoffice-final (inline copy) |
| AWS ECR / Secrets Manager / argocd / OIDC | all .NET repos |

## Deployment dependency ordering [Likely]
Shared infra (RabbitMQ, Mongo, SQL) must exist first; `OE.Shared` package version must be compatible
across OEv2 + bet-genius; contract changes require coordinated deploys (see [Contracts.md](./Contracts.md)).
No explicit cross-repo deploy orchestration was found beyond argocd image-updater per repo. [Unknown]
