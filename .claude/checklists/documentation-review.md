# Checklist — Documentation Review (OEv2)

- [ ] Did architectural knowledge change? If yes, which `/docs` files are affected
      ([docs/README.md](../../docs/README.md) is the index).
- [ ] Evidence-tag style preserved ([Verified]/[Likely]/[Unknown] + file:line).
- [ ] If a prior claim is now wrong, **corrected + noted** (precedent: translations-not-persisted,
      hub-host contradiction — see [docs/KnowledgeValidation.md](../../docs/KnowledgeValidation.md)).
- [ ] Contract/routing/shard change reflected in [docs/Contracts.md](../../docs/Contracts.md),
      [docs/ConfigurationOverview.md](../../docs/ConfigurationOverview.md), and `knowledge/*`.
- [ ] Pattern/convention change reflected in [docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md) /
      [docs/CodingStandards.md](../../docs/CodingStandards.md).
- [ ] New unknowns → [docs/Unknowns.md](../../docs/Unknowns.md); new risks/debt →
      [docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md) / [docs/TechnicalDebt.md](../../docs/TechnicalDebt.md).
- [ ] `CLAUDE.md` / `AGENTS.md` + affected `.claude/` assets updated (`/update-docs`).
- [ ] Cross-links valid; no duplication of canonical content (reference, don't copy).
