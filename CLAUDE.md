# CLAUDE.md — odds-engine-2 (OEv2)

> ## ⚠️ FIRST INSTRUCTION (applies to every prompt)
> Before responding to **any** request:
> 1. **Consult this file (CLAUDE.md) and [AGENTS.md](AGENTS.md)** and follow the mandatory 13-step
>    Standard Workflow below.
> 2. **Trace claims in the actual code and cite `file:line` — do not answer from memory.**
> 3. **Label every claim `verified` / `assumption` / `unknown`; never guess.**
> 4. **Explain the plan and wait for approval before any architectural change; make the smallest
>    possible change.**
> 5. **Production is READ-ONLY** — never run queries, deploy, change schema, or invalidate caches
>    unless explicitly instructed.
> 6. **If architecture changed, update `/docs` + CLAUDE.md/AGENTS.md and summarize changes, risks,
>    and validation.**
>
> A `UserPromptSubmit` hook in [.claude/settings.json](.claude/settings.json) re-injects this reminder
> on every prompt. Full workflow + principles are in the section below.

Guidance for Claude when working in this repository. Derived from the codebase + the `/docs`
knowledge base (Phases 1–10). **Read the relevant `/docs` file before acting** — this file is the
index + the rules that matter most.

## What this repo is
An **event-driven pipeline of 9 headless .NET worker services** that ingest normalized odds/fixture/
settlement messages from RabbitMQ (`e.central`), persist a MongoDB **Sportsbook** catalogue (SQL
Server for translations + client config), and fan results out to (a) the platform via a SignalR push
bridge + settlement messages and (b) an external white-label Unified Odds Feed.
**There is no UI and no inbound HTTP here** — the Blazor app is the sibling `platform-web`.

- Source: `src/OddsEngine/` (solution `OddsEngine.sln`). Services are `OE.*` projects; shared kernel
  is `OE.Shared` (every service references only it — no worker→worker deps, no cycles).
- Full map: [docs/README.md](docs/README.md) → [docs/Architecture.md](docs/Architecture.md),
  [docs/ProjectOverview.md](docs/ProjectOverview.md).

## Tech stack (verified from `src/OddsEngine/**/*.csproj`, 2026-09-10)
| Concern | What |
|---|---|
| Runtime | `net10.0` for every service and job |
| Shared kernel | `OE.Shared` targets **`netstandard2.1`** — new shared code must stay inside that API surface |
| Host model | `Microsoft.NET.Sdk.Worker` (Websocket, UOF.Client, Job.*) and `Microsoft.NET.Sdk` + `OutputType=Exe` (Adapter, Sportsbook, Translation) |
| Mongo | `MongoDB.Driver` 3.11.1 |
| Broker | `RabbitMQ.Client` 5.2.0 — raw client behind `IMessageBroker`; **no Wolverine/MassTransit** |
| SQL | `Microsoft.EntityFrameworkCore(.SqlServer)` 3.1.32 |
| Logging | `Serilog` 4.3.0 + `Serilog.Formatting.Compact` + `Serilog.Sinks.Console` |
| Push | `Microsoft.AspNetCore.SignalR.Client` + `.Protocols.MessagePack` |
| Cache | `System.Runtime.Caching` (`MemoryCache.Default`) — no Redis |
| Tests | `xunit` **2.5.3** (not v3) + `coverlet.collector`; no Testcontainers |
| Language | `<Nullable>enable</Nullable>`, no `LangVersion` pin; `.editorconfig` mandates **block-scoped** namespaces (`csharp_style_namespace_declarations = block_scoped`) — do not convert files to file-scoped |

Note: EF Core 3.1.32 is an out-of-support package running on a `net10.0` target. Recorded as an
observation, not a change request — upgrading it is a cross-repo decision.

## The data pipeline (memorize this)
```
bet-genius / (oddsfeed?) ─► OE.Adapter.Service ─► r.oe.adapter.<type>.<NN>
   ├─► OE.Sportsbook.Service(00–10) ─► MongoDB + r.oe.sb.engine.* + r.oe.bet.settlement
   ├─► OE.Job.Lookup ─► *Lookup collections
   └─► OE.UOF.Client ─► external broker (vhost ext, uof.*)
OE.Websocket.Service ◄─ r.oe.sb.engine.* / r.bet.* / r.wallet.* ─► /platform SignalR hub ─► platform-web
```
Jobs: LiveStream (BetGenius video, 60s), Settlement (close stale, 5m), Cleanup (purge, 5m window),
KeepAlive (heartbeat, 5s).

## Non-negotiable facts (get these wrong and you break prod)
1. **Sharding:** every fixture's events route to a fixed Sportsbook instance via
   `Helper.GetInstanceName(providerFixtureId)` (last 2 digits → `00`–`09`); shard **`10` = global
   bet-stop**. This couples `OE.Adapter.Service` (router) + `OE.Sportsbook.Service` (binding) +
   argocd instance count — change all together. ([docs/Architecture.md](docs/Architecture.md) ADR-003)
2. **Committed `appsettings.json` ≠ production config.** Prod = argocd env vars (`__`) + AWS Secrets
   Manager. To know real config, read `argocd/apps/oddsengine/<service>/<env>.yaml` — this path
   lives in a **separate GitOps repository**, not in this checkout.
   ([docs/Configuration.md](docs/Configuration.md))
3. **`OE.Shared` is a shared contract** with bet-genius + backoffice-final (+ platform readers).
   Editing message DTOs, the Mongo schema, enums, or routing keys is a **cross-repo breaking change**.
   ([docs/Contracts.md](docs/Contracts.md), [docs/CrossRepositoryDependencies.md](docs/CrossRepositoryDependencies.md))
4. **SignalR uses MessagePack `ContractlessStandardResolver`** (name-keyed) — renaming a property on
   any `Outgoing*`/`EngineMessages` model is a breaking wire change; deploy OEv2 + platform-web
   together. ([docs/WebSocket.md](docs/WebSocket.md))
5. **Abbreviated field names everywhere** (`FiId`, `MaStId`, `Od`, `PrFiId`, …) — use the glossary in
   [docs/SharedLibraries.md](docs/SharedLibraries.md). New persisted/wire fields must follow it.
6. **MongoDB writes use `w=0`** (unacknowledged) — write failures are silent; check Mongo logs.

## Conventions to follow (don't introduce new patterns)
- Per-service skeleton: `Program.cs` (Generic Host + DI), `Worker.cs` (BackgroundService → dispatch
  on `BasicProperties.Type`), `Repository|Repositories/` (`IXxx`+`Xxx`, Singleton), `Telemetry/
  Instrumentation.cs`, `appsettings.json`, `Dockerfile`.
- Persistence: idempotent upsert via `BulkWrite<T>` (`UpdateOneModel`, `IsOrdered=false`), natural
  keys with `SetOnInsert`, mutable fields with `Set`. No transactions/unit-of-work.
- Messaging: `IMessageBroker.Publish/CPublish` (sets `DeliveryMode=2`, `Type`, `CorrelationId`,
  OTel headers). Topic exchange `e.central`, lowercase dotted routing keys.
- Caching: in-process `MemCache` (`MemoryCache.Default`); **TTL `int` overload is in HOURS**;
  cache-aside + negative caching. No Redis.
- Logging: Serilog (Compact JSON) + `LogInformationEx/LogErrorEx/LogWarningEx` extensions;
  OpenTelemetry traces propagated through message headers.
- Config: imperative `IConfiguration` (`cfg["A:B"]`, `GetValue<T>`) — **no Options pattern**.
- No AutoMapper (manual `To*`/`Prepare*`/`Map*`), no MediatR, no FluentValidation (guard clauses).
- Full pattern catalog: [docs/ImplementationPatterns.md](docs/ImplementationPatterns.md);
  naming: [docs/NamingConventions.md](docs/NamingConventions.md).

## Where things live (quick map)
| You need | Go to |
|---|---|
| Decode an abbreviated field | [docs/SharedLibraries.md](docs/SharedLibraries.md) |
| Which service owns a routing key | [docs/ConfigurationOverview.md](docs/ConfigurationOverview.md) |
| Sharding / messaging spine | `OE.Shared/Utilities/{Helper,MessageBroker}.cs` |
| Catalogue writes (the hot file) | `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs` |
| Cross-repo contracts | [docs/Contracts.md](docs/Contracts.md), [docs/OddsEngineIntegration.md](docs/OddsEngineIntegration.md) |
| Known issues / risks / unknowns | [docs/TechnicalDebt.md](docs/TechnicalDebt.md), [docs/ArchitectureRisks.md](docs/ArchitectureRisks.md), [docs/Unknowns.md](docs/Unknowns.md) |
| Symptom → cause | [docs/knowledge/Troubleshooting.md](docs/knowledge/Troubleshooting.md) |
| What changes most | [docs/knowledge/FrequentlyModifiedAreas.md](docs/knowledge/FrequentlyModifiedAreas.md) |
| Test inventory, mutation scores, coverage gaps | [docs/Testing.md](docs/Testing.md) |

## Build / run
- Build: `dotnet build src/OddsEngine/OddsEngine.sln` (note: `OE.Job.KeepAlive` is NOT in the .sln but
  is built by CI; `OE.API` is dead — no code).
- Local run: `dotnet run` from a service folder; needs local RabbitMQ + MongoDB (+ SQL for
  Sportsbook/UOF). Uses committed `appsettings.json` + git-ignored `appsettings.Development.json`.
- `src/OddsEngine/.editorconfig` applies to the whole solution and suppresses `CS8601`
  (nullable-reference assignment warning) — don't be surprised it's silent on that class of bug.
- Test: `dotnet test src/OddsEngine/OddsEngine.sln` (or a single `-Tests`/`-Test` project).
- Mutation testing: `dotnet stryker --project <Project>.csproj` from the matching test-project
  folder (global tool, config in `src/OddsEngine/stryker-config.json`, no CI gate). Scores and
  gaps: [docs/Testing.md](docs/Testing.md). A green suite is not evidence a bug would be caught.
- CI builds each changed service to ECR; argocd deploys. ([docs/Hosting.md](docs/Hosting.md))

## Roslyn MCP navigator (token-cheap code search)
When the `cwm-roslyn-navigator` tools are available, prefer them over `grep` for symbol work:
- `find_symbol` — locate a type/member; the right tool when 6 `SportsbookRepository` copies exist.
- `find_references` / `find_callers` — run before changing any signature, DTO field, or enum
  (see non-negotiable fact 3: `OE.Shared` edits are cross-repo breaking changes).
- `find_implementations` — interface to concrete for the `IXxx` + `Xxx` singleton pairs.
- `get_project_graph` — verify the "every service references only `OE.Shared`, no cycles" rule.
- `get_diagnostics` — after edits, alongside `dotnet build`.

No `.mcp.json` is committed; registration is per-developer
(`claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator --solution <path>`).
Fall back to `grep`/`/find-*` commands when the server is not registered.

## MANDATORY development workflow (Phase 12 — applies to EVERY task)

Follow this Standard Workflow for every task, in order. Do not skip steps; if a step is N/A, say so.

1. **Understand the requirement** — restate it in one sentence.
2. **Search the repository** for existing implementations (`/find-service`, `/find-pattern`; 6
   `SportsbookRepository` copies exist — match the right one).
3. **Consult the generated documentation** (`/docs`, [docs/README.md](docs/README.md)).
4. **Trace the feature end-to-end** (`/trace-*` commands) before changing behavior.
5. **Identify reusable components** (`OE.Shared` contracts/helpers; the correct repo) — reuse, don't duplicate.
6. **Explain the proposed solution** before implementing.
7. **Identify risks and assumptions** (cross-ref TechnicalDebt/ArchitectureRisks/Unknowns).
8. **Ask clarifying questions** when the requirement or evidence is unclear.
9. **Wait for approval before architectural changes** (new patterns, contract changes, new services,
   sharding changes).
10. **Implement the smallest possible change.**
11. **Validate compilation and behavior** (`dotnet build src/OddsEngine/OddsEngine.sln`; state how
    behavior was checked. Ten test projects exist, all in `OddsEngine.sln` — `OE.Shared.Test`,
    `OE.Adapter.Service.Tests`, `OE.Sportsbook.Service.Tests`, `OE.Translation.Service.Tests`,
    `OE.Translation.Job.Tests`, `OE.Websocket.Service.Tests`, `OE.Job.KeepAlive.Tests`,
    `OE.Job.Lookup.Tests`, `OE.Job.Settlement.Tests`, `OE.Job.Cleanup.Tests` — run `dotnet test`
    against the relevant project(s) when present; `OE.Job.LiveStream` and `OE.UOF.Client` have none.
    `OE.Job.KeepAlive.Tests` is in the .sln even though `OE.Job.KeepAlive` itself is not).
12. **Update documentation** if architectural knowledge changed (`/update-docs`; keep CLAUDE.md /
    AGENTS.md / commands / skills / playbooks in sync). **Also update `README.md` in the same
    change whenever the change affects anything the README documents** (projects/frameworks,
    prerequisites, build/run/test instructions, configuration, endpoints) — the README must never
    describe a repo state that no longer exists.
13. **Summarize changes, risks, and validation results.**

Playbook: [.claude/checklists/standard-workflow.md](.claude/checklists/standard-workflow.md).

## Permanent Engineering Principles (non-negotiable)
- **The repository is the source of truth.**
- **Preserve existing architecture.**
- **Never introduce new patterns/libraries without approval.**
- **Never guess when evidence is unavailable.**
- **Clearly distinguish verified facts, assumptions, and unknowns** (tag everything).
- **Production is READ-ONLY by default** — never publish, deploy, change schemas, invalidate caches,
  or run admin actions unless explicitly instructed.
- **Every SQL script must be idempotent** (data fixes, seeds, DDL, hand-run/flyway migrations):
  guard INSERTs with `NOT EXISTS`/`MERGE`, schema changes with `IF NOT EXISTS`/`OBJECT_ID` checks,
  so re-running is a no-op with the same end state.
- **For external repositories, identify dependencies — never invent their behavior**
  ([docs/CrossRepositoryDependencies.md](docs/CrossRepositoryDependencies.md)).
- **Keep documentation, CLAUDE.md, AGENTS.md, commands, skills, and playbooks synchronized** with
  architectural changes.

## Gotchas (top of the list)
- `_IsBetStopGlobally` is not thread-safe (H3).
- Websocket spawns unmanaged threads + `Environment.Exit(1)` (H2).
- UOF entitlement validation is disabled (H5).
- See [docs/TechnicalDebt.md](docs/TechnicalDebt.md) for the full register.

### ⚠️ Translations are back in the pipeline
**Dead — the old SQL path:** `OE.Sportsbook.Service`'s
`TranslationRepository`/`TranslationContext` are gone, and nothing writes the `sb:translation:*`
MemCache any more. One live remnant: `ProcessCacheInvalidationMessage` still deletes by that prefix on
`ON_DELETE_CACHE` (`OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:340`
`ProcessCacheInvalidationMessage`), so that handler now purges a namespace nothing populates.

**Live — the current path:** `OE.Translation.Service` owns a message-driven path moved in
from `OE.Sportsbook.Service` (see the "moved from OE.Sportsbook.Service" comment at
`OE.Translation.Service/Repositories/MongoTranslationRepository.cs:682`). It:
- **persists to Mongo** — `*NaTr` fields on the `Fixture`/`Market`/`Selection` collections (the
  `*Lookup` collections carry none); see `WriteFixtureTranslations` / `WriteMarketTranslations` /
  `ProcessTranslationChange` in that same file;
- **publishes and pushes over SignalR** — translation-only `ON_ODDS_CHANGE` messages built in
  `PublishTranslationOnlyOddsChanges`, mapped into `OutgoingOddsChange`'s
  `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr`/`MaTeTr`/`SeNaTr` at
  `OE.Websocket.Service/Repository/SportsbookRepository.cs:1547-1927`.

## Claude assets
Commands, skills, checklists, playbooks, templates, prompts live in `.claude/` — see
[.claude/README.md](.claude/README.md). Also see [AGENTS.md](AGENTS.md).
