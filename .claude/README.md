# .claude/ — odds-engine-2 Claude Assets

Repository-specific assets derived from the codebase + the `/docs` knowledge base (Phases 1–11).
Start with [../CLAUDE.md](../CLAUDE.md) (always-loaded) and [../AGENTS.md](../AGENTS.md).

## Commands (`.claude/commands/` — slash commands)
| Command | Use |
|---|---|
| `/trace-fixture` `/trace-market` `/trace-selection` | trace a catalogue entity end-to-end |
| `/trace-websocket` `/trace-platform-api` | trace a real-time event / REST interaction |
| `/trace-feature` `/trace-request` | trace a capability / a message lifecycle |
| `/find-pattern` `/find-service` `/find-component` | locate the canonical pattern / owner / UI consumer |
| `/impact-analysis` | blast radius of a change (incl. cross-repo contracts) |
| `/review-feature` `/security-review` `/performance-review` | structured reviews |
| `/investigate-production` | READ-ONLY prod diagnosis |
| `/compare-reference` | regression check vs the `Reference` baseline |
| `/update-docs` | sync docs + assets after an architectural change |

## Skills (`.claude/skills/<name>/SKILL.md`)
feature-development · architecture-review · blazor-development (→ platform-web) ·
platform-api-investigation · websocket-investigation · mongodb-investigation · sql-investigation ·
translation-investigation · performance-analysis · regression-analysis · bug-investigation ·
production-investigation · code-review · repository-analysis.

## Checklists (`.claude/checklists/`)
**standard-workflow (MANDATORY — Phase 12)** · before-coding · before-pull-request ·
architecture-review · regression-review · production-investigation · deployment-review ·
documentation-review.

## Playbooks (`.claude/playbooks/`)
implement-feature · investigate-bug · trace-websocket · trace-api ·
production-read-only-investigation · performance-investigation · regression-investigation.

## Templates (`.claude/templates/`)
new-service · new-message-type · mongo-collection-change · pr-description.

## Prompts (`.claude/prompts/`)
onboard · change-proposal · production-safe.

## Mandatory workflow (Phase 12)
Every task follows the 13-step **Standard Workflow** + **Permanent Engineering Principles** defined in
[../CLAUDE.md](../CLAUDE.md) / [../AGENTS.md](../AGENTS.md), with the checklist at
[checklists/standard-workflow.md](checklists/standard-workflow.md).

## Principles baked into every asset
- Repo + `/docs` are the source of truth; **evidence over inference** (Verified/Likely/Unknown).
- Preserve the architecture; **no new patterns without approval**; smallest viable change;
  **wait for approval before architectural changes**.
- **Production is read-only** by default.
- Shared contracts (`OE.Shared` DTOs, Mongo schema, enums, routing keys, SignalR models) are
  **cross-repo** — identify dependencies, don't invent sibling behavior.
- Keep docs + these assets in sync with architectural changes.

> These assets reference the `/docs` knowledge base rather than duplicating it — update both together.
