---
description: Trace a fixture end-to-end through the OEv2 pipeline (ingest → Mongo → push → UI)
argument-hint: [providerFixtureId or FiId]
---

Trace fixture `$ARGUMENTS` end-to-end. Report each hop with file:line evidence; mark verified / assumption / unknown.

1. **Ingest:** how the fixture enters — `OE.Adapter.Service` consumes `r.bg.receiver.#|r.uof.#`,
   normalizes to `OnFixtureChangeMessage`, and routes to `r.oe.adapter.on_fixture_change.<NN>` where
   `<NN> = Helper.GetInstanceName(providerFixtureId)` (last 2 digits → 00–09). Compute the shard for
   this id and state which `OE.Sportsbook.Service` instance owns it.
2. **Persist:** `OE.Sportsbook.Service/Repositories/SportsbookRepository.ProcessOnFixtureChangeMessage`
   → `ToFixture` upsert into Mongo `Fixture` (note `SetOnInsert` for `FiId`; translations are NOT
   persisted to the doc). Then it publishes `OnOddsChange` to `r.oe.sb.engine.onoddschange`.
3. **Lookups:** `OE.Job.Lookup` updates `*Lookup` collections.
4. **Push:** `OE.Websocket.Service` → `PublishOddsChange*` on the `/platform` hub, group
   `endpoint:{clientCode}:sports:{SpId}:…` / `fixturedetails:{FiId}`.
5. **Lifecycle:** status transitions (`FixtureStatus`), close (`OE.Job.Settlement`), purge
   (`OE.Job.Cleanup`), livestream enrichment (`OE.Job.LiveStream` → `LiId`/`GeAl`).

Use [docs/EntityLifecycle.md](../../docs/EntityLifecycle.md),
[docs/SequenceDiagrams.md](../../docs/SequenceDiagrams.md), [docs/DomainModel.md](../../docs/DomainModel.md).
Decode abbreviated fields via [docs/SharedLibraries.md](../../docs/SharedLibraries.md).
