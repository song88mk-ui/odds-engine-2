---
description: Update the /docs knowledge base + Claude assets after an architectural change
argument-hint: [what changed]
---

Synchronize documentation with the change `$ARGUMENTS`. Keep docs the source-of-truth they are.

1. Identify which `/docs` files the change affects (architecture, contracts, config, domain, patterns,
   debt, unknowns…). Use [docs/README.md](../../docs/README.md) as the index.
2. Update only what changed; preserve the evidence-tag style ([Verified]/[Likely]/[Unknown] +
   file:line). If a prior claim is now wrong, **correct it and note the correction** (as done for the
   translations-not-persisted finding).
3. If a contract/routing-key/shard rule changed, update [docs/Contracts.md](../../docs/Contracts.md),
   [docs/ConfigurationOverview.md](../../docs/ConfigurationOverview.md), and the relevant
   `knowledge/*` reference.
4. If patterns/conventions changed, update [docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md)
   / [docs/CodingStandards.md](../../docs/CodingStandards.md) and the affected `.claude/` assets
   (commands/skills/playbooks) + `CLAUDE.md`/`AGENTS.md`.
5. Add new open questions to [docs/Unknowns.md](../../docs/Unknowns.md); new risks to
   [docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md)/[docs/TechnicalDebt.md](../../docs/TechnicalDebt.md).

Output: list of files updated + a one-line rationale each.
