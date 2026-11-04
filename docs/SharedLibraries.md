# Shared Libraries — OE.Shared Catalog

`OE.Shared` (netstandard2.1) is the shared kernel. This is the most important file to know
before touching anything: changing a contract here ripples across all 11 services **and**
bet-genius. All [Verified].

## ⭐ Field-name glossary (abbreviation convention)

MongoDB documents and message DTOs use terse 2–6 char abbreviations to shrink payloads. The
convention: `<Entity><Attribute>`, where `Id`/`Na`/`St`/`Ty`/`Da`/`Tr` = Id/Name/Status/Type/Date/Translations.

| Abbr | Meaning | Abbr | Meaning |
|---|---|---|---|
| `FiId` | FixtureId (long) | `MaId` | MarketId (long) |
| `SeId` | SelectionId (long) | `SpId` | SportId (int) |
| `SegId` | SegmentId (int) | `LeId` | LeagueId (int) |
| `ChTyId` | ChannelTypeId (BG/EM/OD) | `PrFiId` | ProviderFixtureId (string) |
| `PrMaId` | ProviderMarketId | `PrSeId` | ProviderSelectionId |
| `FiNa`/`SpNa`/`SegNa`/`LeNa`/`MaNa`/`SeNa` | …Name | `MaTeId` | MarketTemplateId |
| `SeTeId` | SelectionTemplateId | `FiStId`/`FiStNa` | FixtureStatus Id/Name |
| `FiStDa` | FixtureStartDate | `FiTyId`/`FiTyNa` | FixtureType Id/Name |
| `MaStId`/`MaStNa` | MarketStatus Id/Name | `CaStId`/`CaStNa` | CashoutStatus Id/Name |
| `SeStId` | SelectionStatus Id | `Od` | Odds (decimal) |
| `Pr`/`WoPr`/`LoPr`/`VoPr`/`HaWoPr`/`HaLoPr` | Probability / Won/Lost/Void/HalfWon/HalfLost Pr | `Li` | Line/handicap |
| `InPl` | InPlay (bool) | `IsAc` | IsActive |
| `Sc` | Score | `IsOfLi`/`IsOffLi` | IsOfferedLive |
| `HoId`/`AwId` | Home/Away id | `HoReCa`/`AwReCa` | Home/Away red cards |
| `CuPe`/`CuMi`/`CuSe` | Current period/minute/second | `CoId` | Correlation/Competition Id (context-dependent) |
| `TiSe`/`TiRe` | Timestamp Sent/Received (epoch) | `SoOr` | Sort order |
| `MaSp`/`SeSp`/`FiSp` | Market/Selection/Fixture Specifier | `VoRe` | Void reason |
| `IsSu` | bet-stop flag (Fixture doc) | `IsBeSo`/`BeSo` | IsBetStop / BetStop source key |
| `LiId`/`GeAl` | Livestream id / geo-alias (Fixture) | `ClCo`/`ClCoMa` | ClientCode(s) / ClientCodeMask |
| `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr`/`MaTeTr`/`SeNaTr` | translation lists (`TranslationMessage {LaCo, Tx}`); null/empty = unchanged or untranslated → fall back to `*Na` | `ClOd` | Client odds list |
| `DeOd`/`MaOd`/`HoOd`/`InOd` | Decimal/Malay/HongKong/Indo odds | `LaCo`/`Tx` | Language code / translated text (`TranslationMessage`) |

> When in doubt, open the collection/message `.cs` file — every field has a `[BsonElement]` or
> property name you can match here.

## 1. Data layer

### 1.1 MongoDB collections (`Data/Sportsbook/MongoDB/Collections/`)
| Collection | Purpose |
|---|---|
| `Fixture` | One doc per fixture — sport/segment/league refs, status, score, teams, bet-stop flag (`IsSu`), livestream (`LiId`/`GeAl`); `*NaTr` translation fields are written by **`OE.Translation.Service`** (update-only), never by Sportsbook |
| `Market` | One per market — template, status, line, cashout, client-code mask, sort order |
| `Selection` | One per selection/outcome — odds (`Od`), probabilities, status, correlation |
| `BettingStatus` | Global/keyed betting state; model id `"betstop"` holds global bet-stop |
| `SportLookup`/`SegmentLookup`/`LeagueLookup`/`MarketLookup`/`SelectionLookup` | Denormalized reference tables maintained by Job.Lookup |
| `MarketMapping`/`SelectionMapping` | Provider↔internal id mappings |
| `Projections/FixtureProject` | Projection DTO for lightweight fixture reads |

Indexes created at Sportsbook startup (`MongoIndexBootstrapper.cs`): unique `idx_fiid`/`idx_maid`/`idx_seid`;
compound `idx_fiid_mastid_mateid` on Market & Selection; `idx_maid` on Selection.

### 1.2 SQL Server contexts (EF Core 3.1)
| Context | DB | DbSets / tables | Used by |
|---|---|---|---|
| `TradingContext` | Trading | `ClientOddsFeedConfiguration`, `ClientOddsFeedLiveFixture` | UOF.Client |
| `TranslationContext` | Translation | `TranslationCache2` (StringId, SourceText, TargetTextZh/Es/Fr, Processed, HasBetPlacement) + `TranslationCache` (legacy, external, read-only) | Adapter.Service (startup preload), Translation.Service (read+write), Translation.Job (write) — **not** Sportsbook.Service |

## 2. Messages (the contract surface)

### Message-type constants (`Constants/MessageType.cs`)
- **BetGenius provider:** `OnProcessMessage`, `OnFixtureClose`, `OnMarketClose`, `OnMatchSummary`, `OnOtherMessage`.
- **Adapter (primary trading events):** `OnFixtureChange`, `OnMarketChange`, `OnMarketSettlement`,
  `OnMarketCancel`, `OnBetStopFixture`, `OnBetStopGlobal`, `OnHeartBeat`, `OnDynamicTranslationChange`,
  `OnPriceBoost`, `OnLeagueConfig`, `OnLeagueClientsChange` (BO-145).
- **Internal sportsbook:** `OnOddsChange`, `OnBetSettlement`, `OnBetCancellation`.

### `Messages/OutgoingAdapterMessages/`
| Message | Maps to | Notes |
|---|---|---|
| `OutgoingAdapterMessageBase` | — | base: `Id="{ChTyId}:{PrFiId}"`, `ChTyId`, `PrFiId`, `FiId`, `FiSp`, `TiSe`, `TiRe` |
| `OnFixtureChangeMessage` | ON_FIXTURE_CHANGE | extends base; sport/segment/league/status/score/teams + the `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr` translation lists |
| `OnMarketChangeMessage` | ON_MARKET_CHANGE | extends fixture-change; `Ma[]` → `MarketMessage` → `Se[]` `SelectionMessage` (+ `ClOd[]` client odds) |
| `OnMarketSettlementMessage` | ON_MARKET_SETTLEMENT | `Ma[]` MarketResult → `Se[]` SelectionResult (final status) |
| `OnMarketCancellationMessage` | ON_MARKET_CANCEL | `Ma[]` with `VoRe`, `StTi`/`EnTi` window |
| `OnBetStopFixtureMessage` | ON_BET_STOP_FIXTURE | `IsBetStop`, `SetActiveMarketsToInactive` |
| `OnBetStopGlobalMessage` | ON_BET_STOP_GLOBAL | `IsBetStop` |
| `OnBetSettlementMessage` | ON_BET_SETTLEMENT | `Selections[]` with `Result`, `VoidFactor`, `DeadHeatFactor` (full property names, not abbreviated) |
| `OnPriceBoostMessage` | ON_PRICE_BOOST | one client's boost on one selection: `ClientCode`, `ProviderFixtureId`, `MarketTemplateId`, `SelectionSpecifier`, `BoostedOdds` (0 removes it), `IsBoosted`, names and translations. Not derived from the base class: it is backoffice's JSON, full property names |
| `OnLeagueConfigMessage` | ON_LEAGUE_CONFIG | backoffice's league setting change: `ClientId`, `ClientCode`, `SportId`, `SegmentId`, `LeagueId`, `IsActive`. Only the ids are used. Backoffice's JSON, so a rename drops the field |
| `OnLeagueClientsChangeMessage` | ON_LEAGUE_CLIENTS_CHANGE | the clients allowed to see one league: `SportId`, `SegmentId`, `LeagueId`, `ClientCodes` (pipe-joined, the `ClCo` format, empty = nobody) |

### `Messages/OutgoingSportsbookMessages/`
| Message | Maps to | Notes |
|---|---|---|
| `OnInternalOddsChangeMessage` | ON_ODDS_CHANGE | canonical internal odds change (distinct from provider-facing OnMarketChange); `Ma[]` InternalMarket → InternalSelection |
| `OnDeleteCacheMessage` | (cache mgmt) | `ClientId`, `ClientCode`, `CacheType`, `CacheKey(s)`, `PurgeAll` |

## 3. Models (`Models/`)
- `ClientConfigurationModel` (SportId, PrematchOdds, LiveOdds, MaxFixtures) — UOF client config DTO.
- `ClientLiveFixtureModel`, `FixtureModel` — live-fixture tracking.
- `ClientOdds` — per-client multi-format odds (Decimal/Malaysian/Hongkong/Indonesian).
- `TranslationMessage` (`LaCo`, `Tx`) — the live wire type for every `*Tr` list.
- ⚠️ **Dead types, safe to delete:** `TranslationBase` (`LaCo`, `Va`) and `TranslationItem`
  (`StringId`, `TargetLanguageId`, `TargetText`) in `OE.Shared/Models`, plus the six
  `Outgoing*Translation` classes in `OE.Websocket.Service/Messages/OutgoingOddsChange.cs`
  (`OutgoingSportTranslation`, `OutgoingSegmentTranslation`, `OutgoingLeagueTranslation`,
  `OutgoingFixtureTranslation`, `OutgoingMarketTranslation`, `OutgoingSelectionTranslation`) — all
  left over from the pre-2026-07 shape, **zero references** in the solution. The live SignalR type is
  `OutgoingTranslation` (`LaCo`, `Tx`). Tracked as [TechnicalDebt.md](./TechnicalDebt.md) H7.
- `MessageBrokerModel`, `RabbitMQModel` (MessageRequest/ResponseDTO), `BaseResponseDTO<T>` (StatusCode/Message/Result).
- `SportModel` (SpId, FiSp, PrId).
- `Provider/BetGenius/`: `BGAuth` (IdToken/ExpiresIn), `BGEvent` (fixture + LiveStreams/VOD),
  `BGEvents` (paged, cursors) — shapes for the BetGenius **Video** API.

## 4. Enums (`Enums/`) — full value tables
| Enum | Values |
|---|---|
| `BetStatus` | Waiting=1000, Running=2000, Won=3000, HalfWon=3001, Lost=4000, HalfLost=4001, Void=5000, Rejected=6000, Cancelled=7000, CashedOut=8000 |
| `SettlementStatus` | Pending=0, Lost=1, Won=2, Void=3, HalfWon=4, HalfLost=5, Cancelled=6, CashedOut=7 |
| `FixtureStatus` | Prematch=0, Live=1, Suspended=2, Ended=3, Closed=4, Cancelled=5, Abandoned=6, Delayed=7, Unknown=8, Postponed=9, Interrupted=10, MatchAboutToStart=11, NotMapped=99 |
| `MarketStatus` | Cancelled=-4, Settled=-3, HandedOver=-2, Suspended=-1, Inactive=0, Active=1, NotMapped=99 |
| `CashoutStatus` | Closed=-2, Unavailable=-1, Available=1 |
| `FixtureType` | Match=0, Outright=1, Unknown=-1 |
| `FixtureChangeType` | Cancelled=3, LostCoverage=5, Other=101 |
| `VoidFactor` | Zero=0, Half=1, One=2 |
| `ChannelType` | BG=0, EM=1, OD=2 |
| `FavoriteSelection` | Home=0, Away=1, None=2 |
| `CacheType` | MarketSortOrder=0, SportSortOrder=1, UserFavorites=2, Maintenance=4, ParlayBonus=5, Client=6, DynamicTranslation=7 |
| `StatusCode` | 100=Success … 101–160 validation/business errors … 201–205 auth/maintenance … 300–303 odds-feed integration … 1000/1001 error. (See file for all ~90 codes.) |

> `Helper.GetVoidFactor` maps `BetStatus`/`SettlementStatus` → `VoidFactor`
> (Won/Lost→Zero, Void→One, Half*→Half) (`Utilities/Helper.cs:25-46`).

## 5. Providers (`Providers/`) — strategy/factory pattern
- `ILiveStreamProvider` — `ChannelType` + `GetLiveStream(fixtureId)` + `GetLiveStreams()`.
- `ILiveStreamFactory` / `LiveStreamFactory` — resolves a provider by `ChannelType` from the
  DI-injected `IEnumerable<ILiveStreamProvider>` (`LiveStreamFactory.cs:16-24`); throws
  `NotSupportedException` for unmapped channels.
- `BGLiveStream/BetGeniusLiveStream` — BG implementation: auth token + fixtures from the
  BetGenius Video API; maps HLS/DASH deliveries + device/region permissions; token & fixtures
  cached (~5h).

## 6. Repositories (`Repositories/`)
- `IBetGeniusRepository` / `BetGeniusRepository` — HTTP client for BetGenius auth + fixtures.
- `IRabbitMQRepository` / `RabbitMQRepository` — **HTTP** publish to a broker API
  (`v1/broker/publish`) returning `BaseResponseDTO<MessageResponseDTO>` (an alternative to the
  native `IMessageBroker`).

## 7. Utilities (`Utilities/`)
- `IMessageBroker` / `MessageBroker` — the RabbitMQ wrapper (connect, consume, publish, external
  publish, manual ack, trace propagation, retry/backoff, exclusive/priority queues, `BindQueue`,
  `CPublish` per-publish channel). The most important utility.
- `IMemCache` / `MemCache` — `System.Runtime.Caching.MemoryCache` wrapper (add/update/get/remove,
  prefix purge) used for translations & template caches.
- `IHelper` / `Helper` — `GetInstanceName` (sharding) + `GetVoidFactor`.
- `Extensions/`: `DateTimeExtension`, `IntExtensions`, `LoggerExtension` (`LogInformationEx`, etc.).
