# Common Models

Reference for shared domain models, persistence entities, and enums. Full schemas + glossary:
[../SharedLibraries.md](../SharedLibraries.md); domain semantics: [../DomainModel.md](../DomainModel.md).
All [Verified].

## Domain / value models (`OE.Shared/Models/`)
| Model | Fields | Use |
|---|---|---|
| `ClientConfigurationModel` | `SportId`,`PrematchOdds`,`LiveOdds`,`MaxFixtures?` | UOF client feed config (DTO) |
| `ClientLiveFixtureModel` | `ClientId`,`SportId`,`Fixtures[]` | client live-fixture tracking |
| `FixtureModel` | `FixtureSpecifier`,`FixtureStartDate` | lightweight fixture ref |
| `ClientOdds` | `ClientCode`,`DecimalOdds`,`MalaysianOdds`,`HongkongOdds`,`IndonesianOdds`,`SelectionTemplateId`,`SelectionId` | per-client multi-format odds |
| `TranslationMessage` | `LaCo`,`Tx` | **the** embedded translation type on every `*Tr` list |
| `TranslationCache2` | `StringId`,`SourceText`,`TargetTextZh/Es/Fr`,`Processed`,`HasBetPlacement` | the SQL translation store (EF entity) |
| `LegacyTranslationCache` | `SourceHash`,`SourceLanguage`,`TargetLanguage`,`TargetText` | external legacy table, read-only, temporary `CacheBackfill` bridge only |
| ~~`TranslationBase`~~ / ~~`TranslationItem`~~ | — | **dead — zero references**; delete (see [TechnicalDebt.md](../TechnicalDebt.md) H7) |
| `SportModel` | `SpId`,`FiSp`,`PrId` | sport ref + provider mapping |

## MongoDB collection models (`Data/Sportsbook/MongoDB/Collections/`)
`Fixture`, `Market`, `Selection`, `BettingStatus`, `SportLookup`, `SegmentLookup`, `LeagueLookup`,
`MarketLookup`, `SelectionLookup`, `MarketMapping`, `SelectionMapping`; projection `FixtureProject`,
`MarketStatusProjection`. **Abbreviated fields** — glossary in [../SharedLibraries.md](../SharedLibraries.md).
Embedded sub-docs: `*TranslationMDB`, `PeriodScoreMDB`, `ClientBoostedSelectionName*MDB`.

## SQL entities
| Context | Entities | DB |
|---|---|---|
| `TradingContext` | `ClientOddsFeedConfiguration` (PK `{ClientId,SportId}`), `ClientOddsFeedLiveFixture` (PK `{ClientId,SportId,FixtureSpecifier}`) | Trading |
| `TranslationContext` | `TranslationCache2`, `TranslationCache` (legacy) | Translation — Adapter (preload), Translation.Service (read+write), Translation.Job (write) |

## Enums (`OE.Shared/Enums/`) — values are the contract
| Enum | Values (abbrev) |
|---|---|
| `FixtureStatus` | Prematch=0,Live=1,Suspended=2,Ended=3,Closed=4,Cancelled=5,Abandoned=6,Delayed=7,Unknown=8,Postponed=9,Interrupted=10,MatchAboutToStart=11,NotMapped=99 |
| `MarketStatus` | Cancelled=-4,Settled=-3,HandedOver=-2,Suspended=-1,Inactive=0,Active=1,NotMapped=99 |
| `SelectionStatus`* | via `SeStId` (settlement-driven) — see `SettlementStatus` |
| `SettlementStatus` | Pending=0,Lost=1,Won=2,Void=3,HalfWon=4,HalfLost=5,Cancelled=6,CashedOut=7 |
| `BetStatus` | Waiting=1000,Running=2000,Won=3000,HalfWon=3001,Lost=4000,HalfLost=4001,Void=5000,Rejected=6000,Cancelled=7000,CashedOut=8000 |
| `CashoutStatus` | Closed=-2,Unavailable=-1,Available=1 |
| `FixtureType` | Match=0,Outright=1,Unknown=-1 |
| `FixtureChangeType` | Cancelled=3,LostCoverage=5,Other=101 |
| `VoidFactor` | Zero=0,Half=1,One=2 |
| `ChannelType` | BG=0,EM=1,OD=2 |
| `FavoriteSelection` | Home=0,Away=1,None=2 |
| `CacheType` | MarketSortOrder=0,SportSortOrder=1,UserFavorites=2,Maintenance=4,ParlayBonus=5,Client=6,DynamicTranslation=7 |
| `StatusCode` | 100=Success; 101–160 validation/business; 201–205 auth/maintenance; 300–303 odds-feed; 1000/1001 error (~90 codes) |

## Status-mapping helpers
- `int.GetBetStatus()` — `SettlementStatus` → `BetStatus` (IntExtensions).
- `Helper.GetVoidFactor(BetStatus|SettlementStatus)` — → `VoidFactor` (Won/Lost→Zero, Void→One, Half*→Half).
- `ToBetStatus(bool result, VoidFactor?)` — settlement result → `BetStatus` (SportsbookRepository).
