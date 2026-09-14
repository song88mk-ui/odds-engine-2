# Technical Debt

Consolidated, validated debt inventory (Phase 10 format). Each finding: **Evidence/Files**,
**Confidence**, **Suggested investigation**. Per scope rules, **no refactoring is suggested** — only
investigation. Severity 🔴 High / 🟠 Medium / 🟡 Low. Companions: [ArchitectureRisks.md](./ArchitectureRisks.md),
[AntiPatterns.md](./AntiPatterns.md), [Unknowns.md](./Unknowns.md), [KnowledgeValidation.md](./KnowledgeValidation.md).
Phase-1 IDs (H/M/L) and Phase-6 IDs (P*) preserved.

## 🔴 High

| ID | Finding | Evidence / Files | Conf. | Suggested investigation |
|---|---|---|---|---|
| H1 | Live secrets committed (RabbitMQ/BetGenius/SQL/API) | `.gitignore` excludes only `appsettings.Development.json`; real values in `OE.{Adapter,Sportsbook,Job.LiveStream,Job.Settlement,Websocket,UOF.Client}/appsettings.json` | High | confirm validity per env; audit git-history exposure; verify prod External-Secrets override |
| H2 | Websocket unmanaged threads + `Environment.Exit(1)` + `async void` | `OE.Websocket.Service/Worker.cs` (~13 `new Thread()` on `BlockingCollection`, no token/join). **Partly mitigated 2026-09-03 (GM-791):** dispatch threads are now background threads (`CreateDispatchThread`), so a stopped host lets the process exit instead of leaving a Running-but-dead container; the startup hub connect retries with backoff instead of faulting `ExecuteAsync`; `StopAsync` calls `base.StopAsync` so the stopping token fires. Still open: `async void` loops, `Environment.Exit(1)` in the message handler, no join on shutdown | High | repro handler exception; observe shutdown drain + message loss; check prod restart rate |
| H3 | `_IsBetStopGlobally` not thread-safe | `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:48,220,232` (prefetch 1000) | High (code) | trace concurrent `OnBetStopGlobal`; check stop/resume flaps in logs |
| H4 | KeepAlive `IsHealthy` hardcoded true | `OE.Job.KeepAlive/Worker.cs` | High | confirm monitoring doesn't treat heartbeat as health |
| H5 | UOF.Client validation + `MaxFixtures` enforcement disabled (commented) | `OE.UOF.Client/Repository/ClientRepository.cs` (~203-207, ~314-379, ~785-843) + `Worker.cs` | High | confirm entitlement is intended unenforced or pending |
| H6 | Player-props condition always-true (`!= "12" || != "13"`) | `OE.UOF.Client/.../ClientRepository.cs:476-493` | Medium ([Likely] bug) | confirm intended `&&`; validate player-props margin output |
| H7 | **Dead translation types left over from the pre-2026-07 shape** — `TranslationBase` and `TranslationItem` (`OE.Shared/Models`) and the six `Outgoing*Translation` classes (`OutgoingSport/Segment/League/Fixture/Market/SelectionTranslation`) in `OE.Websocket.Service/Messages/OutgoingOddsChange.cs` have **zero references** in the solution. Also dead: Mongo `Fixture.FiNaTr` (superseded by `HoNaTr`/`AwNaTr`) and `Selection.ClBoSeNaTr` (never written). The Sportsbook `sb:translation:*` cache is never populated, so `ProcessCacheInvalidationMessage` (`:340`) is a **no-op** — but backoffice-final still publishes `ON_DYNAMIC_TRANSLATION_CHANGE`, so the handler must stay. | `OE.Shared/Models`, `OE.Websocket.Service/Messages/OutgoingOddsChange.cs:95-127`, `OE.Shared/Data/Sportsbook/MongoDB/Collections/{Fixture,Selection}.cs` | High | delete the 8 unreferenced types; decide whether `FiNaTr`/`ClBoSeNaTr` are dropped from the schema or filled |
| H8 | ~~**`Custom:Translation:CacheBackfill` is on in production by default, not by decision**~~ **RESOLVED 2026-09-14 (GM-784)**: the bridge, the `LegacyTranslationCache` entity, its `TranslationContext` DbSet, the `SourceHash` helper and the config key are all deleted; `TranslationCache2` is the context's only DbSet and nothing in OEv2 reads the legacy table, so it can now be dropped independently. It was not merely a scan-cost problem: the bridge set `PROCESSED = 1` **in SQL only**, which pulled the row off `update.py`'s `PROCESSED = 0` worklist before anything announced it, so the in-memory cache, OE.Adapter and Mongo never learned the translation and names rendered **English** on the sportsbook until a Backoffice re-save or a service restart. Prod was losing **1,700-2,300 targets/day** this way while `update.py` logged `cache_hits=0` on every pass (live 2026-07-06 → 2026-09-14). Rows now go through DeepL and announce normally, roughly doubling daily DeepL volume — the intended trade. | `OE.Translation.Service/appsettings.json`, `Repositories/TranslationRepository.cs`, `OE.Shared/Data/Translation/TranslationContext.cs`, `argocd/apps/oddsengine/translation-service/{stg,prd}.yaml` | High | — |

## 🟠 Medium

| ID | Finding | Evidence / Files | Conf. | Suggested investigation |
|---|---|---|---|---|
| M1 | EF Core 3.1 (EOL) pinned | `OE.Shared.csproj` (netstandard2.1) | High | confirm upgrade constraints from consumers |
| M2 | ~~Mixed TFMs net8/net9 + SDK images~~ **RESOLVED 2026-08-24 (GM-751)**: all executables (services, jobs, tests) unified on `net10.0`; Dockerfiles on `sdk:10.0`/`aspnet:10.0`; CI on SDK `10.0.x`. `OE.Shared` intentionally stays `netstandard2.1` (shared contract) | per-service `.csproj`/Dockerfile | High | — |
| M3 | Mixed package versions — `Microsoft.Extensions.Hosting`/`Http`/SignalR unified at 10.0.11 by GM-751 (2026-08-24); `MongoDB.Driver` 3.4 vs 3.6 split **remains** (deliberately excluded from GM-751 — current code is not compatible with a driver major bump) | various `.csproj` | High | confirm supported `MongoDB.Driver` target before unifying |
| M4 | Silent `catch {}` / `async void` swallow | `OE.Job.Lookup/Worker.cs:81-84`; `OE.UOF.Client/Repository/PlatformRepository.cs:33,43-45` | High | add temp logging to quantify swallowed errors |
| M5 | `OE.Job.Lookup` no Serilog/telemetry | `OE.Job.Lookup/Program.cs` | High | confirm prod logging path for this job |
| M6 | Loops ignore `CancellationToken` → unclean shutdown — **(2026-07-13) Cleanup's delete path now honors the token (`Cleanup(CancellationToken)`); the outer Worker loops remain** | KeepAlive/Cleanup/Settlement/LiveStream `Worker.cs`; Websocket threads | High | observe pod drain on SIGTERM |
| M7 | ~~Cleanup per-document delete loop (not `DeleteMany`)~~ **RESOLVED 2026-07-13**: batched `DeleteMany` (`Filter.In` on `FiId`, `Custom:BatchSize` default 5000, `Custom:BatchDelayMs` default 2000) + per-batch UTC-window re-check. NOTE prod `Custom__Enabled` has been `false` since 2026-02-05 (`argocd/apps/oddsengine/job-cleanup/prd.yaml`) — job never ran in prod; ~80% of prod Sportsbook data was cleanup-eligible on 2026-07-13 | `OE.Job.Cleanup/Repository/SportsbookRepository.cs` | High | enable in prod (argocd) and watch first backlog run |
| M8 | Settlement reads but doesn't write Mongo despite `EditSportsbook` | `OE.Job.Settlement` | Medium | trace who writes the fixture-close |
| M9 | `new TradingContext()` per call | `OE.UOF.Client/Repository/TradingRepository.cs` | High | measure connection/context overhead |
| M10 | Settlement correlation chain broken (`Guid.NewGuid()`) | `SportsbookRepository.cs:165,207,233,278,316,319` | High | confirm downstream doesn't need original correlation |
| M11 | Routing-key typo `r.bg.receiver.onfixturecchange` | `OE.Job.Settlement` (Settlement publish) | Medium | confirm the consumer binds the same string (or it's dead) |
| M12 | Stateful Singleton reset by wall-clock (monthly) | `OE.UOF.Client/Worker.cs` | Medium | confirm reset correctness across month boundary/timezone |
| P1 | Unindexed settlement read (`PrFiId+MaTeId+SeTeId+SeSp`) | `SportsbookRepository.cs:189`; indexes in `MongoIndexBootstrapper.cs` | High | profile query plan/scan cost on Selection |
| P2 | TTL-in-hours truncation — `MemCache`'s `int` TTL overload is in **HOURS**, so any sub-hour value truncates to 0 = immediate expiry. The old translation call site is gone (Adapter preloads instead), but the overload is unchanged and still bites any future caller | `MemCache.cs:10-16` | High | rename or remove the ambiguous int overload |
| P3 | `w=0` unacked writes + unordered bulk → silent failures | conn string `Custom:MongoDB-Sportsbook`; `SportsbookRepository.cs:583` | High | check Mongo server logs for dropped writes |
| P9 | Per-instance state on the translation hot path — the `sb:translation:*` purge is moot (cache never populated, see H7), but `GateTranslations` replaced it with a **per-instance** `_publishedTranslations` dictionary: each of the 11 shards independently decides "unchanged", state is lost on restart, and is cleared wholesale at `MAX_PUBLISHED_TRANSLATION_ENTRIES` (logged "Translation gate reset"). By design the failure mode is a harmless duplicate re-send, but it is unbounded-until-reset per-instance memory on every message | `SportsbookRepository.cs:340` (purge), `GateTranslations` + `_publishedTranslations` | Medium | confirm the entry cap is sized for prod key cardinality; consider metrics on gate resets |

## 🟡 Low

| ID | Finding | Evidence / Files | Conf. | Suggested investigation |
|---|---|---|---|---|
| L1 | Dead project `OE.API` (no code) | `OE.API/` (0 `.cs`, no `.csproj`) | High | confirm removable |
| L2 | `OE.Job.KeepAlive` absent from `.sln` (built by CI) | `OddsEngine.sln`; `ci.yml` | High | confirm intentional |
| L3 | `IdGen` referenced unused | `OE.Sportsbook.Service.csproj`; `SportsbookRepository.cs:46` | High | confirm planned use or removable |
| L4 | Dead status-mapper helpers | `OE.Adapter.Service/Repositories/TradingRepository.cs:95-113` | High | confirm unused |
| L5/P4 | Large commented blocks in `SportsbookRepository` — the commented-out `*NaTr` `.Set()`/`.AddToSetEach` lines (`:594, :610`) are now **load-bearing**: `OE.Translation.Service` owns every `*NaTr` write, and re-enabling them would have Sportsbook's upsert race against it. Other commented fields (`LiId`, `ClMa`, `GeAl`, `IsBo`, `ClBo`, period scores, betradar ids) remain genuinely disabled | `SportsbookRepository.cs:414, :594, :610, :969` | High | replace the commented blocks with the explanatory comment already at `:414`/`:969` so nobody "restores" them |
| L6 | Hardcoded values (sport switch, +2% margin, shard "10", BetGenius `PROD` login) | `ClientRepository.cs`; `Adapter TradingRepository.cs:75`; `BetGeniusRepository.cs:69` | High | confirm intended vs configurable |
| L7 | Inefficient manual Pascal-casing per message | `OE.Job.Lookup/Repository/SportsbookRepository.cs` | High | (perf only) measure if hot |
| L8 | Dockerfile inconsistencies (ENTRYPOINT/CMD, output paths) | per-service `Dockerfile` | High | confirm intended |
| L9 | No build/test gate, no image scan/SBOM in CI | `.github/workflows/ci.yml` | High | confirm security/test requirements |
| L10 | Naming mismatches (repo class vs store; `LoggerExtension.cs`→`LoggerExtensions`) | Adapter/UOF repos; `Extensions/LoggerExtension.cs` | High | confirm naming convention |
| P5 | Dual Mongo client (DI vs self-newed) | `Program.cs:61-68` vs `SportsbookRepository.cs:340` | High | confirm DI client intent |
| P6 | `mtpp` cached 1 year, no refresh | `OE.UOF.Client/Repository/PlatformRepository.cs` | High | confirm config-drift acceptable |
| P7 | O(n) cache prefix purge | `MemCache.cs:79` | High | (perf only) measure at cache scale |
| P8 | Redundant Translation index (`IX_SBD_STRING_LANG` dup of unique) | `TranslationContext.cs:36-41` | High | confirm with SQL owner (schema owned externally) |
| — | No XML doc comments / many "what is this number?" TODOs | repo TODOs (the Websocket `_MaTeIds` example went with GM-811) | High | low priority; missing-doc inventory |
| — | No test projects found in-repo | `find` (none) | High | confirm tests exist elsewhere or are absent |

## Duplicate implementations (validated — see ArchitectureRisks.md)
6× `SportsbookRepository`, 2× `TradingRepository`, 2× `BaseResponseDTO`, 8× `Instrumentation`,
2 copies of the Mongo `Data/Sportsbook` schema (OEv2 + backoffice inline). Confidence: High (in-repo)
/ Medium (backoffice). Suggested investigation: diff the 6 `SportsbookRepository` copies and the two
`BaseResponseDTO`s for intentional divergence vs copy-paste drift.

## Cross-cutting notes
- **Strengths (not debt):** idempotent upsert + sticky-shard ordering; end-to-end trace propagation;
  externalized prod config/secrets; **no circular dependencies**. See [BestPractices.md](./BestPractices.md).
- Many High/Low items are **intentionally-disabled in-progress features** (commented blocks) — confirm
  intent (see [Unknowns.md](./Unknowns.md)) before acting. This doc records existence + investigation
  only; remediation is out of scope (Phase 10).
