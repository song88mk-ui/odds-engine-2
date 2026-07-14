# Common Services

Quick reference for the reusable services/abstractions in odds-engine-2 — "what do I use for X."
All live in **`OE.Shared`** unless noted. Deep dives: [../ImplementationPatterns.md](../ImplementationPatterns.md),
[../SharedLibraries.md](../SharedLibraries.md). All [Verified].

## Messaging
| Need | Use | Where | Notes |
|---|---|---|---|
| Consume/publish RabbitMQ | **`IMessageBroker` / `MessageBroker`** | `Utilities/MessageBroker.cs` | `Connect()`, `MessageReceived` event, `Publish/CPublish/ExternalPublish`, ack/nack, trace propagation, retry/backoff |
| Publish via HTTP bridge | **`IRabbitMQRepository` / `RabbitMQRepository`** | `Repositories/RabbitMQRepository.cs` | `POST v1/broker/publish` → `BaseResponseDTO<MessageResponseDTO>` |
| Message-type constants | **`MessageType`** | `Constants/MessageType.cs` | `ON_ODDS_CHANGE`, `ON_FIXTURE_CHANGE`, … |

**Publish cheat-sheet:** `broker.Publish(payload, MessageType.X, "r.oe...", correlationId)` — sets
`DeliveryMode=2`, `Type`, `CorrelationId`, OTel headers. Use `CPublish` from multi-threaded code;
`ExternalPublish` for the white-label vhost (`ext`, UOF.Client only).

## Caching
| Need | Use | Where | Notes |
|---|---|---|---|
| In-process cache | **`IMemCache` / `MemCache`** | `Utilities/MemCache.cs` | `MemoryCache.Default`; `AddOrUpdateCache` (int TTL = **hours**!), `GetCache<T>`, `DeleteCache`, `DeleteCacheByPrefix` |

## Helpers / pure logic
| Need | Use | Where |
|---|---|---|
| Fixture→shard instance | **`IHelper.GetInstanceName(providerFixtureId)`** | `Utilities/Helper.cs` |
| BetStatus/SettlementStatus → VoidFactor | **`Helper.GetVoidFactor(...)`** (static) | `Utilities/Helper.cs` |
| SettlementStatus(int) → BetStatus | **`int.GetBetStatus()`** (ext) | `Extensions/IntExtensions.cs` |
| Epoch ms ↔ DateTime | **`DateTime.ToEpochTimestamp()` / `long.ConvertToDateTime()`** | `Extensions/DateTimeExtension.cs` |
| Located structured log | **`logger.LogInformationEx/LogWarningEx/LogErrorEx`** | `Extensions/LoggerExtension.cs` |

## Providers (livestream)
| Need | Use | Where |
|---|---|---|
| Resolve a livestream provider by channel | **`ILiveStreamFactory.Resolve(channelType)`** | `Providers/LiveStreamFactory.cs` |
| Provider contract | **`ILiveStreamProvider`** (`ChannelType`, `GetLiveStream(s)`) | `Providers/ILiveStreamProvider.cs` |
| BetGenius impl | **`BetGeniusLiveStream`** | `Providers/BGLiveStream/` |
| BetGenius HTTP (auth+fixtures) | **`IBetGeniusRepository` / `BetGeniusRepository`** | `Repositories/BetGeniusRepository.cs` |

## Data access (per-service repositories)
| Service | Repository | Store |
|---|---|---|
| Sportsbook | `SportsbookRepository` (catalogue); `TranslationRepository` (SQL+cache) **— orphaned/uncalled as of 2026-06-30** | Mongo (SQL Translation no longer read) |
| UOF.Client | `TradingRepository` (SQL), `PlatformRepository` (HTTP), `ClientRepository` (odds math) | SQL Trading + Platform API |
| Adapter | `TradingRepository` (router — **no DB**) | — |
| Lookup/LiveStream/Settlement/Cleanup/Websocket | `SportsbookRepository` (each service's own) | Mongo |
> Naming caveat: the class name ≠ the store. See [../RepositoryPatterns.md](../RepositoryPatterns.md).

## Telemetry
| Need | Use | Where |
|---|---|---|
| ActivitySource/Meter + instruments | **`Instrumentation`** (per service) | `<Service>/Telemetry/Instrumentation.cs` |
| Register telemetry | **`services.AddTelemetry(new TelemetrySettings(...))`** | `Infra.Extensions.Telemetry` (NuGet) |

## DI registration extensions
| Need | Use | Where |
|---|---|---|
| Wire livestream providers+factory | **`services.AddProviders()`** | `OE.Job.LiveStream/Extensions/ServiceCollectionBettingExtensions.cs` |

## Not present (don't look for them)
No Redis client, no AutoMapper, no MediatR, no FluentValidation, no generic repository, no Options
pattern. See [../ImplementationPatterns.md](../ImplementationPatterns.md).
