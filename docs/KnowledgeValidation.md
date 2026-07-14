# Knowledge Validation

Validation pass over everything documented in Phases 1–9, **before** any Claude-asset generation.
States how each area was verified, the confidence, the assumptions made, contradictions found &
resolved, and coverage gaps. Per Phase-10 rules: findings carry **Evidence / Files / Confidence /
Suggested investigation**; **no refactoring is suggested**.

## Confidence legend
- **High** — read directly in odds-engine-2 source/config (multiple corroborating files).
- **Medium** — verified in a sibling repo ([Verified-sibling]) or strongly implied by code+config.
- **Low** — inferred from naming/partial evidence; flagged [Likely]/[Unknown].

## How each area was validated
| Area | Method | Confidence |
|---|---|---|
| Solution/projects/refs | Read `.sln` + all `.csproj` | High |
| Service runtime behavior | Read every `Program.cs` + `Worker.cs` + repositories | High |
| Messaging spine | Read `MessageBroker.cs` end-to-end | High |
| Persistence | Read `SportsbookRepository`, `TranslationRepository`, contexts, bootstrapper, `MemCache` | High |
| Domain model | Read every collection/entity/enum file | High |
| Config/deploy | Read all appsettings + Dockerfiles + agent digest of argocd/infra | High (committed) / Medium (argocd via agent) |
| Cross-repo (bet-genius/platform-final/-web/backoffice/Auth) | Explore agents on sibling repos + their docs | Medium ([Verified-sibling]) |
| UI (platform-web) | Agent digest of platform-web docs+source | Medium ([Verified-pw]/[Doc-pw]) |
| Git hotspots | `git log` analysis (271 commits) | High |

## Assumptions made (explicit)
| # | Assumption | Basis | If wrong… | Confidence |
|---|---|---|---|---|
| A1 | "UOF" = Unified Odds Feed | outbound `uof.*` keys + sibling `oddsfeed` | terminology only; behavior unaffected | Medium |
| A2 | `r.uof.*` produced by `oddsfeed`/legacy `oddsengine` | Adapter binds it; no producer in analyzed repos | a source service is undocumented | Low |
| A3 | Prod runs 11 Sportsbook shards (00–10) | verified dev in argocd; math produces 00–09 + global 10 | shard count/routing differs in stg/prd | Medium |
| A4 | committed appsettings are dev stubs overridden in prod | argocd env+secrets observed | some service reads committed values in prod | Medium |
| A5 | `/platform` hub = standalone gateway (Platform.Websocket.API2) | Phase 1 finding; hub `.cs` in neither repo | delivery/scaling reasoning shifts | Medium |
| A6 | platform-final/backoffice only READ Mongo | platform doc rule + backoffice secondary-pref reads | a second writer exists | Medium |

## Contradictions found & resolved (validation caught these)
1. **`/platform` hub host.** platform-web's own docs say *both* "hosted within platform-web" and
   "NOT hosted inside platform-web (separate gateway)". **Resolved:** separate gateway
   (`Platform.Websocket.API2`); hub source in neither repo. Recorded [Unknowns.md](./Unknowns.md) #19.
   Evidence: `platform-web/docs/OddsEngineIntegration.md:6` vs `:331`. Confidence: Medium.
2. **Translations "embedded onto Mongo documents."** Phases 1/3 stated this; reading the code showed
   all `*Tr` `.Set()` calls are **commented out** — translations go only into the published message.
   **Resolved & corrected** in DomainModel/EntityLifecycle/RequestLifecycle/SequenceDiagrams + MongoDb.
   Evidence: `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:386-398,492-495,531,552-569`.
   Confidence: High.
   **Follow-up correction (2026-06-30: translations removed from pipeline):** the "translations go only
   into the published message" finding is now itself superseded — the `*Tr` lists (messages) and the
   `*NaTr` wire props (`OutgoingOddsChange`/`OutgoingBoostedOddsChange`) were **removed entirely**, and
   `OE.Sportsbook.Service`'s SQL translation read path (`LoadTranslationsAsync`/`MapTranslations`) was
   deleted (`TranslationRepository` now orphaned). Translations are **no longer anywhere in the OEv2
   pipeline**; the UI localizes via platform-web's REST `GetTranslations()`. Confidence: High.
3. **Translation cache "60-min TTL."** True, but via fragile `(int)TimeSpan.FromMinutes(60).TotalHours`
   = 1h by truncation luck. **Clarified** as a footgun (P2). Evidence: `TranslationRepository.cs:103,123`.
   Confidence: High. **(Corrected 2026-06-30: translations removed from pipeline.)** `TranslationRepository`
   is now orphaned/uncalled, so this cache is no longer populated — the footgun is no longer exercised.
4. **platform-final stack** mislabeled "likely NestJS" in one early agent note; **confirmed .NET**
   (≈39 projects). Confidence: Medium ([Verified-sibling]).

## Structural validations performed
| Claim | Result | Evidence | Confidence |
|---|---|---|---|
| No circular dependencies | **Confirmed** — only `OE.Shared` is referenced; it references nothing in-repo | `grep ProjectReference` all csproj → only OE.Shared; `OE.Shared.csproj` has 0 ProjectReferences | High |
| No worker→worker references | **Confirmed** | same | High |
| Duplicate `SportsbookRepository` | **Confirmed — 6 copies** | `find SportsbookRepository.cs` → 6 services | High |
| Duplicate `TradingRepository` | **Confirmed — 2** (router vs SQL) | Adapter + UOF.Client | High |
| Duplicate `BaseResponseDTO` | **Confirmed — 2** | `OE.Shared/Models/Responses` + `OE.UOF.Client/Models/DTOs` | High |
| Per-service `Instrumentation` | 8 copies (intentional boilerplate) | `find Instrumentation.cs` → 8 | High |

## Coverage gaps (not fully analyzed)
| Gap | Why | Suggested investigation |
|---|---|---|
| `oddsfeed` / legacy `oddsengine` siblings | out of the named reference set | grep for `r.uof.`/`r.of.` producers to close A2/Unknown #1 |
| argocd stg/prd values | agent read dev examples | read `apps/oddsengine/*/stg.yaml`,`prd.yaml` to confirm A3/A4, shard count, Mongo host |
| `/platform` hub source project | not in either repo | locate the gateway repo to close #19/#22 |
| Runtime metrics/perf (actual latencies, scan costs) | static analysis only | profile `bulkwrite.duration`, settlement query (P1), shard distribution in prod |
| Test coverage | **no test projects found in-repo** | confirm tests live elsewhere or are absent |
| backoffice inline `OE.Shared` copy drift | not diffed | diff backoffice `Data/Sportsbook` vs current `OE.Shared` (#24) |

## Documentation completeness
- All 9 worker services + `OE.Shared` documented (purpose, lifecycle, data, config). High.
- Cross-repo seams documented with classification. Medium (sibling-verified).
- **Missing documentation in the code itself:** sparse XML doc comments; many "what is this number?"
  TODOs (e.g. Websocket `_MaTeIds`); no in-repo README beyond a one-liner; no ADRs (the ones in
  these docs are reverse-engineered). Confidence: High (observed absence).

## Net validation verdict
The documented architecture is **well-corroborated for odds-engine-2 itself (High)**; cross-repo and
deployment specifics are **sibling/agent-verified (Medium)** and carry the listed assumptions. Two
substantive inaccuracies were caught and corrected during validation (translations-not-persisted; the
hub-host contradiction). Remaining risk concentrates in the unverified `r.uof.*` source, the
stg/prd argocd specifics, and the absence of tests. Detailed items: [TechnicalDebt.md](./TechnicalDebt.md),
[Unknowns.md](./Unknowns.md), [ArchitectureRisks.md](./ArchitectureRisks.md).
