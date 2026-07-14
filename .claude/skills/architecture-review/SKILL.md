---
name: architecture-review
description: Review a change or design against odds-engine-2's architecture, contracts, and risk register. Use for design proposals, large diffs, or "is this the right approach" questions.
---

# Architecture Review (OEv2)

## What to check
- **Style fit:** event-driven worker pipeline; `OE.Shared` kernel; no worker→worker deps (no cycles).
  Does the change preserve this? ([docs/Architecture.md](../../../docs/Architecture.md))
- **Contract boundary:** message DTOs / Mongo schema / enums / routing keys / SignalR models are
  cross-repo contracts ([docs/Contracts.md](../../../docs/Contracts.md)). Additive vs breaking;
  affected siblings (bet-genius/platform-final/-web/backoffice).
- **Sharding invariant:** per-fixture sticky routing intact; shard count coupling (Adapter+Sportsbook+
  argocd) respected (ADR-003).
- **Persistence model:** idempotent upserts; `w=0`; no transactions — does the design rely on
  guarantees that don't exist? ([docs/PersistencePatterns.md](../../../docs/PersistencePatterns.md))
- **Concurrency:** new shared mutable Singleton state? (cf. H3 bet-stop race).
- **Patterns:** no new paradigms without approval ([docs/ImplementationPatterns.md](../../../docs/ImplementationPatterns.md)).
- **Risk register:** cross-ref [docs/ArchitectureRisks.md](../../../docs/ArchitectureRisks.md) (R1–R8),
  [docs/TechnicalDebt.md](../../../docs/TechnicalDebt.md).

## Output
Findings as Architecture-fit / Contract-impact / Risk / Inconsistency, each with file:line +
confidence + suggested investigation. Recommend approval-needed items explicitly. Don't propose
refactors unless asked.
