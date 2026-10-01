# AGENTS.md — odds-engine-2

Operating guide for AI agents. Read alongside [CLAUDE.md](CLAUDE.md) (the primary context) and the
`/docs` knowledge base. This file adds agent-specific operating rules.

## Orientation (30 seconds)
- 11 headless .NET RabbitMQ worker services + `OE.Shared` kernel. No UI, no inbound HTTP here.
- Pipeline: `bet-genius → OE.Adapter → OE.Sportsbook(00–10) → MongoDB + r.oe.sb.engine.* →
  OE.Websocket → /platform hub → platform-web`. Side jobs + an external UOF egress (UOF.Client).
- Source of truth = the repo + `/docs`. Start at [docs/README.md](docs/README.md).

## MANDATORY Standard Workflow (Phase 12 — every task, in order)
1. Understand the requirement (restate it). 2. Search the repo for existing implementations.
3. Consult `/docs`. 4. Trace the feature end-to-end. 5. Identify reusable components.
6. Explain the proposed solution. 7. Identify risks & assumptions. 8. Ask clarifying questions when
needed. 9. **Wait for approval before architectural changes.** 10. Implement the smallest possible
change. 11. Validate compilation & behavior. 12. Update docs if architectural knowledge changed.
13. Summarize changes, risks, validation results.
Full version: [CLAUDE.md](CLAUDE.md) + [.claude/checklists/standard-workflow.md](.claude/checklists/standard-workflow.md).

## Permanent Engineering Principles (non-negotiable)
1. **Evidence over inference.** Every behavioral claim traces to file:line. Tag **Verified / Likely /
   Unknown**; never present a guess as fact. Unknowns → [docs/Unknowns.md](docs/Unknowns.md).
2. **Repository is the source of truth.** Read `/docs`, then the actual code. Use `/trace-*`/`/find-*`;
   don't assume from names (e.g. `TradingRepository` is sometimes a router, sometimes SQL).
3. **Preserve architecture; no new patterns/libraries without approval** (no AutoMapper/MediatR/
   Options/Redis) ([docs/ImplementationPatterns.md](docs/ImplementationPatterns.md)).
4. **Smallest change that works**, then validate it builds (`dotnet build`) and reason about behavior.
5. **Production is READ-ONLY** — never publish, deploy, run migrations/schema changes, mutate Mongo/SQL,
   or invalidate caches against a real environment unless explicitly instructed.
6. **Respect contracts.** `OE.Shared` DTOs/enums, Mongo schema, routing-key namespace, SignalR models
   are **cross-repo** — flag changes, identify affected siblings, **don't invent sibling behavior**
   ([docs/CrossRepositoryDependencies.md](docs/CrossRepositoryDependencies.md)).
7. **Keep docs + `CLAUDE.md`/`AGENTS.md`/commands/skills/playbooks synchronized** with architectural
   changes.
8. **SQL scripts must be idempotent.** Any SQL script (data fix, seed, DDL, hand-run/flyway migration)
   must be safe to run multiple times with the same end state: guard INSERTs with `NOT EXISTS`/`MERGE`
   and schema changes with `IF NOT EXISTS`/`OBJECT_ID` checks.

## High-blast-radius (touch with extra care)
- `OE.Shared/Constants/MessageType.cs` + `Messages/**` (wire/DB contract).
- `OE.Shared/Utilities/MessageBroker.cs` (every broker user) and `Helper.cs` (`GetInstanceName` sharding).
- `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs` (catalogue + settlement; most-changed file).
- MongoDB document schema (`OE.Shared/Data/Sportsbook/**`) — platform-final + backoffice readers.
See [docs/knowledge/FrequentlyModifiedAreas.md](docs/knowledge/FrequentlyModifiedAreas.md) +
[docs/ArchitectureRisks.md](docs/ArchitectureRisks.md).

## Known traps
- Committed `appsettings.json` are dev stubs (prod = argocd + secrets) — and some hold **live
  secrets** (do not echo secret values; treat as compromised, H1).
- `MemCache` `int` TTL is in **hours** (sub-hour truncates to 0).
- MongoDB `w=0` → silent write failures.
- SignalR MessagePack is name-keyed → property rename = breaking.
- `_IsBetStopGlobally` not thread-safe; Websocket threads ignore cancellation.
- Exception logging: `_logger.LogError(ex, "...")` with the exception first, never `ex.Message` /
  `ex.StackTrace` in the message; PascalCase placeholders; keep operational start/connect log lines
  (don't drop them for SonarQube S6664). See [docs/Logging.md §3a](docs/Logging.md).

## Subagent guidance
- For broad "where is X / how does Y flow" questions, dispatch read-only Explore agents (the codebase
  has misleading names; fan-out search beats guessing).
- Keep cross-repo investigation scoped to the integration seam; classify findings by repo
  ([Verified] / [Verified-sibling:X] / [Unknown]).

## Tooling
- Build: `dotnet build src/OddsEngine/OddsEngine.sln`. Nine test projects exist (`OE.Shared.Test`,
  `OE.Adapter.Service.Tests`, `OE.Sportsbook.Service.Tests`, `OE.Translation.Service.Tests`,
  `OE.Translation.Job.Tests`, `OE.Websocket.Service.Tests`, `OE.Job.KeepAlive.Tests`,
  `OE.Job.Lookup.Tests`, `OE.Job.Settlement.Tests`) — `OE.Job.Cleanup`, `OE.Job.LiveStream` and
  `OE.UOF.Client` have none; confirm before claiming coverage. CI runs these on PRs via the `test`
  job. A green suite is weak evidence: mutation scores per project, and the gaps behind them, are in
  [docs/Testing.md](docs/Testing.md).
  Git history: feature tickets `GM-####`, infra `DEVOPS-###`.

## Asset index
`.claude/` — commands, skills, checklists, playbooks, templates, prompts. See
[.claude/README.md](.claude/README.md).
