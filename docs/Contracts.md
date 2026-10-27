# Shared Contracts Catalog

Every contract that crosses the odds-engine-2 boundary, in one place, with its owner, consumers, and
change-risk. A contract here = a shape/namespace/key that two or more repos must agree on.
Classification: **[Verified]** · **[Verified-sibling:X]** · **[Historical:Reference]** · **[Unknown]**.

## Contract families

| # | Contract | Form | Owner | Other parties | Coupling/versioning |
|---|---|---|---|---|---|
| C1 | **OE.Shared message DTOs** | C# classes (`Outgoing*Message`, etc.) | odds-engine-2 (`OE.Shared`) | bet-genius (produces), backoffice-final (mirrors), platform (consumes via Mongo/messages) | NuGet `OE.Shared` (GitHub Packages, org FlyingCockatoo) + inline copies; rename = break |
| C2 | **MongoDB Sportsbook document schema** | BSON docs, abbreviated fields | odds-engine-2 (writer) | platform-final (read), backoffice-final (read) | implicit schema; field rename = break for readers |
| C3 | **RabbitMQ routing-key namespace** | string keys on `e.central` (topic) | shared convention | all OE.* + bet-genius + platform + backoffice | add safe; rename/move = break bindings |
| C4 | **RabbitMQ message envelope** | `BasicProperties.Type` = message-type constant + JSON/UTF8 body + W3C trace headers | odds-engine-2 (`MessageBroker`, `MessageType`) | all broker participants | dispatch keys off `Type` |
| C5 | **SignalR `/platform` hub methods** | method names + arg types | hub gateway [Unknown repo] | OE.Websocket (caller), platform-web (handlers) | signature change = break |
| C6 | **SignalR message models** | `OutgoingOddsChange`, `OutgoingBetStatus`, … | OE.Websocket + platform-web (`EngineMessages`) | both | **MessagePack Contractless** → property **rename changes wire key**; deploy together. ⚠️ **The translation props were renamed, not just re-added:** the old `SpNaTr/SegNaTr/LeNaTr/FiNaTr/MaNaTr/SeNaTr` are gone; `OutgoingOddsChange` now carries `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr` (fixture), `MaTeTr` (market) and `SeNaTr` (selection), all `List<OutgoingTranslation> {LaCo, Tx}`. Under Contractless that is a **breaking wire change** — a platform-web mirror still reading `SpNaTr` silently deserializes empty. `OutgoingBoostedOddsChange` carries **no** translations. **Null/empty means "unchanged" or "not translated yet", never "no translation" — clients keep their current names and fall back to the English `*Na`.** See [ArchitectureRisks.md](./ArchitectureRisks.md). |
| C7 | **Platform REST DTOs** | `BaseResponseDTO<T>` + endpoint DTOs | platform-final | OEv2 (UOF.Client/Websocket callers), platform-web, backoffice | `x-api-key` auth; `StatusCode` enum shared |
| C8 | **SQL `Trading` config schema** | EF entities (`ClientOddsFeedConfiguration`, …) | backoffice-final (writer) | OEv2 UOF.Client (reader) | column change = break |
| C9 | **SQL `Translation` schema** | `TranslationCache2` (+ legacy `TranslationCache`, external, read-only) | **shared, column-partitioned**: OEv2 owns `STRING_ID`/`SOURCE_*`/`PROCESSED`/`CREATED_AT`/`UPDATED_AT` (Translation.Service) and `HAS_BET_PLACEMENT` (Translation.Job); the **external translation pipeline** owns `TARGET_TEXT_ZH`/`_ES`/`_FR` | OEv2 Adapter (full preload), Translation.Service, Translation.Job | ⚠️ **A live two-writer contract, not a read-only one.** Neither side may write the other's columns. `PROCESSED = 1` means "off the worklist" — setting it wrongly silently drops a key from translation. `SportsbookDynamic`/`SportsbookGlossary` are retired **from OEv2's code only** — both tables still exist in the schema (no migration drops them). DDL lives in `infrastructure/flyway`, not here. |
| C10 | **External UOF feed** | `uof.*` keys on vhost `ext`, JSON | odds-engine-2 (UOF.Client) | external white-label clients | external versioned contract |
| C11 | **Shared enums** | `StatusCode`, `FixtureStatus`, `MarketStatus`, `SettlementStatus`, `BetStatus`, `CashoutStatus`, `VoidFactor`, `ChannelType`, `CacheType` | odds-engine-2 (`OE.Shared.Enums`) | bet-genius, platform, backoffice | numeric values are the contract |

## C1 — OE.Shared message DTOs (the core wire contract)
Defined in `OE.Shared/Messages/*` and `OE.Shared/Constants/MessageType.cs`. Full field listings:
[SharedLibraries.md](./SharedLibraries.md) §2. Key types and the `MessageType` they map to:

| DTO | MessageType constant | Producer → Consumer |
|---|---|---|
| `OnFixtureChangeMessage` | `OnFixtureChange` | bet-genius/UOF → Adapter → Sportsbook |
| `OnMarketChangeMessage` | `OnMarketChange` | …→ Sportsbook, Lookup, UOF.Client |
| `OnMarketSettlementMessage` | `OnMarketSettlement` | …→ Sportsbook |
| `OnMarketCancellationMessage` | `OnMarketCancel` | …→ Sportsbook, which drops it: no Worker case since DEVOPS-251. The `OnBetCancellation` it would publish on `r.oe.bet.settlement` sends `MarketId` as a string, and platform-final reads a long. Tripwire: `WorkerTests.MessageReceived_OnMarketCancel_IfDispatched_OnBetCancellationMarketIdMustBeNumeric` (GM-959) |
| `OnBetStopFixtureMessage` / `OnBetStopGlobalMessage` | `OnBetStopFixture` / `OnBetStopGlobal` | …→ Sportsbook → Websocket |
| `OnBetSettlementMessage` | `OnBetSettlement` | Sportsbook → platform Settlement |
| `OnPriceBoostMessage` | `OnPriceBoost` | backoffice (`r.uof.onpriceboost`) → Adapter → the fixture's Sportsbook shard (`r.oe.adapter.onpriceboost.<NN>`) → Mongo `IsBo`/`ClBo`/`ClBoOd`/`ClBoSeNa`/`ClBoSeNaTr`, read by platform-final `GetBoostedFixtures`. The JSON property names are backoffice's (`Backoffice.Shared.Models.OnPriceBoostMessage`): renaming one drops that field of every boost. `ClBoOd` is `CODE:odds\|CODE:odds`, a format platform-final splits on `\|` and `:` (BO-10) |
| `OnInternalOddsChangeMessage` | `OnOddsChange` | Sportsbook → Websocket |
| `OnDeleteCacheMessage` | (cache mgmt, `CacheType`) | backoffice/platform → cache consumers |
| `OnHeartbeatMessage` | `OnHeartBeat` | KeepAlive → UOF.Client |

[Verified] in-repo; [Verified-sibling:bet-genius] bet-genius produces the same types;
[Verified-sibling:backoffice-final] backoffice mirrors the Sportsbook data namespaces.

## C2 — MongoDB document schema
Collections + abbreviated-field glossary in [SharedLibraries.md](./SharedLibraries.md) §1 and
[DomainModel.md](./DomainModel.md). OEv2 is the sole **writer**; platform-final & backoffice-final
**read** (secondary preference). `[BsonIgnoreExtraElements]` on the collection classes gives some
additive tolerance (new fields don't break old readers), but **renames/removals break readers**. [Verified]

## C3/C4 — RabbitMQ routing keys + envelope
Namespace reference: [ConfigurationOverview.md](./ConfigurationOverview.md). Envelope:
`DeliveryMode=2`, `Type`=message-type constant, `CorrelationId`, W3C trace headers injected
(`MessageBroker.cs`). Topic exchange `e.central`, lowercased `|`-multi-bind keys. New keys are
additive-safe; renaming or changing the shard suffix scheme breaks bindings. [Verified]

## C5/C6 — SignalR hub contract
Hub methods OEv2 calls (outbound): `PublishOddsChangeForMainMarkets`, `…ForAllMarkets`,
`PublishBetStopFixture`, `PublishBetStopGlobal`, `PublishBetStatus`, `PublishCashOutChange`,
`PublishWalletChange`, `PublishPayableChange`. Events the UI handles: `OnOddsChange`,
`OnBetStopFixture`, `OnBetStopGlobal`, `OnBetslipOddsChange`, `OnBetslipBetStopFixture`,
`OnTopMarketsOddsChange`, `OnBetStatus`, `OnCashOutChange`, `OnWalletChange`. Models +
field tables: [WebSocket.md](./WebSocket.md). **MessagePack `ContractlessStandardResolver`** keys by
property **name** → renaming a property is a breaking wire change requiring lock-step deploy of OEv2
+ platform-web. [Verified]/[Verified-sibling:platform-web]

## C7 — Platform REST DTOs
`BaseResponseDTO<T> { StatusCode, Message, Result }` envelope; `StatusCode` enum (C11) shared.
Endpoints OEv2 calls + the wider read surface: [PlatformApi.md](./PlatformApi.md). Auth: `x-api-key`
(+ `x-client-id`, `x-operator-token`, `x-og-*` on platform-web). [Verified]/[Verified-sibling:platform-final]

## C8/C9 — SQL schemas
**C8 — Trading (read-only for OEv2):** `ClientOddsFeedConfiguration` (`ClientId`, `SportId`,
`PrematchOdds`, `LiveOdds`, `MaxFixtures`), `ClientOddsFeedLiveFixture`. **backoffice-final writes;
OEv2 reads.** [Verified]/[Verified-sibling:backoffice-final]

**C9 — Translation (two writers):** `TranslationCache2` (`ID`, `STRING_ID`, `SOURCE_LANGUAGE`,
`SOURCE_TEXT`, `TARGET_TEXT_ZH`/`_ES`/`_FR`, `HAS_BET_PLACEMENT`, `PROCESSED`, `CREATED_AT`,
`UPDATED_AT`). Column ownership is the contract — see the C9 row above.
The legacy external `TranslationCache` (`SOURCE_HASH`, `SOURCE_LANGUAGE`, `TARGET_LANGUAGE`,
`TARGET_TEXT`, …) is **no longer read by OEv2** — retired together with the `CacheBackfill` bridge
in GM-784 (2026-09-14). It, like `SportsbookDynamic`/`SportsbookGlossary`, is retired from OEv2's
code but **still exists in the schema**. Field listings: [SharedLibraries.md](./SharedLibraries.md) §1.2,
[Database.md](./Database.md). [Verified]

> **All SQL DDL for OEv2's databases lives in the `infrastructure/flyway` repo** (`migrations/<env>/`),
> applied with `just migrate <env>`. OEv2 ships no migrations, so an EF model here is a *mapping*, not
> a schema definition, and may omit objects the database actually has (e.g. two of
> `TranslationCache2`'s three indexes). Any schema change is a flyway PR **plus** an EF model change —
> and per CLAUDE.md every script must be idempotent.

## C10 — External UOF feed
`uof.onfixture.change.*`, `uof.onoddschange.*`, `uof.onbetstop*`, `uof.onmarketsettlement.*`,
`uof.onmarketcancellation.*`, `uof.onheartbeat` on vhost `ext`. Per-client margin (+2%) & Asian odds
formats applied at the boundary. External consumers not in workspace. [Verified] producer; [Unknown] consumers.

## C11 — Shared enums
Numeric values are the contract (see [SharedLibraries.md](./SharedLibraries.md) §4 for full tables).
`StatusCode` (~90 codes) is shared with platform for REST responses; status enums are shared with
bet-genius/backoffice for catalogue state. **Never renumber existing members.** [Verified]

## Governance summary
- **Additive-safe:** new routing keys, new optional Mongo fields (`[BsonIgnoreExtraElements]`), new
  enum members (new numbers), new REST endpoints.
- **Breaking (coordinate + lock-step deploy):** rename/remove of any DTO/Mongo/SQL field, SignalR
  property rename (MessagePack), enum renumbering, routing-key rename or shard-scheme change, hub
  method signature change.
- No central schema registry exists; contracts are enforced by **shared source (`OE.Shared`) + code
  copies (backoffice mirror, platform `EngineMessages`)**. Drift between the copies is the main risk
  — see [Unknowns.md](./Unknowns.md).
