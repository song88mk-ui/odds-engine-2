# Checklist — Standard Development Workflow (Phase 12, MANDATORY)

Applies to **every task** in odds-engine-2, in order. If a step is N/A, say so explicitly. Authority:
[CLAUDE.md](../../CLAUDE.md) / [AGENTS.md](../../AGENTS.md).

## Standard Workflow
- [ ] 1. **Understand the requirement** — restated in one sentence.
- [ ] 2. **Searched the repository** for existing implementations (`/find-service`, `/find-pattern`;
       beware the 6 `SportsbookRepository` copies).
- [ ] 3. **Consulted the generated documentation** (`/docs`, [docs/README.md](../../docs/README.md)).
- [ ] 4. **Traced the feature end-to-end** (`/trace-*`).
- [ ] 5. **Identified reusable components** (`OE.Shared` contracts/helpers; correct repo) — reuse, not duplicate.
- [ ] 6. **Explained the proposed solution** before implementing.
- [ ] 7. **Identified risks & assumptions** (TechnicalDebt / ArchitectureRisks / Unknowns).
- [ ] 8. **Asked clarifying questions** where requirement/evidence is unclear.
- [ ] 9. **Obtained approval for architectural changes** (new patterns, contract changes, new
       services, sharding changes) — waited before proceeding.
- [ ] 10. **Implemented the smallest possible change.**
- [ ] 11. **Validated compilation & behavior** (`dotnet build src/OddsEngine/OddsEngine.sln`; stated
        how behavior was checked — no test projects in-repo).
- [ ] 12. **Updated documentation** if architectural knowledge changed (`/update-docs`; docs +
        CLAUDE.md/AGENTS.md/commands/skills/playbooks in sync).
- [ ] 13. **Summarized changes, risks, and validation results.**

## Permanent Engineering Principles (must hold throughout)
- [ ] Repository treated as the source of truth.
- [ ] Existing architecture preserved; no new patterns/libraries without approval.
- [ ] No guessing — verified facts / assumptions / unknowns clearly distinguished and tagged.
- [ ] **Production kept READ-ONLY** (no writes/deploys/schema changes/cache invalidations/admin
      actions unless explicitly instructed).
- [ ] External-repo behavior **identified, not invented**.
- [ ] Docs + Claude assets kept synchronized with any architectural change.

> Related: [before-coding](./before-coding.md), [before-pull-request](./before-pull-request.md),
> [architecture-review](./architecture-review.md). Production work: [production-investigation](./production-investigation.md).
