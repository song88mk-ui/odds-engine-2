# Utilities & Extension Methods

Quick reference with signatures. All in `OE.Shared` unless noted. All [Verified].

## `Utilities/`
### `IHelper` / `Helper`
- `string GetInstanceName(string providerFixtureId)` — shard id `"00".."09"` from last 2 digits
  (throws if id < 2 chars). The sharding contract — see [../Architecture.md](../Architecture.md) ADR-003.
- `static VoidFactor GetVoidFactor(BetStatus status)` / `GetVoidFactor(SettlementStatus status)` —
  Won/Lost→Zero, Void→One, HalfWon/HalfLost→Half.

### `IMemCache` / `MemCache` (wraps `MemoryCache.Default`)
- `AddOrUpdateCache<T>(key, obj, int cacheTimeout)` — ⚠️ **TTL is in HOURS** (`AddHours`).
- `AddOrUpdateCache<T>(key, obj, DateTimeOffset date)` — absolute expiry.
- `T GetCache<T>(key)` · `T GetCache<T>(key, Func<T> factory, int expiration)` (read-through) ·
  `T GetCacheData<T>(key)` · `KeyValuePair<bool,long> GetCache(key)`.
- `bool Contains(key)` · `void DeleteCache(key)` · `void DeleteCacheByPrefix(prefix)` (O(n) scan) ·
  `List<string> GetExistingKeys(substring)`.

### `IMessageBroker` / `MessageBroker`
- `Connect()` / `ConnectWithManualAck()` / `ExternalConnect([model])` · `event MessageReceived`.
- `Publish[<T>](payload, messageType, routingKey, correlationId)` (shared channel) ·
  `CPublish[<T>](...)` (dedicated channel) · `ExternalPublish[<T>](...)` (vhost `ext`).
- `Acknowledge(e)` / `NAcknowledge(e)` · `IModel BindQueue(queue, options)` · `Dispose()`.

## `Extensions/`
### `LoggerExtensions` (file `LoggerExtension.cs`)
- `logger.LogInformationEx(msg, data?)` / `LogWarningEx(msg, data?)` / `LogErrorEx(msg, ex?, data?)`
  — auto-captures class/member/file:line; output `[Project.Class.Member (file:line)] msg | Data:{json}`.
  ⚠️ produces a single string — Serilog message-template properties are not preserved.

### `DateTimeExtension`
- `long DateTime.ToEpochTimestamp()` — ms since 1970 (tick math).
- `DateTime long.ConvertToDateTime()` — from unix ms.
- consts: `EpochTicks`, `TicksPeriod`, `TicksPeriodMs`.

### `IntExtensions`
- `BetStatus int.GetBetStatus()` — SettlementStatus→BetStatus switch (throws on undefined id).
- `bool int.IsVirtualSportId()` — ⚠️ stub (empty list + TODO).

## DI extensions
- `IServiceCollection.AddProviders()` — wires `ILiveStreamProvider`+`ILiveStreamFactory`+
  `IBetGeniusRepository`+`IMemCache` (`OE.Job.LiveStream/Extensions/ServiceCollectionBettingExtensions.cs`).
- `services.AddTelemetry(TelemetrySettings)` — from `Infra.Extensions.Telemetry` (NuGet).

## Constants
- `MessageType` (`Constants/MessageType.cs`) — all message-type string constants.

## Providers
- `ILiveStreamFactory.Resolve(int channelType)` → `ILiveStreamProvider`.
- `ILiveStreamProvider`: `ChannelType`, `Task<(bool,string)> GetLiveStream(fixtureId)`,
  `Task<List<string>> GetLiveStreams()`.
- `IBetGeniusRepository`: `GetBetGeniusAuthorizationToken()`, `GetBetGeniusFixture(token,id)`,
  `GetBetGeniusFixtures(token)`.

## Telemetry
- `Instrumentation` (per service): `ActivitySource`, counters (`ProcessedMessagesCounter` →
  `processed.messages`), histograms (`BulkWriteDurationHistogram` → `bulkwrite.duration`).
