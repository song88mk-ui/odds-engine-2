---
name: mongodb-investigation
description: Investigate the MongoDB Sportsbook catalogue (collections, indexes, read/write patterns, performance). Use for data-shape questions, slow queries, write concerns, or "where is X stored".
---

# MongoDB Investigation (OEv2)

MongoDB `Sportsbook` is the catalogue OEv2 **owns/writes** (platform-final + backoffice read). Canonical
file: `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs`. Reference: [docs/MongoDb.md](../../../docs/MongoDb.md).

## Know this
- Collections: `Fixture`, `Market`, `Selection`, `BettingStatus`, `*Lookup`, `*Mapping`. Abbreviated
  fields — glossary [docs/SharedLibraries.md](../../../docs/SharedLibraries.md).
- Indexes (bootstrapped at Sportsbook startup): unique `idx_fiid`/`idx_maid`/`idx_seid`; compound
  `idx_fiid_mastid_mateid` (Market+Selection); `idx_maid` (Selection).
- Writes: unordered `BulkWrite` upserts, `Set`/`SetOnInsert`, **`w=0` (unacked — silent failures)**.
- Reads: `Builders<T>.Filter` + `Find`; primary read pref in OEv2.
- ⚠️ **Translations/`LiId`/`ClMa`/`GeAl`/`IsBo`/`PeSc` are NOT persisted** (commented out). **(2026-06-30)
  translations are also no longer in the published message** — the `*Tr`/`*NaTr` were removed from the
  pipeline entirely (the `*NaTr` Mongo fields still exist but stay unwritten). ⚠️ Settlement read
  `PrFiId+MaTeId+SeTeId+SeSp` is **unindexed** (P1).
- Dual client wiring (DI vs self-newed, P5).

## Procedure
Locate the collection/field → read the repo method (Process*/To*) → identify filter + index coverage
→ for perf, check the histogram `bulkwrite.duration` + query plan. Report with file:line + confidence.
**Production = read-only** (no writes/admin). Use `/performance-review` for perf depth.
