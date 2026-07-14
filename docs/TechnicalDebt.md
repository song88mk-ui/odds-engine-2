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
| H2 | Websocket unmanaged threads + `Environment.Exit(1)` + `async void` | `OE.Websocket.Service/Worker.cs` (~13 `new Thread()` on `BlockingCollection`, no token/join) | High | repro handler exception; observe shutdown drain + message loss; check prod restart rate |
| H3 | `_IsBetStopGlobally` not thread-safe | `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:48,220,232` (prefetch 1000) | High (code) | trace concurrent `OnBetStopGlobal`; check stop/resume flaps in logs |
| H4 | KeepAlive `IsHealthy` hardcoded true | `OE.Job.KeepAlive/Worker.cs` | High | confirm monitoring doesn't treat heartbeat as health |
| H5 | UOF.Client validation + `MaxFixtures` enforcement disabled (commented) | `OE.UOF.Client/Repository/ClientRepository.cs` (~203-207, ~314-379, ~785-843) + `Worker.cs` | High | confirm entitlement is intended unenforced or pending |
| H6 | Player-props condition always-true (`!= "12" || != "13"`) | `OE.UOF.Client/.../ClientRepository.cs:476-493` | Medium ([Likely] bug) | confirm intended `&&`; validate player-props margin output |
| H7 | **Orphaned/dead translation code retained after pipeline removal (2026-06-30)** — `TranslationRepository`/`ITranslationRepository`, `TranslationContext` (`AddDbContextPool`), `TranslationBase`/`TranslationItem`, `Outgoing*Translation` helper types, plus DI registrations in Sportsbook `Program.cs` all remain but are **never resolved/called**. Now-unused config: `ConnectionStrings:Translation`, `Custom:Translation:UseContentKey`. `OnDynamicTranslationChange` invalidation handler is now a **no-op** (cache never populated). | Sportsbook `Program.cs` (DI), `SportsbookRepository.cs` (call sites deleted), `OE.Shared/Models` (`TranslationBase`/`TranslationItem`), Websocket `Outgoing*Translation` | High | confirm intentional dead-code retention; decide removal of repo/context/config; verify no other service still reads `Translation` |

## 🟠 Medium

| ID | Finding | Evidence / Files | Conf. | Suggested investigation |
|---|---|---|---|---|
| M1 | EF Core 3.1 (EOL) pinned | `OE.Shared.csproj` (netstandard2.1) | High | confirm upgrade constraints from consumers |
| M2 | Mixed TFMs net8/net9 + SDK images | per-service `.csproj`/Dockerfile | High | confirm intended target baseline |
| M3 | Mixed package versions (`Microsoft.Extensions.Hosting`, `MongoDB.Driver` 3.4 vs 3.6) | various `.csproj` | High | confirm supported versions |
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
| P2 | TTL-in-hours truncation (`(int)…TotalHours`) — **(2026-06-30) `TranslationRepository` no longer called (see H7), so this is no longer exercised; the `MemCache` int-overload footgun still applies to any future caller** | `TranslationRepository.cs:103,123`; `MemCache.cs:10-16` | High | confirm no sub-hour TTL is ever passed (now dormant) |
| P3 | `w=0` unacked writes + unordered bulk → silent failures | conn string `Custom:MongoDB-Sportsbook`; `SportsbookRepository.cs:583` | High | check Mongo server logs for dropped writes |
| P9 | Per-instance cache invalidation delivery to all shards — **(2026-06-30) now moot: the `sb:translation:*` cache is no longer populated, so `OnDynamicTranslationChange` purge is a no-op (handler retained); see H7** | `SportsbookRepository.cs:323`; Sportsbook `appsettings.json` RoutingKey | Medium | dormant — re-open only if a cached read path returns |

## 🟡 Low

| ID | Finding | Evidence / Files | Conf. | Suggested investigation |
|---|---|---|---|---|
| L1 | Dead project `OE.API` (no code) | `OE.API/` (0 `.cs`, no `.csproj`) | High | confirm removable |
| L2 | `OE.Job.KeepAlive` absent from `.sln` (built by CI) | `OddsEngine.sln`; `ci.yml` | High | confirm intentional |
| L3 | `IdGen` referenced unused | `OE.Sportsbook.Service.csproj`; `SportsbookRepository.cs:46` | High | confirm planned use or removable |
| L4 | Dead status-mapper helpers | `OE.Adapter.Service/Repositories/TradingRepository.cs:95-113` | High | confirm unused |
| L5/P4 | Large commented blocks; translations/fields never persisted to Mongo (the `*NaTr` Mongo fields still exist but are unwritten — **unchanged**; as of 2026-06-30 translations are also no longer in any message, see H7) | `SportsbookRepository.cs:386-398,492-495,531,552-569` etc. | High | confirm disabled-feature intent before relying on doc fields |
| L6 | Hardcoded values (sport switch, +2% margin, shard "10", BetGenius `PROD` login) | `ClientRepository.cs`; `Adapter TradingRepository.cs:75`; `BetGeniusRepository.cs:69` | High | confirm intended vs configurable |
| L7 | Inefficient manual Pascal-casing per message | `OE.Job.Lookup/Repository/SportsbookRepository.cs` | High | (perf only) measure if hot |
| L8 | Dockerfile inconsistencies (ENTRYPOINT/CMD, output paths) | per-service `Dockerfile` | High | confirm intended |
| L9 | No build/test gate, no image scan/SBOM in CI | `.github/workflows/ci.yml` | High | confirm security/test requirements |
| L10 | Naming mismatches (repo class vs store; `LoggerExtension.cs`→`LoggerExtensions`) | Adapter/UOF repos; `Extensions/LoggerExtension.cs` | High | confirm naming convention |
| P5 | Dual Mongo client (DI vs self-newed) | `Program.cs:61-68` vs `SportsbookRepository.cs:340` | High | confirm DI client intent |
| P6 | `mtpp` cached 1 year, no refresh | `OE.UOF.Client/Repository/PlatformRepository.cs` | High | confirm config-drift acceptable |
| P7 | O(n) cache prefix purge | `MemCache.cs:79` | High | (perf only) measure at cache scale |
| P8 | Redundant Translation index (`IX_SBD_STRING_LANG` dup of unique) | `TranslationContext.cs:36-41` | High | confirm with SQL owner (schema owned externally) |
| — | No XML doc comments / many "what is this number?" TODOs | e.g. Websocket `_MaTeIds`; repo TODOs | High | low priority; missing-doc inventory |
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
