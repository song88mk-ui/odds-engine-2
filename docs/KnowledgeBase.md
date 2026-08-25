# Knowledge Base

The practical "what you need to know to work here" reference. Start with [README.md](./README.md)
for the doc map; this page is the cheat-sheet.

## Mental model in 5 bullets
1. **It's a RabbitMQ pipeline of headless .NET workers.** No HTTP APIs inside OEv2; services talk
   only via the `e.central` topic exchange.
2. **The source is bet-genius** (Genius Sports feed) and [Likely] an `oddsfeed`/UOF source; the
   sinks are **platform-final → platform-web**.
3. **Sportsbook.Service is the heart**, sharded 11 ways by provider-fixture-id digits (00–09 +
   global shard 10). Same fixture → same instance, always.
4. **MongoDB `Sportsbook` is the catalogue OEv2 owns**; platform-final reads it. SQL Server holds
   translations & trading config.
5. **Everything important is in `OE.Shared`** — contracts, models, enums, the broker, the
   sharding helper. Changing it ripples cross-repo.

## "Where do I look for…" index
| I want to… | Go to |
|---|---|
| Understand a message type | `OE.Shared/Constants/MessageType.cs` + `OE.Shared/Messages/*` ([SharedLibraries.md](./SharedLibraries.md)) |
| Decode an abbreviated field (`MaStId`?) | Glossary in [SharedLibraries.md](./SharedLibraries.md) |
| Trace which service handles a routing key | Routing-key table in [ConfigurationOverview.md](./ConfigurationOverview.md) |
| Change RabbitMQ behavior | `OE.Shared/Utilities/MessageBroker.cs` |
| Understand sharding | `OE.Shared/Utilities/Helper.cs` + ADR-003 ([ArchitectureDecisions.md](./ArchitectureDecisions.md)) |
| Find prod config (not appsettings!) | `argocd/apps/oddsengine/<service>/<env>.yaml` |
| Know what a service does | [ProjectOverview.md](./ProjectOverview.md) |
| See cross-repo contracts | [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md) |
| Find a known bug/risk | [TechnicalDebt.md](./TechnicalDebt.md) |
| Find an open question | [Unknowns.md](./Unknowns.md) |

## Glossary of domain terms
| Term | Meaning |
|---|---|
| **OEv2** | odds-engine-2 (this repo); v1 is the obsolete `oddsengine` sibling |
| **Fixture** | a match/event (or an outright). MongoDB `Fixture`. |
| **Market** | a betting market on a fixture (e.g. 1X2, O/U). `MaTeId` = market *template* (type). |
| **Selection** | an outcome within a market (e.g. Home/Draw/Away). `SeTeId` = selection template. |
| **Segment** | region/category grouping under a sport (`SegId`). |
| **League** | competition under a segment (`LeId`). |
| **Provider id (`PrFiId`/`PrMaId`/`PrSeId`)** | the upstream feed's ids; `FiId/MaId/SeId` are internal. |
| **Specifier (`FiSp`/`MaSp`/`SeSp`)** | provider-format key string. |
| **Bet-stop** | suspend betting — per-fixture or global (shard 10). |
| **Cashout** | settle a bet early at an offered value. |
| **UOF / Unified Odds Feed** | the outbound white-label feed (UOF.Client → external broker). [Likely] |
| **Instance / shard (`NN`)** | 00–10 Sportsbook partition from `GetInstanceName`. |
| **Channel type** | feed provider: BG=0 (BetGenius), EM=1, OD=2. |
| **Translation** | localized name; SQL `SportsbookDynamic`. (Corrected 2026-06-30: translations removed from pipeline — OEv2 no longer reads this into `sb:translation:*`; the read path is orphaned. UI localizes via platform-web REST `GetTranslations()`.) |

## Reusable patterns to copy (the "good" ones)
- **New worker:** clone a service folder (Program/Worker/Repository/Telemetry/appsettings/Dockerfile).
- **Consume a new routing key:** add it to the service's `MessageBroker:RoutingKey` (pipe-separated)
  and add a `case` in the `MessageReceived` dispatch on `BasicProperties.Type`.
- **Publish a message:** `Publish<T>(payload, MessageType.X, "r.oe...", correlationId)` — sets
  persistence + trace headers for you. Use `CPublish` from multi-threaded code.
- **Idempotent Mongo write:** unordered `BulkWrite` of upserts keyed on `FiId`/`MaId`/`SeId`.
- **Cache a SQL lookup:** MemCache with TTL **and negative-caching** for misses; invalidate via a
  cache message.
- **Add a metric:** declare the instrument in `Telemetry/Instrumentation.cs`, increment in the handler.

## Gotchas / footguns
- ⚠️ **Committed `appsettings.json` ≠ prod config.** Prod is argocd env + AWS secrets. Don't trust
  the `localhost`/credential values.
- ⚠️ **`OE.Job.KeepAlive` isn't in the `.sln`** and **`OE.API` is dead.** Build via CI/dir, not the
  solution.
- ⚠️ **Sharding math couples Adapter + Sportsbook + argocd instance count.** Change all three together.
- ⚠️ **Abbreviated fields are easy to misread** (`Pr`=probability vs `PrFiId`=provider id; `CoId`=
  correlation *or* competition depending on message). Check the type.
- ⚠️ **Singletons must be thread-safe** — some aren't (bet-stop flag, monthly-reset lists).
- ⚠️ **Lots of commented-out code** = in-progress/disabled features, not necessarily safe to delete.
- ⚠️ **`OE.Shared` edits are cross-repo breaking changes** (bet-genius, platform-final, platform-web).
- ⚠️ **EF Core 3.1** here — modern EF APIs/behaviors don't apply.

## Tech stack snapshot
.NET 10 workers · netstandard2.1 shared lib · RabbitMQ (topic, manual ack, OTel-propagated) ·
MongoDB (catalogue) · SQL Server + EF Core 3.1 (translations/trading) · SignalR client (push) ·
Serilog (compact JSON) · OpenTelemetry → Tempo · Docker → AWS ECR → EKS via argocd GitOps.

## Onboarding path (suggested reading order)
1. [Architecture.md](./Architecture.md) — the pipeline + sharding.
2. [SharedLibraries.md](./SharedLibraries.md) — contracts + glossary (keep open as a reference).
3. [ProjectOverview.md](./ProjectOverview.md) — what each service does.
4. [ConfigurationOverview.md](./ConfigurationOverview.md) — routing keys + how config resolves.
5. [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md) — the seams.
6. Skim [TechnicalDebt.md](./TechnicalDebt.md) + [Unknowns.md](./Unknowns.md) before changing anything.
