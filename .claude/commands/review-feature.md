---
description: Review a feature/change against OEv2 architecture, patterns, and contracts
argument-hint: [PR, branch, or description of the change]
---

Review `$ARGUMENTS`. Read the diff; check against the repo's own conventions (don't impose external ones).

Checklist:
- **Patterns:** matches [docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md) /
  [docs/CodingStandards.md](../../docs/CodingStandards.md)? No new patterns introduced unapproved?
- **Contracts:** any `OE.Shared`/Mongo/enum/routing-key/SignalR change? Additive vs breaking; siblings
  affected ([docs/Contracts.md](../../docs/Contracts.md))? MessagePack rename risk?
- **Persistence:** idempotent upsert, `Set`/`SetOnInsert` correct, indexes cover new queries
  ([docs/MongoDb.md](../../docs/MongoDb.md))? `w=0` implications?
- **Concurrency:** new shared mutable Singleton state (thread-safety, cf. H3)?
- **Sharding:** per-fixture routing preserved?
- **Config/secrets:** no committed secrets; flags via `Custom:*`; argocd-overridable?
- **Observability:** Activities/metrics/logging via `LogXxxEx`?
- **Exception logging:** exception passed as the first argument (`LogError(ex, ...)`), no
  `ex.Message`/`ex.StackTrace` in the message, no empty `catch`, operational log lines kept
  ([docs/Logging.md §3a](../../docs/Logging.md))?
- **Anti-patterns:** check against [docs/AntiPatterns.md](../../docs/AntiPatterns.md).

Output findings as Correctness / Contract / Risk / Style, each with file:line + confidence. Suggest
investigation, not refactors, unless asked.
