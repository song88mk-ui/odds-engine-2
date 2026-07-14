---
name: code-review
description: Review an OEv2 diff for correctness, contract impact, concurrency, and convention fit. Use on PRs/branches/working changes. Pairs with the /review-feature command.
---

# Code Review (OEv2)

Review against the repo's **own** conventions (don't impose external ones). Use `/review-feature` for
the structured run. References: [docs/CodingStandards.md](../../../docs/CodingStandards.md),
[docs/AntiPatterns.md](../../../docs/AntiPatterns.md), [docs/Contracts.md](../../../docs/Contracts.md).

## Checklist
- **Correctness:** handler dispatch on `BasicProperties.Type`; ack/nack on throw; idempotent upsert
  (`Set`/`SetOnInsert`); index coverage for new queries.
- **Contracts:** `OE.Shared`/Mongo/enum/routing-key/SignalR change → additive vs breaking; siblings
  affected; MessagePack rename = breaking.
- **Concurrency:** new shared mutable Singleton state without sync? (H3).
- **Sharding:** per-fixture routing preserved.
- **Config/secrets:** no committed secrets; flags via `Custom:*`; argocd-overridable.
- **Observability:** Activities + metrics + `LogXxxEx`.
- **Conventions/naming:** [docs/NamingConventions.md](../../../docs/NamingConventions.md); abbreviated
  fields; per-service skeleton; reuse not re-implement (6 `SportsbookRepository` trap).
- **Anti-patterns:** silent `catch {}`, `async void`, `Environment.Exit`, `new DbContext` per call.

## Output
Findings grouped Correctness / Contract / Risk / Style, each file:line + severity + confidence.
Suggest investigation; propose fixes only if asked.
