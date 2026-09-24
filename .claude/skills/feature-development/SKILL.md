---
name: feature-development
description: Implement a feature in odds-engine-2 the way this team does — trace first, reuse existing patterns, respect cross-repo contracts, smallest change. Use when adding/modifying worker behavior, message handling, persistence, or the external feed.
---

# Feature Development (OEv2)

odds-engine-2 = 11 headless .NET executable projects (RabbitMQ services and jobs) + `OE.Shared`. Follow the repo's conventions; do not
introduce new patterns/libraries without approval.

## Procedure
1. **Understand + locate.** Run `/trace-feature` and `/find-service`. Read the relevant `/docs`
   (start [docs/README.md](../../../docs/README.md)) and the actual code.
2. **Reuse.** Find the canonical pattern with `/find-pattern`. ⚠️ 6 `SportsbookRepository` copies
   exist — edit the **correct service's** repo. Reuse `OE.Shared` contracts/helpers.
3. **Design within the architecture.** Per-service skeleton (Program/Worker/Repository/Telemetry).
   Persistence = idempotent `BulkWrite<T>` upsert (`Set`/`SetOnInsert`). Messaging = `IMessageBroker.
   Publish` (sets DeliveryMode/Type/CorrelationId/trace). Config = `Custom:*` flags, imperative read.
4. **Contracts.** If you touch `OE.Shared` DTOs / Mongo schema / enums / routing keys / SignalR
   models → run `/impact-analysis`; it's a cross-repo change ([docs/Contracts.md](../../../docs/Contracts.md)).
   Abbreviated field naming ([docs/SharedLibraries.md](../../../docs/SharedLibraries.md)).
5. **Sharding.** Don't break per-fixture routing (`GetInstanceName`); new routing keys must fit the
   `r.oe.*.<NN>` scheme if shard-bound.
6. **Implement the smallest change.** Add Activities/metrics + `LogXxxEx` logging.
7. **Validate.** `dotnet build src/OddsEngine/OddsEngine.sln`; reason about behavior; no committed
   secrets; thread-safety for shared Singleton state (cf. H3).
8. **Sync docs/assets** ([/update-docs](../../commands/update-docs.md)).

## Output
Proposed approach → risks/assumptions → (await approval if architectural) → implement → summary of
changes/risks/validation. Templates: [.claude/templates/](../../templates/).
