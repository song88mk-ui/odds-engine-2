# MongoDB — Sportsbook Catalogue

MongoDB is odds-engine-2's **operational system of record** for the sportsbook catalogue. All
[Verified] from `OE.Sportsbook.Service` + `OE.Shared` unless tagged.

## Connection & ownership
- Database: **`Sportsbook`**. Connection string `Custom:MongoDB-Sportsbook`
  (`mongodb://10.49.100.55:27017/sportsbook?retryWrites=true&w=0` in stg/prd; in-cluster in dev).
- **`w=0`** (unacknowledged writes) and `retryWrites=true` in the connection string — fire-and-forget
  write concern; throughput-oriented, **no write acknowledgement** (a notable durability trade-off).
- **OEv2 is the sole writer.** platform-final and backoffice-final read it (secondary preference).
- **Dual client wiring** [Verified]: `Program.cs` registers singleton `IMongoClient`/`IMongoDatabase`
  used only by `MongoIndexBootstrapper` at startup; `SportsbookRepository.PrepareDatabaseConnection`
  (`:340`) **news up its own `MongoClient`** from config and ignores the injected one. Redundant; both
  point at the same DB.

## Collections
Schemas + the abbreviated-field glossary: [SharedLibraries.md](./SharedLibraries.md) §1,
[DomainModel.md](./DomainModel.md). Collections actively used by Sportsbook.Service (`:345-348`):
`Fixture`, `Market`, `Selection`, `BettingStatus`. Maintained elsewhere: `*Lookup` (Job.Lookup),
`MarketMapping`/`SelectionMapping`. `[BsonIgnoreExtraElements]` on the document classes gives
additive read tolerance.

## Indexes — created at Sportsbook startup (`MongoIndexBootstrapper.cs`) [Verified]
| Collection | Index | Keys | Unique |
|---|---|---|---|
| Fixture | `idx_fiid` | `FiId` | ✅ |
| Market | `idx_maid` | `MaId` | ✅ |
| Market | `idx_fiid_mastid_mateid` | `FiId, MaStId, MaTeId` | — |
| Selection | `idx_seid` | `SeId` | ✅ |
| Selection | `idx_fiid_mastid_mateid` | `FiId, MaStId, MaTeId` | — |
| Selection | `idx_maid` | `MaId` | — |

> Created via `CreateManyAsync` in `Program.Main` before the host runs — so **MongoDB must be
> reachable at startup** or the service crash-loops (acceptable fail-fast). `BettingStatus` and the
> `*Lookup`/`*Mapping` collections have **no bootstrapped indexes** here (lookups rely on `_id` =
> their `*LoId`; mapping on `_id` = `MaId`/`SeId`).

## Write patterns — [Verified]
All writes go through one helper: `BulkWrite<T>` (`:578`) → `collection.BulkWrite(data,
{ IsOrdered = false, BypassDocumentValidation = false })`, timed into the `bulkwrite.duration`
histogram. Writes are **`UpdateOneModel` upserts**:

- **Fixture** (`ToFixture`, `:351`): filter `_id == message.Id`, `IsUpsert=true`. **`SetOnInsert(FiId)`**
  (immutable natural key) + `Set(...)` for all mutable fields; score/favorite set conditionally.
- **Market/Selection change** (`ToMarketAndSelectionChange`, `:458`): per market & selection,
  filter `_id == Id`, `IsUpsert=true`; **`SetOnInsert(FiId/MaId/SeId)`**, `Set(...)` the rest
  (odds, status, probabilities, `CoId=correlationId`).
- **Settlement** (`ToMarketAndSelection`, `:541`): `IsUpsert=false`; sets only market/selection
  **status** (`MaStId`/`MaStNa`, `SeStId`) — odds untouched.
- **Bet-stop fixture** (`ToDBFixtureBettingStatus`, `:419`): `IsUpsert=false`, sets `IsSu`; filter on `FiId`.
- **Inactivate markets** (`ToMarketInactiveStatus`, `:430`): per market, set status Inactive.
- **Global bet-stop** (`ToDBBettingStatus`, `:447`): upsert the `BeSo="betstop"` doc's `IsBeSo`.

> ⚠️ **Translations are NOT persisted to documents.** Every `*Tr`/`SeNaTr`/`MaNaTr` `.Set()` /
> `.AddToSetEach(...)` is **commented out** (`:386-398, :492-495, :531-532, :552-556, :568-569`).
> The Mongo `*NaTr` fields still exist in the schema but remain never-written.
> (Corrected 2026-06-30: translations removed from pipeline.) Translations are **no longer embedded
> into the published message either** — `MapTranslations`/`LoadTranslationsAsync` and the `*Tr` lists
> were deleted from `SportsbookRepository`/the OE.Shared messages, so the SQL read path is gone.
> (Corrects the Phase 3 phrasing "embedded onto documents".) Likewise `LiId`, `ClMa`,
> `GeAl`, `IsBo`, `ClBo`, period scores, betradar ids are commented out — those Fixture fields exist
> in the schema but are **not written by Sportsbook.Service** (LiveStream writes `LiId`/`GeAl`).

## Read patterns — [Verified]
| Where | Filter | Index used |
|---|---|---|
| `ProcessOnFixtureChangeMessage` markets/selections (`:80, :85`) | `FiId == X && MaStId ∈ {Active,Suspended}` | `idx_fiid_mastid_mateid` (prefix `FiId,MaStId`) ✅ |
| `ProcessOnMarketSettlementMessage` (`:146`) | `MaId == X` | `idx_maid` ✅ |
| `ProcessOnBetSettlementMessage` (`:189`) | `PrFiId == X && MaTeId && SeTeId && SeSp` | **none — unindexed** ⚠️ |
| `ProcessOnBetStopFixtureMessage` (`:243`) | `_id == X` | `_id` ✅ |
| `GetActiveMarketIds` (`:876`) | `FiId == X && MaStId ∈ {Active,Suspended}` + **projection** (`MaId,PrFiId` → `MarketStatusProjection`) | `idx_fiid_mastid_mateid` ✅ |
| `SetBetStopStatus` (`:869`) | `BeSo == "betstop"` | `_id` ✅ |

> ⚠️ The **bet-settlement selection lookup** (`PrFiId+MaTeId+SeTeId+SeSp`) is **not covered by any
> index** — a potential collection scan on the (large) Selection collection per settlement. Candidate
> index. Recorded in [TechnicalDebt.md](./TechnicalDebt.md).

## Read preference
- Sportsbook.Service does **not** set a read preference → reads use **primary** (it reads its own
  recent writes, so this is correct for consistency).
- platform-final / backoffice-final read with **Secondary** preference (offload, eventual). [Verified-sibling]

## Other Mongo users (intra-OEv2)
- **Job.Lookup** upserts `*Lookup` (unordered, last-writer-wins on name).
- **Job.LiveStream** updates `Fixture.LiId`/`GeAl` (BulkWrite).
- **Job.Settlement** reads `Fixture` (status/type/time filter).
- **Job.Cleanup** deletes aged Fixture+Market+Selection (per-doc delete loop, not bulk — debt M7);
  mapping-collection deletes commented out.
- **Websocket.Service** reads Fixture/Market/Selection for template/metadata lookups.

## Durability / consistency notes
- `w=0` → writes are not acknowledged; a failed write is invisible (no exception) unless the driver
  surfaces a connection error. Combined with `BulkWrite(IsOrdered=false)`, partial failures within a
  batch don't stop the rest and aren't individually reported.
- Idempotency via upsert-by-`_id`/natural key + the sticky shard guarantees per-fixture write
  serialization (one Sportsbook instance owns a fixture). See [PersistencePatterns.md](./PersistencePatterns.md).
