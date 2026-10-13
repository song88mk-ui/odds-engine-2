# CLAUDE.md — odds-engine-2 (OEv2)

<!-- song88:standing-rules:begin -->
## Standing rules (every prompt, before anything else)

Shared by every song88 repo. The canonical text is `../song88-claude/standing-rules/STANDING-RULES.md`:
edit it there and run `../song88-claude/standing-rules/sync_standing_rules.py`, never edit this block by hand. A `UserPromptSubmit`
hook in `.claude/settings.json` re-injects these rules on every prompt.

1. **Read this repo's CLAUDE.md and AGENTS.md first.**
2. **Fetch latest before investigating.** Run `git fetch origin` and compare with origin/main for this repo and every sibling repo you read, before citing file contents or repo or deployment state as fact. A local checkout can be hundreds of commits behind. When you cannot or should not pull, read files with `git show origin/main:<path>`, and say so explicitly if you did not check.
3. **Trace in the code, never from memory.** Search the repo and its docs, trace the feature end to end, and cite `file:line`.
4. **Label every claim verified, assumption or unknown.** Never guess.
5. **Explain the plan and wait for approval before any code change.** Then make the smallest change that does the job.
6. **Production is read-only.** Do not run queries, deploy, change schema or invalidate caches unless explicitly instructed.
7. **Update docs and README in the same change** whenever the change affects what they describe.
8. **Every SQL script must be idempotent.** Re-running it is a no-op with the same end state.
9. **Do not modify a sibling repo as a side effect of work here.** Siblings are read-only references unless the user asks for a change in that repo.
10. **Never print, log or commit a secret.** Mask it if it must appear in a log line.
11. **A new project or service ships with tests** in the repo's existing test framework.
12. **Commit, push or open a PR only when explicitly asked.** "Implement it" authorizes local edits only.
13. **Report validation with every change:** build, unit tests, integration tests, manual checks and remaining risks. If something could not be validated, say what and why, name the tests to run before deployment, and mark the work Implementation Complete – Validation Pending, never Completed.
14. **If a workflow step does not apply, say so** instead of skipping it silently.
<!-- song88:standing-rules:end -->

Guidance for Claude when working in this repository. Derived from the codebase + the `docs/`
knowledge base (Phases 1–10). **Read the relevant `docs/` file before acting** — this file is the
index + the rules that matter most.

## What this repo is
An **event-driven pipeline of 11 headless .NET executable projects (services and jobs)** that ingest normalized odds/fixture/
settlement messages from RabbitMQ (`e.central`), persist a MongoDB **Sportsbook** catalogue (SQL
Server for translations + client config), and fan results out to (a) the platform via a SignalR push
bridge + settlement messages and (b) an external white-label Unified Odds Feed.
**There is no UI and no inbound HTTP here** — the Blazor app is the sibling `platform-web`.

- Source: `src/OddsEngine/` (solution `src/OddsEngine/OddsEngine.sln`). Services are `OE.*` projects; shared kernel
  is `OE.Shared` (every service references only it — no worker→worker deps, no cycles).
- Full map: [docs/README.md](docs/README.md) → [docs/Architecture.md](docs/Architecture.md),
  [docs/ProjectOverview.md](docs/ProjectOverview.md).

## Tech stack
Verified from `src/OddsEngine/**/*.csproj`, 2026-09-10.

| Concern | What |
|---|---|
| Shared kernel | `OE.Shared` targets **`netstandard2.1`** — new shared code must stay inside that API surface |
| Broker | `RabbitMQ.Client` 5.2.0 — raw client behind `IMessageBroker`; **no Wolverine/MassTransit** |
| SQL | `Microsoft.EntityFrameworkCore(.SqlServer)` 3.1.32 |
| Cache | `System.Runtime.Caching` (`MemoryCache.Default`) — no Redis |
| Tests | `xunit` **2.5.3** (not v3) + `coverlet.collector`; no Testcontainers |
| Language | `<Nullable>enable</Nullable>` everywhere except Job.Cleanup, Job.KeepAlive, Job.LiveStream and Job.Lookup, no `LangVersion` pin; `.editorconfig` mandates **block-scoped** namespaces (`csharp_style_namespace_declarations = block_scoped`) — do not convert files to file-scoped |

Note: EF Core 3.1.32 is an out-of-support package running on a `net10.0` target. Recorded as an
observation, not a change request — upgrading it is a cross-repo decision.

## Build / test
- Build: `dotnet build src/OddsEngine/OddsEngine.sln` (note: `OE.Job.KeepAlive` is NOT in the .sln but
  CI builds it, `.github/workflows/ci.yml:57`).
- Test: `dotnet test src/OddsEngine/OddsEngine.sln` (or a single `-Tests`/`-Test` project). Ten test
  projects exist, all in the solution — `OE.Shared.Test`, `OE.Adapter.Service.Tests`,
  `OE.Sportsbook.Service.Tests`, `OE.Translation.Service.Tests`, `OE.Translation.Job.Tests`,
  `OE.Websocket.Service.Tests`, `OE.Job.KeepAlive.Tests`, `OE.Job.Lookup.Tests`,
  `OE.Job.Settlement.Tests`, `OE.Job.Cleanup.Tests`. `OE.Job.LiveStream` and `OE.UOF.Client` have
  none. `OE.Job.KeepAlive.Tests` is in the .sln even though `OE.Job.KeepAlive` itself is not.
- Mutation testing: `dotnet stryker --project <Project>.csproj` from the matching test-project
  folder (global tool, config in `src/OddsEngine/stryker-config.json`, no CI gate). Scores and
  gaps: [docs/Testing.md](docs/Testing.md). A green suite is not evidence a bug would be caught.
- Local run: `dotnet run` from a service folder; needs local RabbitMQ + MongoDB (+ SQL for
  Sportsbook/UOF). Uses each service's committed settings file plus a git-ignored `Development` overlay.
- `src/OddsEngine/.editorconfig` applies to the whole solution and suppresses `CS8601`
  (nullable-reference assignment warning) — don't be surprised it's silent on that class of bug.
- CI builds each changed service to ECR; argocd deploys. ([docs/Hosting.md](docs/Hosting.md))

## The 30-second architecture
```
bet-genius / (oddsfeed?) ─► OE.Adapter.Service ─► r.oe.adapter.<type>.<NN>
   ├─► OE.Sportsbook.Service(00–10) ─► MongoDB + r.oe.sb.engine.* + r.oe.bet.settlement
   ├─► OE.Job.Lookup ─► *Lookup collections
   └─► OE.UOF.Client ─► external broker (vhost ext, uof.*)
OE.Websocket.Service ◄─ r.oe.sb.engine.* / r.bet.* / r.wallet.* ─► /platform SignalR hub ─► platform-web
```
Jobs: LiveStream (BetGenius video, 60s), Settlement (close stale, 5m), Cleanup (purge, 5m window),
KeepAlive (heartbeat, 5s), plus Lookup (above) and `OE.Translation.Job`.

## Critical rules
Get these wrong and you break prod.

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
   any `Outgoing*`/`EngineMessages` model is a breaking wire change; deploy OEv2, the platform-final
   `/platform` hub (`Platform.Websocket.API2`, which re-types payloads in `IPlatformEvent.cs`) and
   platform-web together. ([docs/WebSocket.md](docs/WebSocket.md))
   The `SoOr` stamped on pushed fixtures is the client's league order, cached per client
   (`{clientCode}:fso:...`) from Platform API `fixturesortorder`; it reloads on
   `OnOddstableLeagueSortOrderChange` (GM-851, needs the routing key in the argocd
   `MessageBroker__RoutingKey`) and at startup, not on `OnSportSortOrderChange`.
5. **Abbreviated field names everywhere** (`FiId`, `MaStId`, `Od`, `PrFiId`, …) — use the glossary in
   [docs/SharedLibraries.md](docs/SharedLibraries.md). New persisted/wire fields must follow it.
6. **MongoDB writes use `w=0`** (unacknowledged) — write failures are silent; check Mongo logs.
7. **Gotchas** (full register: [docs/TechnicalDebt.md](docs/TechnicalDebt.md)):
   `_IsBetStopGlobally` is not thread-safe (H3); Websocket spawns unmanaged threads +
   `Environment.Exit(1)` (H2); UOF entitlement validation is disabled (H5).
8. **Translations are back in the pipeline.**
   **Dead — the old SQL path:** `OE.Sportsbook.Service`'s
   `TranslationRepository`/`TranslationContext` are gone, and nothing writes the `sb:translation:*`
   MemCache any more. One live remnant: `ProcessCacheInvalidationMessage` still deletes by that prefix on
   `ON_DELETE_CACHE` (`src/OddsEngine/OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:340`
   `ProcessCacheInvalidationMessage`), so that handler now purges a namespace nothing populates.

   **Live — the current path:** `OE.Translation.Service` owns a message-driven path moved in
   from `OE.Sportsbook.Service` (see the "moved from OE.Sportsbook.Service" comment at
   `src/OddsEngine/OE.Translation.Service/Repositories/MongoTranslationRepository.cs:682`). It:
   - **persists to Mongo** — `*NaTr` fields on the `Fixture`/`Market`/`Selection` collections (the
     `*Lookup` collections carry none); see `WriteFixtureTranslations` / `WriteMarketTranslations` /
     `ProcessTranslationChange` in that same file;
   - **publishes and pushes over SignalR** — translation-only `ON_ODDS_CHANGE` messages built in
     `PublishTranslationOnlyOddsChanges`, mapped into `OutgoingOddsChange`'s
     `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr`/`MaTeTr`/`SeNaTr` at
     `src/OddsEngine/OE.Websocket.Service/Repository/SportsbookRepository.cs:1526-1925`;
   - **scopes MaTe updates by `SrcTx`** — since GM-852 every producer sends the row's SOURCE_TEXT, so
     a `MaTe:` delta, base or variant, updates only the open markets whose `MaNa` matches it, ignoring
     case. The filter and grouping are pure methods (`GroupDeltas`, `AddMarketTemplateDelta`,
     `BuildMaTeMarketFilter`) tested in `MongoTranslationRepositoryTests` by rendering the filter
     (GM-874).

## How this team writes code
Match it — don't introduce new patterns.

- Per-service skeleton: a `Program.cs` (Generic Host + DI), a `Worker.cs` (BackgroundService → dispatch
  on `BasicProperties.Type`), `Repository|Repositories/` (`IXxx`+`Xxx`, Singleton), `Telemetry/
  Instrumentation.cs`, `appsettings.json`, `Dockerfile`.
- Persistence: idempotent upsert via `BulkWrite<T>` (`UpdateOneModel`, `IsOrdered=false`), natural
  keys with `SetOnInsert`, mutable fields with `Set`. No transactions/unit-of-work.
- Messaging: `IMessageBroker.Publish` / `CPublish` (sets `DeliveryMode=2`, `Type`, `CorrelationId`,
  OTel headers). Topic exchange `e.central`, lowercase dotted routing keys.
- Caching: in-process `MemCache` (`MemoryCache.Default`); **TTL `int` overload is in HOURS**;
  cache-aside + negative caching. No Redis.
- Logging: Serilog (Compact JSON) + `LogInformationEx`, `LogErrorEx`, `LogWarningEx` extensions;
  OpenTelemetry traces propagated through message headers.
  - **Exceptions: pass the exception as the first argument**, `_logger.LogError(ex, "... {Payload}",
    payload)` (or `LogErrorEx("...", ex)`). Never put `ex.Message` / `ex.StackTrace` in the message
    text and never pass the exception as a template argument. No empty `catch` blocks.
  - **Secrets in logs:** a sibling repo (platform-final, GM-880, 2026-09-30) found `_logger.LogError`
    printing an OG private API key on every failed bet placement, visible in PRD Loki; mask a
    secret (keep only the last few characters, blank the rest) before it reaches a log line.
  - **PascalCase placeholders** (`{Message}`, `{Payload}`).
  - **Keep operational log lines** (start / connecting / connected / progress). Don't remove or merge
    them to satisfy SonarQube S6664.
  - Full rule: [docs/Logging.md §3a](docs/Logging.md).
- Config: imperative `IConfiguration` (`cfg["A:B"]`, `GetValue<T>`) — **no Options pattern**.
- No AutoMapper (manual `To*`/`Prepare*`/`Map*`), no MediatR, no FluentValidation (guard clauses).
- Full pattern catalog: [docs/ImplementationPatterns.md](docs/ImplementationPatterns.md);
  naming: [docs/NamingConventions.md](docs/NamingConventions.md).

## Where things live
| You need | Go to |
|---|---|
| Decode an abbreviated field | [docs/SharedLibraries.md](docs/SharedLibraries.md) |
| Which service owns a routing key | [docs/ConfigurationOverview.md](docs/ConfigurationOverview.md) |
| Sharding / messaging spine | `src/OddsEngine/OE.Shared/Utilities/Helper.cs` and `.../MessageBroker.cs` |
| Catalogue writes (the hot file) | `src/OddsEngine/OE.Sportsbook.Service/Repositories/SportsbookRepository.cs` |
| Cross-repo contracts | [docs/Contracts.md](docs/Contracts.md), [docs/OddsEngineIntegration.md](docs/OddsEngineIntegration.md) |
| Known issues / risks / unknowns | [docs/TechnicalDebt.md](docs/TechnicalDebt.md), [docs/ArchitectureRisks.md](docs/ArchitectureRisks.md), [docs/Unknowns.md](docs/Unknowns.md) |
| Symptom → cause | [docs/knowledge/Troubleshooting.md](docs/knowledge/Troubleshooting.md) |
| What changes most | [docs/knowledge/FrequentlyModifiedAreas.md](docs/knowledge/FrequentlyModifiedAreas.md) |
| Test inventory, mutation scores, coverage gaps | [docs/Testing.md](docs/Testing.md) |

## Development workflow
Follow these steps for every task, in order. Do not skip steps (standing rule 14).

1. **Understand the requirement** — restate it in one sentence.
2. **Search the repository** for existing implementations (the `find-service` and `find-pattern` commands; 6
   `SportsbookRepository` copies exist — match the right one).
3. **Consult the generated documentation** — [docs/README.md](docs/README.md).
4. **Trace the feature end-to-end** (`/trace-*` commands) before changing behavior.
5. **Identify reusable components** (`OE.Shared` contracts/helpers; the correct repo) — reuse, don't duplicate.
6. **Explain the proposed solution** before implementing.
7. **Identify risks and assumptions** (cross-ref TechnicalDebt/ArchitectureRisks/Unknowns).
8. **Ask clarifying questions** when the requirement or evidence is unclear.
9. **Wait for approval before any code change.** Contract changes, new patterns, new services and
   sharding changes also need the cross-repo check in Critical rules 1, 3 and 4.
10. **Implement the smallest possible change.**
11. **Validate compilation and behavior** (`dotnet build src/OddsEngine/OddsEngine.sln`, then
    `dotnet test` on the relevant project(s) from Build / test; state how behavior was checked).
12. **Update documentation** in `docs/` for every change, bug fixes, security fixes and resolved
    debt included (the `update-docs` command; keep CLAUDE.md / AGENTS.md / commands / skills /
    playbooks in sync), and `README.md` whenever the change affects what it documents
    (projects/frameworks, prerequisites, build/run/test instructions, configuration, endpoints).
13. **Summarize changes, risks, and validation results.**

Playbook: [.claude/checklists/standard-workflow.md](.claude/checklists/standard-workflow.md).

## Engineering principles
- **The repository is the source of truth.**
- **Preserve existing architecture; never introduce new patterns/libraries without approval.**
- **For external repositories, identify dependencies — never invent their behavior**
  ([docs/CrossRepositoryDependencies.md](docs/CrossRepositoryDependencies.md)).

## Scope & build reminders
- Subject is **odds-engine-2** (headless backend); UI work belongs to `platform-web`.
- **Match the right copy** before editing: 6 `SportsbookRepository` classes exist across services.
- `OE.Shared` must stay within `netstandard2.1`; services target `net10.0`.

## Assets in this repo
Commands, skills, checklists, playbooks, templates, prompts live in `.claude/` — see
[.claude/README.md](.claude/README.md).

**Roslyn MCP navigator** (token-cheap code search). When the `cwm-roslyn-navigator` tools are
available, prefer them over `grep` for symbol work:
- `find_symbol` — locate a type/member; the right tool when 6 `SportsbookRepository` copies exist.
- `find_references` / `find_callers` — run before changing any signature, DTO field, or enum
  (see Critical rule 3: `OE.Shared` edits are cross-repo breaking changes).
- `find_implementations` — interface to concrete for the `IXxx` + `Xxx` singleton pairs.
- `get_project_graph` — verify the "every service references only `OE.Shared`, no cycles" rule.
- `get_diagnostics` — after edits, alongside `dotnet build`.

No `.mcp.json` is committed; registration is per-developer
(`claude mcp add --scope user cwm-roslyn-navigator -- cwm-roslyn-navigator --solution <path>`).
Fall back to `grep`/`/find-*` commands when the server is not registered.

## See also
[AGENTS.md](AGENTS.md) · [docs/README.md](docs/README.md) · [docs/Architecture.md](docs/Architecture.md) ·
[docs/TechnicalDebt.md](docs/TechnicalDebt.md)
