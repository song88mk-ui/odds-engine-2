# Template — MongoDB Collection / Field Change

⚠️ The Mongo `Sportsbook` document schema is a **shared contract** — platform-final and
backoffice-final read it (and backoffice carries inline schema copies). Run `/impact-analysis`.
Additive (new optional field) is tolerated by `[BsonIgnoreExtraElements]`; rename/remove **breaks readers**.

## Add a field
1. Add the property to the collection class in `OE.Shared/Data/Sportsbook/MongoDB/Collections/`,
   using an **abbreviated name** (glossary [docs/SharedLibraries.md](../../docs/SharedLibraries.md)).
2. Write it in the repo `To*` builder: `Set(s => s.X, message.X)` (or `SetOnInsert` if it's an
   immutable natural key). Note: many fields are currently **commented out / not persisted** — be
   deliberate (P4).
3. If queried, add an index in `OE.Sportsbook.Service/MongoIndexBootstrapper.cs` (avoid the unindexed
   read trap, P1).
4. Update readers (platform-final, backoffice) — coordinate.

## Rename/remove a field (breaking)
- Identify all readers (OEv2 + siblings). Plan a coordinated, possibly multi-step (add-new →
  migrate → remove-old) change. Document in [docs/Contracts.md](../../docs/Contracts.md).

## Notes
- Write concern is `w=0` (unacked) — failures are silent; verify via Mongo logs, not app exceptions.
- Indexes are created at Sportsbook startup (fail-fast if Mongo unreachable).
- Update [docs/MongoDb.md](../../docs/MongoDb.md), [docs/DomainModel.md](../../docs/DomainModel.md),
  [docs/knowledge/CommonModels.md](../../docs/knowledge/CommonModels.md).
