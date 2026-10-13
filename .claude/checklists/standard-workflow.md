# Checklist — Development workflow

Applies to **every task** in odds-engine-2, in order. If a step is N/A, say so explicitly. Authority:
[CLAUDE.md](../../CLAUDE.md) § Development workflow and its standing rules.

## Development workflow
- [ ] 1. **Understand the requirement** — restated in one sentence.
- [ ] 2. **Searched the repository** for existing implementations (`/find-service`, `/find-pattern`;
       beware the 6 `SportsbookRepository` copies).
- [ ] 3. **Consulted the generated documentation** (`/docs`, [docs/README.md](../../docs/README.md)).
- [ ] 4. **Traced the feature end-to-end** (`/trace-*`).
- [ ] 5. **Identified reusable components** (`OE.Shared` contracts/helpers; correct repo) — reuse, not duplicate.
- [ ] 6. **Explained the proposed solution** before implementing.
- [ ] 7. **Identified risks & assumptions** (TechnicalDebt / ArchitectureRisks / Unknowns).
- [ ] 8. **Asked clarifying questions** where requirement/evidence is unclear.
- [ ] 9. **Obtained approval before any code change** — waited before proceeding. Contract changes,
       new patterns, new services and sharding changes also got the cross-repo check.
- [ ] 10. **Implemented the smallest possible change.**
- [ ] 11. **Validated compilation & behavior** (`dotnet build src/OddsEngine/OddsEngine.sln`; then
        `dotnet test` on the relevant project(s); stated how behavior was checked).
- [ ] 12. **Updated documentation** in `docs/` for every change (`/update-docs`; docs +
        CLAUDE.md/AGENTS.md/commands/skills/playbooks in sync; `README.md` if it documents what changed).
- [ ] 13. **Summarized changes, risks, and validation results.**

## Engineering principles (hold throughout)
- [ ] Repository treated as the source of truth.
- [ ] Existing architecture preserved; no new patterns/libraries without approval.
- [ ] No guessing — every claim labelled verified / assumption / unknown.
- [ ] **Production kept read-only** (no writes/deploys/schema changes/cache invalidations/admin
      actions unless explicitly instructed).
- [ ] External-repo behavior **identified, not invented**.
- [ ] Docs + Claude assets kept synchronized with any architectural change.

> Related: [before-coding](./before-coding.md), [before-pull-request](./before-pull-request.md),
> [architecture-review](./architecture-review.md). Production work: [production-investigation](./production-investigation.md).
