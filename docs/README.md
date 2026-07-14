# odds-engine-2 — Documentation Index

> **Status:** Phase 1 — Repository Discovery & Knowledge Acquisition (analysis only).
> **Generated:** 2026-06-25 by automated codebase analysis.
> **Scope:** This repository (`odds-engine-2`) is the source of truth. Sibling repositories
> (`bet-genius`, `platform-final`, `platform-web`, `argocd`, `infrastructure`) were inspected
> **one level up** only to understand integration contracts — never modified.

## Evidence & Confidence Conventions

Every non-trivial claim in these documents is backed by a file reference
(`Project/File.cs:line`). Conclusions are tagged:

- **[Verified]** — read directly in this repository's source.
- **[Verified-X]** — read directly in sibling repository `X`.
- **[Likely]** — strongly implied by code/config but not 100% confirmed.
- **[Unknown]** — could not be verified; see [Unknowns.md](./Unknowns.md).

Do **not** treat [Likely] or [Unknown] items as fact when implementing.

## Document Map

| Document | Purpose |
|---|---|
| [Architecture.md](./Architecture.md) | Overall architecture style, message pipeline, runtime topology |
| [RepositoryMap.md](./RepositoryMap.md) | The workspace: this repo + siblings, and how they relate |
| [ProjectOverview.md](./ProjectOverview.md) | Per-project purpose, responsibilities, consumers |
| [SolutionStructure.md](./SolutionStructure.md) | Solution layout, folders, projects in/out of the .sln |
| [DependencyGraph.md](./DependencyGraph.md) | Project references, NuGet packages, message-flow dependencies |
| [StartupProjects.md](./StartupProjects.md) | Entry points, hosting model, DI registration per service |
| [ConfigurationOverview.md](./ConfigurationOverview.md) | Consolidated config matrix, secrets, env wiring |
| [SharedLibraries.md](./SharedLibraries.md) | OE.Shared catalog: contracts, DB models, enums, the field glossary |
| [ImplementationPatterns.md](./ImplementationPatterns.md) | DI, repository, messaging, caching, telemetry patterns in use |
| [CodingStandards.md](./CodingStandards.md) | Conventions observed (naming, nullability, structure) |
| [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md) | Integration contracts with sibling repos, classified |
| [KnowledgeBase.md](./KnowledgeBase.md) | Reusable patterns, glossary, gotchas, FAQ |
| [ArchitectureDecisions.md](./ArchitectureDecisions.md) | Inferred design decisions and their rationale |
| [TechnicalDebt.md](./TechnicalDebt.md) | Catalogued debt, risks, dead code, severity-ranked |
| [Unknowns.md](./Unknowns.md) | Everything that needs human verification |

### Phase 2 — Startup & Runtime (worker services)

> Phase 2's template is web-centric ("Platform-Web application"). odds-engine-2 has **no web app**
> — it is headless workers — so middleware/auth/authz docs explain the absence and document the
> **worker analogue** instead. (The actual web app, platform-web, is already documented in
> `platform-web/docs/`.)

| Document | Purpose |
|---|---|
| [StartupFlow.md](./StartupFlow.md) | Generic Host startup sequence, init order, dependencies, failure/shutdown |
| [DependencyInjection.md](./DependencyInjection.md) | Registrations, lifetimes, factory/options patterns |
| [Hosting.md](./Hosting.md) | Process/container/k8s hosting, scaling, sharding, health gaps |
| [Configuration.md](./Configuration.md) | Provider hierarchy, secrets, user secrets, feature flags |
| [Logging.md](./Logging.md) | Serilog + OpenTelemetry, trace-based correlation, sinks, metrics |
| [MiddlewarePipeline.md](./MiddlewarePipeline.md) | *No HTTP pipeline* — the message consume→dispatch→ack analogue |
| [Authentication.md](./Authentication.md) | *No inbound auth* — outbound credentials + BetGenius token flow |
| [Authorization.md](./Authorization.md) | *No authz layer* — trust model + business gates |

### Phase 3 — Sportsbook Domain (business knowledge)

> odds-engine-2 is the **catalogue authority** (fixtures/markets/selections/odds in MongoDB). It does
> not own bets/wallets/cashout transactions — those are platform-final. Cross-repo ownership is
> marked throughout.

| Document | Purpose |
|---|---|
| [DomainModel.md](./DomainModel.md) | Every entity: purpose, identity, source of truth, where created/updated/consumed/rendered |
| [Relationships.md](./Relationships.md) | Cardinalities, ERD, templates, participants, mappings, cross-repo seams |
| [EntityLifecycle.md](./EntityLifecycle.md) | State machines: Fixture, Market, Selection, Settlement/Bet, bet-stop, cashout, translation |
| [BusinessRules.md](./BusinessRules.md) | Encoded rules (BR-1…BR-22) with evidence; disabled rules flagged |
| [BusinessGlossary.md](./BusinessGlossary.md) | Concept-level domain vocabulary |

### Phase 4 — End-to-End Data Flow (cross-repo)

> Phase 4 traces complete paths browser → Blazor → Platform API → OddsEngine → back, written from
> odds-engine-2's vantage and cross-referencing platform-web's own UI-side docs. The UI half is
> tagged [Vpw]/[Vpf] (verified in sibling repos).

| Document | Purpose |
|---|---|
| [SequenceDiagrams.md](./SequenceDiagrams.md) | Diagrams for odds update, initial load, navigation, bet→settlement, cashout, translation, auth, reconnect |
| [RequestLifecycle.md](./RequestLifecycle.md) | Narrative end-to-end lifecycle for each flow (push + read paths) |
| [StateFlow.md](./StateFlow.md) | The state ladder (Mongo truth → caches → circuit → DOM); what persists vs resets |
| [RenderingPipeline.md](./RenderingPipeline.md) | How an OEv2 event becomes pixels; render gating; OEv2's influence on render volume |
| [ComponentDataFlow.md](./ComponentDataFlow.md) | OEv2 output → hub event → handler → state → component map |

### Phase 5 — Cross-Repository Integration

> Integration with every sibling. **CrossRepositoryDependencies.md supersedes its Phase 1 version**
> (now includes verified backoffice-final + Auth analysis). External behavior is classified
> [Verified] / [Verified-sibling:X] / [Historical:Reference] / [Unknown] — never inferred.

| Document | Purpose |
|---|---|
| [OddsEngineIntegration.md](./OddsEngineIntegration.md) | OEv2's integration **boundary spec** — all inbound/outbound + the backoffice config seam |
| [Contracts.md](./Contracts.md) | Catalog of every shared contract (C1–C11) + additive-vs-breaking governance |
| [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md) | Master per-repo matrix (bet-genius, platform-final/-web, backoffice-final, Auth, argocd, infra) |
| [PlatformApi.md](./PlatformApi.md) | REST seam: what OEv2 calls + the read surface over OEv2 data |
| [WebSocket.md](./WebSocket.md) | SignalR `/platform` hub: methods, events, groups, MessagePack contract |
| [AuthenticationIntegration.md](./AuthenticationIntegration.md) | Cross-repo auth (Auth issuer, operator token, OGAuth) + OEv2's outbound creds |
| [ExternalSystems.md](./ExternalSystems.md) | Third parties: Genius Sports, AWS, Tempo, Cloudflare, Azure SignalR, package feeds |

### Phase 6 — Persistence & Caching

> MongoDB is the catalogue system of record (OEv2 writes; `w=0`). SQL Server (EF 3.1) is read-only
> reference data (translations + client config). Caching is in-process MemoryCache only — no Redis,
> no browser storage (headless).

| Document | Purpose |
|---|---|
| [MongoDb.md](./MongoDb.md) | Collections, indexes, connection, read/write patterns, BulkWrite, the unindexed-settlement + translations-not-persisted findings |
| [Database.md](./Database.md) | SQL Server: Translation + Trading contexts, tables, keys, indexes, EF read patterns |
| [Caching.md](./Caching.md) | MemCache (MemoryCache.Default) API, what's cached, TTL-in-hours footgun, scope |
| [CacheInvalidation.md](./CacheInvalidation.md) | Translation purge path, negative caching, TTL-only caches, cross-instance delivery |
| [PersistencePatterns.md](./PersistencePatterns.md) | Upsert/idempotency, Set vs SetOnInsert, read-modify-publish, no-transactions, w=0 |
| [RepositoryPatterns.md](./RepositoryPatterns.md) | Repository conventions, per-service inventory, naming caveats |

### Phase 7 — UI Architecture (platform-web summaries)

> **odds-engine-2 has no UI.** The Blazor app is **platform-web** (already documented in
> `platform-web/docs/`). These docs are faithful **cross-repo summaries** of platform-web's Blazor
> architecture (tagged [Verified-pw]/[Doc-pw], never inferred) so this onboarding is end-to-end
> complete. To modify the UI, work in platform-web and use its own docs as source of truth.

| Document | Purpose |
|---|---|
| [UIArchitecture.md](./UIArchitecture.md) | platform-web Blazor Server overview, layers, AL/EL2 skins, theming |
| [Navigation.md](./Navigation.md) | Router, route sets, query-param-driven navigation, scopes, hub-group link |
| [ComponentHierarchy.md](./ComponentHierarchy.md) | AL/EL2 component trees, base classes, the data-bearing spine |
| [SharedComponents.md](./SharedComponents.md) | Layouts, BetSection, Toast/Modal services, UI primitives |
| [StateManagement.md](./StateManagement.md) | `BaseState<T>`/INotifyPropertyChanged, lifetimes, how OEv2 pushes enter UI state |
| [Rendering.md](./Rendering.md) | Lifecycle hooks, ShouldRender/Counter gating, CSS/SCSS (isolation not used) |
| [JavaScriptInterop.md](./JavaScriptInterop.md) | IJSRuntime calls, DotNetObjectReference callbacks, parent↔child postMessage bridge |

### Phase 8 — Implementation Patterns (how this team writes code)

> Descriptive catalog of existing conventions (not recommendations). **ImplementationPatterns.md and
> CodingStandards.md supersede their Phase 1 versions.**

| Document | Purpose |
|---|---|
| [ImplementationPatterns.md](./ImplementationPatterns.md) | Repository/Service/Factory/Builder/Extensions/Validators/Mappers/Helpers/BackgroundServices/Events/Logging/Exceptions/FeatureFlags as used here |
| [NamingConventions.md](./NamingConventions.md) | Projects, types, messages, methods, routing keys, the abbreviated field codes |
| [CodingStandards.md](./CodingStandards.md) | .editorconfig, layout, language/DI/messaging/persistence/logging standards |
| [BestPractices.md](./BestPractices.md) | The conventions the team does well (idempotency, tracing, sharding, externalized config) |
| [AntiPatterns.md](./AntiPatterns.md) | Recurring anti-patterns present in the code (factual; cross-refs TechnicalDebt) |

### Phase 9 — Repository Intelligence (quick-reference knowledge base, `knowledge/`)

> Lookup-oriented reference distilled from the deep docs, plus git-history hotspots and a
> troubleshooting guide.

| Document | Purpose |
|---|---|
| [knowledge/CommonServices.md](./knowledge/CommonServices.md) | "What do I use for X" — broker, cache, helpers, providers, repos, telemetry |
| [knowledge/CommonDtos.md](./knowledge/CommonDtos.md) | Shared DTO catalog (envelope, messages, SignalR, UOF, BetGenius) |
| [knowledge/CommonModels.md](./knowledge/CommonModels.md) | Shared models, collection/SQL entities, enum value tables |
| [knowledge/ConfigurationKeys.md](./knowledge/ConfigurationKeys.md) | Complete config-key reference + secret list + argocd env equivalents |
| [knowledge/PlatformApiEndpoints.md](./knowledge/PlatformApiEndpoints.md) | REST endpoints OEv2 calls + the read surface over OEv2 data |
| [knowledge/WebSocketMessages.md](./knowledge/WebSocketMessages.md) | Hub methods, events, groups, model fields, gotchas |
| [knowledge/Utilities.md](./knowledge/Utilities.md) | Utility classes + extension methods with signatures |
| [knowledge/Troubleshooting.md](./knowledge/Troubleshooting.md) | Symptom → cause → where-to-look incident guide |
| [knowledge/FrequentlyModifiedAreas.md](./knowledge/FrequentlyModifiedAreas.md) | Git-history hotspots + high-blast-radius files |

### Phase 10 — Repository Validation & Technical Debt

> Validation pass before any asset generation. Findings carry Evidence / Files / Confidence /
> Suggested investigation. **No refactoring suggested** (out of scope). TechnicalDebt.md and
> Unknowns.md were rewritten into this format and supersede their earlier versions.

| Document | Purpose |
|---|---|
| [KnowledgeValidation.md](./KnowledgeValidation.md) | How each area was verified, assumptions, contradictions found & resolved, coverage gaps, confidence scorecard |
| [TechnicalDebt.md](./TechnicalDebt.md) | Consolidated debt (H/M/L + P*) with evidence, confidence, investigation; validated duplicates |
| [Unknowns.md](./Unknowns.md) | 30 open questions with evidence, best-guess+confidence, how to close |
| [ArchitectureRisks.md](./ArchitectureRisks.md) | Risk register (R1–R8), deviations (V1–V5), dependency analysis (no cycles), duplicates, heatmap |

## One-paragraph summary

`odds-engine-2` (a.k.a. **OEv2**) is an **event-driven pipeline of independent .NET worker
services** that ingest normalized odds/fixture/settlement messages from a RabbitMQ topic
exchange (`e.central`), enrich and persist them into a MongoDB **Sportsbook** catalogue
(with SQL Server for translations and client trading config), and fan the results out
(a) downstream into the platform via a SignalR push bridge and settlement messages, and
(b) externally as a Unified Odds Feed for white-label clients. The upstream message source
is the sibling `bet-genius` repository (Genius Sports feed integration); the downstream
consumers are `platform-final` (backend) and `platform-web` (Blazor UI).
