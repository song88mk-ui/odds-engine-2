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
- **Incident 2026-09-03 (GM-791):** the hub (`websocket-api.platform.svc:3000`) refused the initial
  connect, `ExecuteAsync` faulted, the host stopped (`BackgroundServiceExceptionBehavior=StopHost`),
  but the 13 dispatch threads were foreground threads blocked in `Take()`, so the process never exited.
  The container stayed Running with no consumer and, with no liveness probe in argocd, was never
  restarted. **Mitigation shipped:** background dispatch threads, startup connect with retry/backoff,
  `StopAsync` cancelling the stopping token. Residual: `Environment.Exit(1)`, `async void` loops, and the
  missing liveness probe (argocd, DevOps).
- **Scaling to 500+ clients (assessed 2026-09-03, GM-791):** startup load is now bounded (16 clients at
  a time) and readiness is per client, so a slow Platform API degrades gracefully instead of leaving
  half-configured clients. The hot path is still O(clients) per odds change: `ProcessForActiveClients`
  issues one `SendAsync` per ready, present client (all serialized through the single `HubConnection`
  lock, each MessagePack-serializing the same payload again), and `ProcessForAllClients` runs the
  market-count/top-fixture/top-league maintenance for every client (one `fixturedetails` call per client
  per new fixture, one cache entry per client per fixture). Presence filtering is the mitigation for the
  publishes; the structural fix is a hub method that takes the payload once with the list of client
  codes (cross-repo: `Platform.Websocket.API2` + OEv2). Also O(clients): one `HttpClient` per client in
  `ClientRegistry`, and the unbounded `BlockingCollection` queues grow without limit if the hub is
  slower than the feed. Per-client-per-message logging in `ProcessMarketCount` was moved to Debug.

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
- **Status:** **moot as stated** — the `sb:translation:*` cache is no longer populated, so the purge has
  nothing to invalidate (no-op). See [TechnicalDebt.md](./TechnicalDebt.md) H7.
- ⚠️ **But the underlying cross-instance concern moved rather than disappeared.**
  `OE.Sportsbook.Service.GateTranslations` keeps a **per-instance** `_publishedTranslations` dictionary,
  so each of the 11 shards independently decides whether a key's `*Tr` are "unchanged". State is not
  shared, is lost on restart, and is cleared wholesale at `MAX_PUBLISHED_TRANSLATION_ENTRIES` (logged as
  "Translation gate reset"). The designed-for outcome is a harmless duplicate re-send, not a miss —
  but it is unbounded-until-reset per-instance state on the hot path, and worth watching.

### R8 🟡 Unverified upstream source (`r.uof.*`)
- **Evidence:** Adapter binds `r.uof.#`/`r.of.recovery.#.tso`; no producer in analyzed repos.
- **Files:** `OE.Adapter.Service` / `OE.UOF.Client` appsettings.
- **Impact:** an undocumented input service; recovery-channel behavior unknown. **Confidence:** Low.
- **Suggested investigation:** grep `oddsfeed`/`oddsengine` for `r.uof.`/`r.of.` publishers. (Unknowns #1/#2)

### R9 🟠 SignalR translation props were **renamed** — OEv2 + platform-web must deploy together (C6)
- **Evidence:** the pre-2026-07 `*NaTr` properties (`SpNaTr/SegNaTr/LeNaTr/FiNaTr/MaNaTr/SeNaTr`) no
  longer exist on `OutgoingOddsChange`. The 2026-07 redesign put translations back under **different
  names**: `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr` (fixture), `MaTeTr` (market), `SeNaTr` (selection), typed
  `List<OutgoingTranslation> {LaCo, Tx}` (`OE.Websocket.Service/Messages/OutgoingOddsChange.cs:44-88`,
  mapped at `Repository/SportsbookRepository.cs:1547-1927`). `OutgoingBoostedOddsChange` carries none.
  SignalR uses MessagePack `ContractlessStandardResolver` (name-keyed — contract C6, see
  [Contracts.md](./Contracts.md)).
- **Impact:** a rename under Contractless is a **silent** break — a platform-web mirror still reading
  `SpNaTr` deserializes to empty rather than failing, so the UI degrades to English `*Na` with no error
  anywhere. `HoTr`/`AwTr` also replace the old per-fixture `FiNaTr`, which is a **shape** change, not
  just a name change. **OEv2 + platform-web must deploy together.** **Confidence:** High (OEv2 side).
- **Second-order risk:** an empty `*Tr` list is ambiguous by design — it means "unchanged since last
  publish" (Sportsbook's delta gate) *or* "not translated yet". A consumer that treats empty as
  "clear the translation" will blank names on every gated message.
- **Suggested investigation:** confirm platform-web reads the new names and treats empty as "keep
  current"; coordinate the joint deploy. See [TechnicalDebt.md](./TechnicalDebt.md) H7.

### R10 🟠 OE client pricing is split across two flags in two repos — enabling one alone breaks betting
- **Evidence:** per-client margins (`ClCoMa`) are applied in two places, each behind its own switch.
  - **OEv2 (display):** `OE.Websocket.Service` reprices odds with `GetClientOdds` only when
    `Custom:IsOEEnabled` is true (`Repository/SportsbookRepository.cs:85-88`, `:107-110`). Repricing
    lowers the price, for example 2.00 becomes 1.82 at a 10% margin. argocd sets
    `Custom__IsOEEnabled: "false"` in `apps/oddsengine/websocket-service/{dev,stg,prd}.yaml:44`, and
    has since each environment was created (verified 2026-10-05).
  - **platform-final (bet acceptance):** `Platform.Sportsbook.API/Repository/SportsbookRepository.cs`
    re-reads the selection from Mongo at bet placement and compares the bet's odds with the raw `Od`
    (`GetBetSelectionStatus`, `:7059-7094`). Its own `ClCoMa` repricing runs only when `IsOEEnable`
    is true, and `IsOEEnable` always returns `false` (`:11758-11767`, comment "disable Margin since
    we are now using bet genius"). The bet is stored at the server's odds, not the client's.
  - platform-web sends the displayed decimal odds plus the bettor's `OddsChangeType` (`None`,
    `Higher`, `Any`) with the bet (`platform-web` `Components/BetSection/BetSection.razor.cs:298-317`).
- **Impact:** today both switches are off, so display and acceptance agree on the raw `Od`. If OEv2's
  `IsOEEnabled` is turned on while platform-final's `IsOEEnable` stays `false`:
  - with `OddsChangeType.None`, every repriced selection fails the exact-match check and the bet is
    rejected with `Odds_Or_Lines_Have_Changed`;
  - with `Higher` or `Any`, the bet is accepted at the higher raw `Od`, so the client margin is shown
    but never charged.
  The reverse (platform-final on, OEv2 off) is also inconsistent: the UI would show raw odds while
  the server compares against repriced ones (assumption: not traced past the `GetClientOdds` call at
  platform-final `:1367`). Neither repo fails at build or startup, so the mismatch shows up only
  as rejected bets or lost margin. **Confidence:** High (both code paths read), Medium (platform-final
  line numbers from a local checkout not fetched on 2026-10-05).
- **Also note:** the platform-final checks inside the `IsOEEnable` branch are dead too, including the
  `ClCo` client-offer filter and forcing pre-match markets inactive once a fixture starts.
- **Suggested investigation:** treat re-enabling OE pricing as one coordinated change across OEv2,
  platform-final and argocd, not a config flip. Decide which side owns the margin: if OEv2 does,
  platform-final must apply the same `ClCoMa` maths before comparing. Confirm with trading whether
  per-client margins are still wanted now that Genius Sports prices are used.

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
