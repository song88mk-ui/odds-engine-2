---
description: Trace a feature/capability across services and (if needed) sibling repos
argument-hint: [feature, e.g. cashout | bet-stop | boosted odds | livestream]
---

Trace feature `$ARGUMENTS` across OEv2 (and identify sibling-repo dependencies — don't invent them).

1. **Locate** all touchpoints: grep routing keys, message types, enum values, field codes, repo
   methods. Note which of the 9 services participate.
2. **Order the flow** ingest → normalize/shard → persist (Mongo/SQL) → publish → push/egress →
   consumer. Use file:line.
3. **Cross-repo:** classify each external hop [Verified] / [Verified-sibling:X] / [Unknown] using
   [docs/CrossRepositoryDependencies.md](../../docs/CrossRepositoryDependencies.md) +
   [docs/OddsEngineIntegration.md](../../docs/OddsEngineIntegration.md).
4. **Reusable components:** flag existing repos/helpers/contracts to reuse (avoid the 6×
   `SportsbookRepository` duplication trap — match the right service).
5. **Risks/unknowns:** cross-ref [docs/TechnicalDebt.md](../../docs/TechnicalDebt.md) +
   [docs/Unknowns.md](../../docs/Unknowns.md).

Output: a numbered end-to-end trace + a "files involved" list + risks/assumptions.
