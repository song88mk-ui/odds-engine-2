# Template — New Message Type / Routing Key

⚠️ Message contracts in `OE.Shared` are **cross-repo** (bet-genius, platform, backoffice). Run
`/impact-analysis` first. Additive (new type/key) is safer than changing an existing one.

## Steps
1. **Constant:** add to `OE.Shared/Constants/MessageType.cs` — `public const string ON_X = "OnX";`
   (SCREAMING_SNAKE name → PascalCase value).
2. **DTO:** add the message class under `OE.Shared/Messages/Outgoing{Adapter|Sportsbook}Messages/`.
   - Inherit `OutgoingAdapterMessageBase` if fixture-scoped.
   - Use **abbreviated field names** ([docs/SharedLibraries.md](../../docs/SharedLibraries.md)); composite
     `Id` if applicable.
3. **Routing key:** lowercase dotted `r.<system>.<area>.<event>[.<NN>]`. If shard-bound, follow
   `r.oe.adapter.<type>.<NN>` and ensure the consumer binds it (committed appsettings **and** argocd).
4. **Publish:** `messageBroker.Publish(payload, MessageType.ON_X, "r.oe...x", correlationId)`
   (sets DeliveryMode/Type/CorrelationId/trace). Use `CPublish` from multi-threaded code.
5. **Consume:** add a `case MessageType.ON_X:` in the consumer's dispatch → repository `ProcessX`.
6. **Persist (if needed):** `To*` builder → `BulkWrite<T>` upsert; new query → add an index in
   `MongoIndexBootstrapper`.
7. **SignalR (if it reaches UI):** add the `Outgoing*` model + hub method; **MessagePack name-keyed**
   → mirror in platform-web `EngineMessages`; deploy both together.
8. **Docs:** update [docs/Contracts.md](../../docs/Contracts.md),
   [docs/knowledge/CommonDtos.md](../../docs/knowledge/CommonDtos.md),
   [docs/ConfigurationOverview.md](../../docs/ConfigurationOverview.md) (routing-key table).

## Checklist
- [ ] Additive vs breaking decided; siblings notified if breaking.
- [ ] Consumer binding added in argocd (not just committed appsettings).
- [ ] Index added for any new Mongo query.
