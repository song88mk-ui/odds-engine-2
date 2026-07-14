# Playbook — Implement a Feature (OEv2)

End-to-end procedure. Pairs with the `feature-development` skill + `before-coding`/`before-pull-request`
checklists.

## 1. Understand & locate
- Restate the requirement. Run `/trace-feature <feature>` and `/find-service <responsibility>`.
- Read the relevant `/docs` + the actual handler/repo code. Identify service(s) + shard/client scope.

## 2. Design (within the architecture)
- Reuse `OE.Shared` contracts/helpers + the right service's repository (don't duplicate; 6
  `SportsbookRepository` copies exist).
- Decide: new message type? new routing key? Mongo field? → run `/impact-analysis`.
- If it touches a **shared contract** (DTO/Mongo/enum/routing key/SignalR), it's cross-repo —
  plan a coordinated change ([docs/Contracts.md](../../docs/Contracts.md)).
- New message type → use [.claude/templates/new-message-type.md](../templates/new-message-type.md).
  New service → [.claude/templates/new-service.md](../templates/new-service.md).

## 3. Explain & get approval
- Present approach + reusable components + risks/assumptions. **Await approval for architectural
  changes** (new patterns, contract changes, new services, sharding changes).

## 4. Implement (smallest change)
- Add handler dispatch on `BasicProperties.Type`; repo `Process*`/`To*`/`Prepare*`; idempotent
  `BulkWrite` upsert; `IMessageBroker.Publish` for egress; `Custom:*` flags; Activities + metrics +
  `LogXxxEx`. Abbreviated field naming. No committed secrets.

## 5. Validate
- `dotnet build src/OddsEngine/OddsEngine.sln`. Reason through behavior + edge cases (no tests in-repo).
- Run `/review-feature`. Thread-safety for shared state.

## 6. Document & summarize
- `/update-docs` for any architectural change; update `CLAUDE.md`/assets if conventions changed.
- Summary: changes, risks, validation results, coordinated-deploy needs.
