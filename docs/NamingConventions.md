# Naming Conventions (observed)

Descriptive catalog of the naming actually used in odds-engine-2. All [Verified].

## Projects & assemblies
- Prefix **`OE.`** + area + kind: `OE.<Area>.Service` (workers), `OE.Job.<Name>` (jobs),
  `OE.Shared` (library), `OE.UOF.Client`, `OE.Websocket.Service`.
- ECR image names: **`oev2-<service-shortname>`** (e.g. `oev2-adapter-service`, `oev2-job-lookup`).
- NuGet package id: `OE.Shared`.

## Namespaces
- Match folder structure: `OE.Shared.Data.Sportsbook.MongoDB.Collections`,
  `OE.Shared.Messages.OutgoingAdapterMessages`, `OE.Sportsbook.Service.Repositories`,
  `<Project>.Telemetry`.
- backoffice-final reuses the **same `OE.Shared.Data.Sportsbook` namespaces** (inline copies) — a
  cross-repo naming coupling.

## Types
- Interfaces: **`I` prefix**, co-located with impl (`ISportsbookRepository`/`SportsbookRepository`).
- Workers: **`Worker`** (one per service). Composition root: **`Program`**.
- Telemetry: **`Instrumentation`** per service (`ActivitySourceName`/`MeterName` = the service name,
  e.g. `"OE.Sportsbook.Service"`).
- DI extensions: **`*Extensions`** static classes (`ServiceCollectionBettingExtensions`,
  `LoggerExtensions`). ⚠️ File vs class name can differ (`LoggerExtension.cs` → `LoggerExtensions`).
- Constants: **`static class`** of `public const` (`MessageType`, prefixes).
- Enums: PascalCase members with **explicit numeric values** (the values are the contract).

## Message / DTO naming
- **Inbound provider→adapter:** `On<Event>Message` (`OnFixtureChangeMessage`, `OnMarketChangeMessage`).
- **Internal sportsbook:** `OnInternal<Event>Message` (`OnInternalOddsChangeMessage`).
- **External (UOF.Client) wire models:** `OF<Thing>Message` (`OFFixtureMessage`, `OFMarketMessage`).
- **Websocket SignalR:** `Incoming<X>` / `Outgoing<X>` (`OutgoingOddsChange`, `IncomingBetStatus`).
- **Response envelope:** `BaseResponseDTO<T>`; request/response DTOs suffixed `…DTO`/`…Model`.
- **Message-type constants:** SCREAMING_SNAKE in code → PascalCase string value
  (`ON_ODDS_CHANGE = "OnOddsChange"`).

## Method naming idioms
- **`Process<MessageType>Message(...)`** — Worker→repository handlers.
- **`To<Target>(...)`** — build Mongo `WriteModel`s / map to documents (`ToFixture`, `ToDBBettingStatus`).
- **`Prepare<X>(...)`** — build outgoing messages (`PrepareInternalOddsChangeMessage`).
- **`Get<X>` / `Set<X>`** — reads / state setters (`GetActiveMarketIds`, `SetBetStopStatus`).
- **`<Verb>Ex`** — extension-method logging helpers (`LogInformationEx`).
- Broker publish variants: `Publish`, `CPublish` (dedicated-channel), `ExternalPublish`.

## Data field naming (the defining convention) — abbreviated codes
MongoDB documents and wire messages use terse **2–6 char abbreviations**: `<Entity><Attribute>` where
`Id/Na/St/Ty/Da/Tr/So Or` = Id/Name/Status/Type/Date/Translations/SortOrder. Examples: `FiId`
(FixtureId), `MaStId` (MarketStatusId), `SeNa` (SelectionName), `PrFiId` (ProviderFixtureId),
`Od` (Odds), `IsSu` (bet-stop), `ClCoMa` (client-code mask). **Full glossary:**
[SharedLibraries.md](./SharedLibraries.md). New persisted/wire fields **must** follow this.
- Note: newer message types use **full names** (`OnBetSettlementMessage.HomeScore`,
  `ProviderFixtureId`) — an inconsistency, but the abbreviated convention dominates the catalogue.

## RabbitMQ routing keys
- Lowercase, dot-delimited, hierarchical: `r.<system>.<area>.<event>[.<shard|client>]`
  (`r.oe.sb.engine.onoddschange`, `r.oe.adapter.onmarketchange.07`, `r.bet.betstatus.s88`).
- Error sinks: `r.error.<domain>.<service>` (`r.error.oe.adapter`, `r.error.oe.translation`, `r.error.oe.translation.job`, `r.error.engine.uof.client`); never under a prefix another service binds with `#` (GM-960). External feed: `uof.*` (vhost `ext`).
- Exchange: `e.central`. Queues: `q.<purpose>` (`q.sb.engine`, `q.oe.job.lookup`). Consumer tags:
  the service name + GUID.

## Config keys
- Sections: `MessageBroker`, `ExternalMessageBroker`, `Serilog`, `Telemetry`, `ConnectionStrings`,
  and a catch-all **`Custom:*`** for service-specific settings. Env override uses `__` (e.g.
  `MessageBroker__Host`); SQL conn strings under `ConnectionStrings:` or `Custom:Trading`.

## Telemetry naming
- ActivitySource/Meter = the service name; activities `"{routingKey} receive|send"`; metrics
  lowercase dotted (`processed.messages`, `forwarded.messages`, `bulkwrite.duration`); tags dotted
  (`message.type`, `client.code`, `correlation.id`).

## Cache keys
- Colon-delimited prefixes: `sb:translation:{Prefix:Id}`, `bg:auth`, `bg:fixture:{id}`, `mtpp`.

## Known naming inconsistencies (documented, not corrected)
- File/class mismatches (`LoggerExtension.cs` → `LoggerExtensions`).
- Repository classes that misdescribe their store (`TradingRepository` as router/HTTP).
- Folder `Repository/` vs `Repositories/` across services.
- Mixed abbreviated vs full field names across message generations.
- A typo'd routing key `r.bg.receiver.onfixturecchange` (Settlement).
