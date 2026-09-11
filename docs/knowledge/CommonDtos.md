# Common DTOs

Quick reference for shared data-transfer contracts. Full field lists:
[../SharedLibraries.md](../SharedLibraries.md) §2–3; contract governance: [../Contracts.md](../Contracts.md).
All in `OE.Shared` unless noted. All [Verified].

## Envelope & broker DTOs
| DTO | Fields | Use |
|---|---|---|
| `BaseResponseDTO<T>` | `StatusCode` (enum), `Message`, `Result` (T) | every API response wrapper |
| `MessageRequestDTO` | `Payload`, `MessageType`, `RoutingKey`, `CorrelationId` | HTTP publish bridge request |
| `MessageResponseDTO` | `IsSuccess` | HTTP publish bridge response |
| `MessageBrokerModel` | `Host/Port/Username/Password/VirtualHost/Exchange/Queue/Consumer/RoutingKey/Environment` | programmatic broker config |

## Inbound adapter message DTOs (`Messages/OutgoingAdapterMessages/`)
| DTO | MessageType | Key shape |
|---|---|---|
| `OutgoingAdapterMessageBase` | — | `Id="{ChTyId}:{PrFiId}"`, `ChTyId`,`PrFiId`,`FiId`,`FiSp`,`TiSe`,`TiRe` |
| `OnFixtureChangeMessage` | `OnFixtureChange` | + sport/segment/league, status, score, teams, and the `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr` translation lists (`TranslationMessage {LaCo, Tx}`) |
| `OnMarketChangeMessage` | `OnMarketChange` | + `Ma[]` MarketMessage → `Se[]` SelectionMessage (+ `ClOd[]`) |
| `OnMarketSettlementMessage` | `OnMarketSettlement` | `Ma[]` MarketResult → `Se[]` SelectionResult |
| `OnMarketCancellationMessage` | `OnMarketCancel` | `Ma[]` w/ `VoRe`,`StTi`,`EnTi` |
| `OnBetStopFixtureMessage` | `OnBetStopFixture` | `IsBetStop`, `SetActiveMarketsToInactive` |
| `OnBetStopGlobalMessage` | `OnBetStopGlobal` | `IsBetStop` |
| `OnBetSettlementMessage` | `OnBetSettlement` | `Selections[]` w/ `Result`,`VoidFactor`,`DeadHeatFactor` (full-word names) |

## Internal sportsbook message DTOs (`Messages/OutgoingSportsbookMessages/`)
| DTO | MessageType | Use |
|---|---|---|
| `OnInternalOddsChangeMessage` | `OnOddsChange` | canonical odds change Sportsbook→Websocket; `Ma[]` Internal market→selection |
| `OnDeleteCacheMessage` | (cache mgmt) | `ClientId`,`ClientCode`,`CacheType`,`CacheKey(s)`,`PurgeAll` |

## External UOF wire DTOs (`OE.UOF.Client/Models/ExternalMessage/`)
`OFFixtureMessage` (a.k.a. OFOddsChange), `OFMarketMessage`, `OFSelectionMessage`,
`OFHeartbeatMessage`, `OFTranslation` (per-entity), `PeriodScoreMessage`, `ReferenceMessage`. These
are the **outbound** white-label shapes (decimal/Malay/HK/Indo odds). Published to vhost `ext`.

## SignalR message DTOs (`OE.Websocket.Service/Messages/`)
Incoming: `IncomingBetStatus`, `IncomingCashOutChange`, `IncomingPayableChange`,
`IncomingVisitFavorites`, `IncomingVisitBoostedFixtures`, `IncomingWalletChange`.
Outgoing: `OutgoingOddsChange`, `OutgoingBetStatus`, `OutgoingCashOutChange`, `OutgoingWalletChange`,
`OutgoingPayableChange`, `OutgoingBetStopFixture`, `OutgoingBetStopGlobal`, `OutgoingBoostedOddsChange`.
> These mirror platform-web's `EngineMessages/*`. **MessagePack contractless → property rename =
> breaking.** See [../WebSocket.md](../WebSocket.md).

## UOF DTOs (`OE.UOF.Client/Models/DTOs/`)
`BaseResponseDTO`, `MarketTemplatePlayerPropsResponseDTO` (`SportId`,`FixtureStatusId`,`MarketTemplates`).

## BetGenius provider DTOs (`Models/Provider/BetGenius/`)
`BGAuth` (`IdToken`,`ExpiresIn`), `BGEvent` (fixture + LiveStreams), `BGEvents` (paged, cursors).

> Change-risk: editing any of these is a cross-repo break (bet-genius / platform / backoffice). See
> [../Contracts.md](../Contracts.md) and [../CrossRepositoryDependencies.md](../CrossRepositoryDependencies.md).
