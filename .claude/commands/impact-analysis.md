---
description: Assess the blast radius of a proposed change (incl. cross-repo contracts)
argument-hint: [file / contract / routing key / field you intend to change]
---

Analyze the impact of changing `$ARGUMENTS`. Do not implement — report only.

1. **In-repo:** which services/files depend on it? (grep usages; note the 9 services + `OE.Shared`).
2. **Contract check:** is it a shared contract (message DTO, Mongo field, enum value, routing key,
   SignalR model)? If so it is a **cross-repo breaking change** — list affected siblings
   (bet-genius, platform-final, platform-web, backoffice-final) per
   [docs/Contracts.md](../../docs/Contracts.md) + [docs/CrossRepositoryDependencies.md](../../docs/CrossRepositoryDependencies.md).
   Additive (new key/optional field/new enum number) vs breaking (rename/remove/renumber).
3. **Sharding/coupling:** does it touch `GetInstanceName`, shard count, or routing-key shape? (couples
   Adapter+Sportsbook+argocd).
4. **Deploy coupling:** MessagePack rename → OEv2+platform-web must deploy together.
5. **Risk:** cross-ref [docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md),
   [docs/TechnicalDebt.md](../../docs/TechnicalDebt.md), hotspots
   ([docs/knowledge/FrequentlyModifiedAreas.md](../../docs/knowledge/FrequentlyModifiedAreas.md)).

Output: blast-radius list (files + repos), additive-vs-breaking verdict, required coordinated deploys,
risks, and what to verify before changing.
