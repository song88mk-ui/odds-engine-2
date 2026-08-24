# Architecture Risks

Risk register for odds-engine-2: high-risk areas, architecture violations, dependency analysis,
inconsistent patterns. Each finding: **Evidence / Files / Confidence / Suggested investigation**.
Per Phase-10 rules, **no refactoring is suggested** — only investigation. Severity = Likelihood×Impact
(🔴 high / 🟠 medium / 🟡 low). Detailed code-level items: [TechnicalDebt.md](./TechnicalDebt.md).

## Dependency analysis (validated)
- **Circular dependencies: NONE.** [Confidence: High] Evidence: every `.csproj` references only
  `OE.Shared`; `OE.Shared.csproj` has 0 `ProjectReference`s (`grep ProjectReference` across solution).
  Files: all `OE.*/*.csproj`. No worker→worker edges → acyclic.
- **Coupling is via contracts, not code.** Runtime coupling is the routing-key namespace + shared
  Mongo/SQL schemas + SignalR models — see [Contracts.md](./Contracts.md). [High]

## High-risk areas (risk register)

### R1 🔴 Catalogue correctness under concurrency — bet-stop race
- **Evidence:** `_IsBetStopGlobally` (plain bool) read/written without lock under prefetch 1000 in a
  Singleton repo; check-then-act in `ProcessOnBetStopGlobalMessage`.
- **Files:** `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:48,220,232`.
- **Impact:** bets could be accepted during a global stop (financial). **Confidence:** High (code) /
  Medium (real-world trigger frequency).
- **Suggested investigation:** trace concurrent `OnBetStopGlobal` handling under load; confirm whether
  one consumer thread per connection serializes this in practice; review prod logs for stop/resume flaps.

### R2 🔴 Real-time delivery service stability (Websocket)
- **Evidence:** ~13 `new Thread()` on `BlockingCollection`, no `CancellationToken`/join; `async void`
  handlers; `Environment.Exit(1)` on error.
- **Files:** `OE.Websocket.Service/Worker.cs`.
- **Impact:** abrupt process kills, zombie threads on shutdown, dropped in-flight messages → UI
  outages. **Confidence:** High.
- **Suggested investigation:** reproduce a handler exception in a non-prod instance; observe shutdown
  draining and message loss; review restart frequency in prod.

### R3 🔴 Secret exposure in source control
- **Evidence:** live RabbitMQ/BetGenius/SQL/API credentials committed; `.gitignore` excludes only
  `appsettings.Development.json`.
- **Files:** `OE.{Adapter,Sportsbook,Job.LiveStream,Job.Settlement,Websocket,UOF.Client}.../appsettings.json`.
- **Impact:** credential compromise (some point at real AWS resources). **Confidence:** High.
- **Suggested investigation:** confirm which committed creds are still valid in each env; audit git
  history exposure window; verify prod truly overrides all of them via External Secrets.

### R4 🟠 Sharding contract coupling (3-way)
- **Evidence:** `GetInstanceName` digit math must stay in lock-step across the Adapter (router), the
  Sportsbook binding, and the argocd instance count; global stop hardcoded to shard "10".
- **Files:** `OE.Shared/Utilities/Helper.cs:8-23`, `OE.Adapter.Service/Repositories/TradingRepository.cs:58,75,121`,
  `argocd/apps/oddsengine/sportsbook-service/*`.
- **Impact:** a change in any one without the others mis-routes whole fixtures. **Confidence:** High
  (mechanism) / Medium (prod shard count unverified — A3).
- **Suggested investigation:** confirm stg/prd shard count + routing keys in argocd; measure shard
  load distribution (digit-suffix uniformity).

### R5 🟠 Silent failure modes mask incidents
- **Evidence:** `w=0` unacked Mongo writes; empty `catch {}` (Lookup, UOF Platform repo); `async void`
  startup load; KeepAlive `IsHealthy` hardcoded true.
- **Files:** Mongo conn string + `SportsbookRepository.cs:583`; `OE.Job.Lookup/Worker.cs:81-84`;
  `OE.UOF.Client/Repository/PlatformRepository.cs:33,43-45`; `OE.Job.KeepAlive/Worker.cs`.
- **Impact:** failures invisible to logs/monitoring; false-healthy signals. **Confidence:** High.
- **Suggested investigation:** add targeted logging in a non-prod build to quantify how often these
  paths swallow errors; verify monitoring doesn't rely on the KeepAlive health flag.

### R6 🟠 External availability dependencies
- **Evidence:** single MongoDB IP `10.49.100.55` (no DNS) stg/prd; BetGenius Video API; AWS MQ/RDS.
- **Files:** appsettings `Custom:MongoDB-Sportsbook`; `OE.Job.LiveStream/appsettings.json`.
- **Impact:** infra change/outage breaks ingestion/persistence; hardcoded IP is brittle.
  **Confidence:** High (committed) / Medium (prod via argocd).
- **Suggested investigation:** confirm prod Mongo addressing in argocd; review failover/HA for MQ/Mongo/RDS.

### R7 🟠 Cross-instance cache-invalidation delivery
- **Evidence:** per-process MemoryCache; translation purge must reach every Sportsbook shard, but
  Sportsbook's committed binding is only `r.oe.adapter.#.NN` — the invalidation binding isn't visible.
- **Files:** `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:323`, `appsettings.json`.
- **Impact:** stale translations on some shards up to 60-min TTL. **Confidence:** Medium.
- **Suggested investigation:** trace how `OnDynamicTranslationChange` is delivered to Sportsbook
  (argocd extra binding?); confirm fan-out to all shards. (Unknowns #23)
- **(Corrected 2026-06-30: translations removed from pipeline.)** This risk is now **moot in practice** —
  the `sb:translation:*` cache is no longer populated, so the purge has nothing to invalidate (no-op).
  Re-open only if a cached translation read path is reintroduced. See [TechnicalDebt.md](./TechnicalDebt.md) H7.

### R8 🟡 Unverified upstream source (`r.uof.*`)
- **Evidence:** Adapter binds `r.uof.#`/`r.of.recovery.#.tso`; no producer in analyzed repos.
- **Files:** `OE.Adapter.Service` / `OE.UOF.Client` appsettings.
- **Impact:** an undocumented input service; recovery-channel behavior unknown. **Confidence:** Low.
- **Suggested investigation:** grep `oddsfeed`/`oddsengine` for `r.uof.`/`r.of.` publishers. (Unknowns #1/#2)

### R9 🟠 SignalR `*NaTr` removed — OEv2 + platform-web must deploy together (C6)
- **Evidence (2026-06-30):** the `*NaTr` translation properties (`SpNaTr/SegNaTr/LeNaTr/FiNaTr/MaNaTr/
  SeNaTr`) were removed from `OutgoingOddsChange`/`OutgoingBoostedOddsChange`, and
  `OE.Websocket.Service/Repository/SportsbookRepository.cs` no longer maps them. SignalR uses MessagePack
  `ContractlessStandardResolver` (name-keyed — contract C6, see [Contracts.md](./Contracts.md)).
- **Impact:** platform-web's mirror model `OddsChangeFixtureMessageModel` **won't crash** on the now-missing
  keys (missing keys deserialize to empty), but OEv2 **no longer pushes localized names**. The UI must
  rely on its REST `GetTranslations()` path (or fall back to default-language names like `SpNa`/`FiNa`).
  **OEv2 + platform-web must deploy together.** **Confidence:** High (OEv2 side) / [Unknown] whether
  platform-web actually consumed the message `*NaTr` or relied solely on the REST path (Unknowns — see below).
- **Suggested investigation:** confirm in platform-web whether `*NaTr` from the websocket message was ever
  rendered; coordinate the joint deploy. See [TechnicalDebt.md](./TechnicalDebt.md) H7.

## Architecture "violations" (deviations from the system's own conventions)
> Not classic layering violations (the architecture is clean); these are internal inconsistencies.

| # | Deviation | Evidence / Files | Confidence | Suggested investigation |
|---|---|---|---|---|
| V1 | Class names misdescribe responsibility/store | `TradingRepository` (Adapter=router, UOF=SQL), `PlatformRepository` (HTTP) | High | confirm intent / naming history |
| V2 | Dual Mongo client (DI vs self-newed) | `Program.cs:61-68` vs `SportsbookRepository.cs:340` | High | confirm whether the DI client is meant to be used |
| V3 | `OE.Job.Lookup` skips Serilog/telemetry | `OE.Job.Lookup/Program.cs` | High | confirm logging path for this job in prod |
| V4 | Settlement reads but doesn't write Mongo despite `EditSportsbook` | `OE.Job.Settlement` | Medium | trace who actually writes the fixture-close |
| V5 | Build relies on CI matrix, not the `.sln` (KeepAlive absent; OE.API dead) | `OddsEngine.sln`, `.github/workflows/ci.yml` | High | confirm KeepAlive is intended; confirm OE.API removable |

## Duplicate implementations (validated)
| Duplicate | Count | Files | Confidence | Suggested investigation |
|---|---|---|---|---|
| `SportsbookRepository` | **6** | Cleanup/LiveStream/Lookup/Settlement/Sportsbook/Websocket `*/SportsbookRepository.cs` | High | confirm whether divergence is intentional per-service vs copy-paste drift |
| `TradingRepository` | 2 | Adapter (router) + UOF.Client (SQL) | High | (different roles — likely intentional) |
| `BaseResponseDTO` | 2 | `OE.Shared/Models/Responses` + `OE.UOF.Client/Models/DTOs` | High | confirm which is canonical; check for field drift |
| `Instrumentation` | 8 | per-service `Telemetry/Instrumentation.cs` | High | intentional per-service telemetry boilerplate |
| Mongo `Data/Sportsbook` schema | 2 repos | OEv2 `OE.Shared` + backoffice-final inline copy | Medium (sibling) | diff for drift (#24) |

## Inconsistent patterns (system-level)
Mixed `MongoDB.Driver` versions (3.4.0 vs 3.6.0), mixed
`Nullable`/`ImplicitUsings`, two `Program.cs` styles, Dockerfile `ENTRYPOINT`/`CMD` + output-path
variance, abbreviated vs full field names across message generations. (TFMs and
`Microsoft.Extensions.*` were unified on `net10.0`/10.0.11 by GM-751, 2026-08-24.) Evidence/files:
[TechnicalDebt.md](./TechnicalDebt.md) M3 + [CodingStandards.md](./CodingStandards.md). Confidence: High.
Suggested investigation: confirm intended `MongoDB.Driver` baseline before the next service.

## Risk heatmap
| | Catalogue/bet-stop | Websocket delivery | Secrets | Sharding | Silent failures | External infra |
|---|---|---|---|---|---|---|
| Likelihood | Med | Med-High | High | Low | Med | Low-Med |
| Impact | High | High | High | High | Med | High |
| **Severity** | 🔴 R1 | 🔴 R2 | 🔴 R3 | 🟠 R4 | 🟠 R5 | 🟠 R6 |

> All items are **observations with suggested investigation** — no remediation/refactor is proposed
> here (out of Phase-10 scope). Prioritization for any future work would start with R1–R3.
