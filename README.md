# OddsEngine (odds-engine-2 / OEv2)

Odds Engine, originally based on the Bet Genius odds feed provider integration.

An event-driven pipeline of headless .NET worker services that:

- consume normalized odds / fixture / settlement / translation messages from RabbitMQ
  (topic exchange `e.central`),
- persist a sportsbook catalogue to MongoDB (SQL Server for translations and betting data),
- push real-time updates to the platform frontend (`platform-web`) by acting as a
  **SignalR client** against the `/platform` hub, using the MessagePack protocol, and
- re-publish an external white-label Unified Odds Feed (UOF) to a second RabbitMQ vhost.

There is **no UI and no inbound HTTP** in this repository. Every runnable project is a
console worker (`Host.CreateDefaultBuilder` + `BackgroundService`); none of them listen on
a port. All connections are outbound (RabbitMQ, MongoDB, SQL Server, the SignalR hub, and
the Platform API).

For deep documentation see `docs/README.md` (architecture, contracts, configuration,
technical debt) and `CLAUDE.md` / `AGENTS.md` (working rules for this repo).

## Repository layout

```
src/OddsEngine/               OddsEngine.sln + all projects
  OE.Shared/                  Shared kernel (netstandard2.1): MessageBroker, MemCache,
                              Helper (sharding), message DTOs, Mongo models. Cross-repo
                              wire contract - do not change casually.
  OE.Adapter.Service/         Ingests feed messages (r.bg.receiver.*, r.uof.#), routes each
                              fixture to a Sportsbook shard via Helper.GetInstanceName
  OE.Sportsbook.Service/      Catalogue writer: MongoDB upserts + publishes r.oe.sb.engine.*
  OE.Websocket.Service/       SignalR client: consumes r.oe.sb.engine.* / r.bet.* /
                              r.wallet.* and calls Publish* methods on the /platform hub
  OE.UOF.Client/              External white-label UOF egress (second RabbitMQ vhost "ext")
  OE.Translation.Service/     Translation processing (SQL Server "Translation" DB)
  OE.Translation.Job/         Translation maintenance/cleanup job
  OE.Job.Lookup/              Maintains *Lookup collections in MongoDB
  OE.Job.LiveStream/          BetGenius video/live-stream metadata job
  OE.Job.Settlement/          Closes stale fixtures/markets
  OE.Job.Cleanup/             Purges old data (MongoDB + SQL "Betting" DB)
  OE.Job.KeepAlive/           Heartbeat publisher (NOT in the .sln - build separately)
  OE.API/                     Dead project - no code, not in the .sln; ignore
  OE.*.Tests, OE.Shared.Test/ xUnit test projects (in the .sln)
docs/                         Generated knowledge base (start at docs/README.md)
```

Target frameworks: all runnable projects (services, jobs, tests) are `net10.0`;
`OE.Shared` is `netstandard2.1`.

## Prerequisites

- .NET 10 SDK (builds everything; older SDKs cannot build the `net10.0` projects).
- RabbitMQ reachable from your machine. Services declare the topic exchange `e.central`
  and their own queues/bindings at startup, so a plain broker is enough. Port `5672` is
  used as-is; setting `MessageBroker:Port` to `5671` automatically enables TLS
  (`OE.Shared/Utilities/MessageBroker.cs`).
- MongoDB for the `sportsbook` database (needed by Sportsbook, Websocket, Lookup,
  LiveStream, Settlement, Cleanup, Translation.Service).
- SQL Server for the `Translation` database (Adapter, Translation.Service,
  Translation.Job) and the `Betting` database (Job.Cleanup).
- Access to the private GitHub Packages NuGet feed
  `https://nuget.pkg.github.com/FlyingCockatoo/index.json` - the services reference the
  `Infra.Extensions.Telemetry` package, which is restored from that feed in the
  Dockerfiles (`dotnet nuget add source ... --password $NUGET_PASSWORD`). If restore
  fails on that package locally, add the same source with your own GitHub token.

Example local infrastructure (suggested; not repo-provided - there is no docker-compose
in this repository):

```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
docker run -d --name mongo -p 27017:27017 mongo
docker run -d --name sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD=<your-password> \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

## Quick start (concrete example)

Goal: start real services against a local RabbitMQ and MongoDB and *see* them working.
This repo is headless, so "working" means: the services connect, declare their
exchange/queues, and you can watch message activity in the RabbitMQ management UI at
`http://localhost:15672` plus structured Serilog console logs. No SQL Server is needed
for this quick start (neither service below touches it).

### 1. Start local infrastructure

```bash
docker run -d --name rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
docker run -d --name mongo -p 27017:27017 mongo
```

The management image creates the default `guest` / `guest` user. Log in at
`http://localhost:15672` with those credentials.

### 2. Run OE.Job.KeepAlive (smallest service: RabbitMQ only)

`OE.Job.KeepAlive` is the least-infrastructure service in the repo: it needs only
RabbitMQ, and its committed `appsettings.json` already points at
`localhost` / `guest` / `guest` / vhost `/` with no port key (so it uses 5672, non-TLS),
so **no overrides are needed**. It publishes an `OnHeartbeat` message with routing key
`r.oe.uof.heartbeat` to `e.central` every 5 seconds (`OE.Job.KeepAlive/Worker.cs`).

One manual step first: KeepAlive does **not** declare the exchange. `MessageBroker.Connect()`
only calls `ExchangeDeclare` when `MessageBroker:Queue` is non-empty
(`OE.Shared/Utilities/MessageBroker.cs`), and KeepAlive's `Queue` is `""`. Publishing to a
missing exchange kills the channel, so create `e.central` yourself (skip this if you start
a consumer service such as Sportsbook first - see step 3 - because consumers declare it):

- Management UI: Exchanges tab > Add a new exchange > Name `e.central`, Type `topic`,
  Durability `Durable` > Add exchange.
- Or from the CLI:

```bash
docker exec rabbitmq rabbitmqadmin declare exchange name=e.central type=topic durable=true
```

Then run it (it is not in the .sln, so run the project directly; PowerShell):

```powershell
cd src/OddsEngine/OE.Job.KeepAlive
dotnet run
```

Success looks like (console):

```
[INF] Connecting to RabbitMQ Server
[INF] Connecting to RabbitMQ on ["localhost":5672]; TlsEnabled: False
[INF] Connected, running...
[INF] Running Heartbeat publish loop()
```

In the management UI, open Exchanges > `e.central`: the "Message rates" graph shows a
steady publish-in rate of about 0.2 msg/s (one heartbeat every 5 s). Note: no queue is
bound to `r.oe.uof.heartbeat` in this setup, so the broker drops each heartbeat after the
exchange - you see activity on the exchange graph, not in any queue. That is expected.

### 3. Run OE.Sportsbook.Service (RabbitMQ + MongoDB, declares its own queue)

This is the smallest service that both *consumes* (visible queue + consumer in the UI)
and *publishes*. It needs RabbitMQ and MongoDB. Its committed `appsettings.json` points
at shared staging infrastructure, so override host/credentials (do not reuse the
committed values). PowerShell, each on its own line:

```powershell
cd src/OddsEngine/OE.Sportsbook.Service
$env:MessageBroker__Host = "localhost"
$env:MessageBroker__Port = "5672"
$env:MessageBroker__Username = "guest"
$env:MessageBroker__Password = "guest"
${env:Custom__MongoDB-Sportsbook} = "mongodb://localhost:27017/sportsbook"
dotnet run
```

Notes on those exact names:

- `__` is the .NET config section separator, so `MessageBroker__Host` overrides
  `MessageBroker:Host` (read in `OE.Shared/Utilities/MessageBroker.cs`) and
  `Custom__MongoDB-Sportsbook` overrides `Custom:MongoDB-Sportsbook` (read in
  `OE.Sportsbook.Service/Program.cs`).
- The Mongo key contains a hyphen, so PowerShell needs the `${env:...}` brace syntax for
  that one variable.
- Overriding `Port` to `5672` matters: the committed value is `5671`, which auto-enables
  TLS in `MessageBroker.cs`.
- `VirtualHost` (`/`), `Exchange` (`e.central`), `Queue` (`q.sb.engine`), and
  `RoutingKey` (`r.oe.adapter.#.11`) are already correct in the committed file and need
  no override.

MongoDB must be up **before** the service starts: `Program.Main` creates indexes on the
`Sportsbook` database (`Fixture`, `Market`, `Selection` collections) via
`MongoIndexBootstrapper` before the worker runs.

Success looks like (console):

```
[INF] Prefetch SportsBook data
[INF] Connecting to RabbitMQ Server
[INF] Connecting to RabbitMQ on ["localhost":5672]; TlsEnabled: False
[INF] Connected. Processing Incoming messages
```

In the management UI you should now see, all declared by the service at startup:

- Exchanges: `e.central` (topic, durable),
- Queues: `q.sb.engine` (durable) with 1 consumer named `OE.Sportsbook.Service.<guid>`,
- Bindings on `q.sb.engine`: routing key `r.oe.adapter.#.11` from `e.central`.

In MongoDB (`mongosh`): `use Sportsbook` then `db.Fixture.getIndexes()` shows the
bootstrapped indexes.

### 4. Push a test message through the pipeline

There is no upstream feed on a dev machine, so hand-publish one message from the
management UI: Exchanges > `e.central` > Publish message:

- Routing key: `r.oe.adapter.test.11` (must match the `r.oe.adapter.#.11` binding),
- Properties: `type` = `OnFixtureChange` (the worker dispatches on the AMQP `type`
  property using the constants in `OE.Shared/Constants/MessageType.cs`),
- Payload: an `OnFixtureChangeMessage` JSON body (see
  `OE.Shared/Messages/OutgoingAdapterMessages/`).

You will see the delivery on the `q.sb.engine` message-rate graph, and in the service
log either `FixtureId:... OnFixtureChange ... sending odds change` followed by a publish
of `r.oe.sb.engine.onoddschange` back to `e.central`, or - if the payload is incomplete -
`Error processing message: ...` with the payload echoed. Either way the consume path is
visibly working. (Whether a minimal `{}` payload processes cleanly end-to-end has not
been verified; expect the error log in that case.)

### What this quick start cannot show

- Real odds data: the BetGenius / UOF feed receivers live in sibling repos, so nothing
  arrives on `e.central` unless you publish it yourself or run those receivers.
- `OE.Websocket.Service` end-to-end delivery: the SignalR `/platform` hub host is not in
  this repo; without a hub listening at `Custom:WebSocketAPI` the service cannot deliver.
- SQL-backed services (`OE.Adapter.Service`, `OE.Translation.*`, `OE.Job.Cleanup`): the
  `Translation` / `Betting` schemas are not in this repo, so a fresh local SQL Server is
  not enough. Start SQL Server only when you have a schema to restore.
- Confirmed Mongo writes: the connection string convention uses `w=0` (unacknowledged),
  so write failures are silent; check collections manually with `mongosh`.

Still observable regardless: connections and channels in the management UI Overview,
exchange/queue/binding declarations, the heartbeat publish rate, hand-published message
consumption, and structured Serilog console logs from every service.

## Configuration

Each service has its own `appsettings.json`. Configuration is read imperatively via
`IConfiguration` (no Options pattern).

**Warning: several committed `appsettings.json` files contain real hostnames and
credentials for shared staging infrastructure (AWS MQ, RDS, an internal MongoDB IP).
Running a service without overriding them will connect you to that shared environment.
Always override locally; never commit new secrets.** Production does not use these files
at all - it is configured via env vars and AWS Secrets Manager through argocd
(see `docs/Configuration.md`).

Local override options:

- `appsettings.Development.json` next to the service's `appsettings.json` (git-ignored,
  see `.gitignore`). Loaded only when the environment is `Development`, so set
  `DOTNET_ENVIRONMENT=Development` when using `dotnet run`.
- Environment variables with `__` as the section separator, e.g.
  `MessageBroker__Host=localhost`.

Key sections (names only - values differ per service; see each service's
`appsettings.json` for the exact set):

| Section | Keys | Used by |
|---|---|---|
| `MessageBroker` | `Host`, `Port`, `Username`, `Password`, `VirtualHost`, `Exchange`, `Queue`, `Consumer`, `RoutingKey`, `IsPriority`, `IsExclusive` | all message-driven services |
| `ExternalMessageBroker` | same shape, vhost `ext` | `OE.UOF.Client` |
| `Custom` | `MongoDB-Sportsbook` (Mongo connection string), `DbContextTimeout`, plus per-service keys such as `APIKey`, `WebSocketAPI`, `PlatformAPI`, `AsianClientCodes`, `UseMessagePack`, `IsOEEnabled`, `InternalAPIKey` (Websocket; multi-tenant since GM-720 — clients are discovered from the Platform API `v1/clients`; run exactly one instance) and `BetGenius:{Uri,Env,User,Password,Key}` (LiveStream) | varies |
| `ConnectionStrings` | `Translation` (Adapter, Translation.*), `Betting` (Job.Cleanup) | SQL-backed services |
| `Serilog` | console sink config | all |
| `Telemetry` | `Enabled`, `Environment` (OpenTelemetry) | most |

Notes verified in code/config:

- Sharding: `OE.Adapter.Service` routes every fixture to a fixed Sportsbook instance via
  `Helper.GetInstanceName(providerFixtureId)` (`OE.Shared/Utilities/Helper.cs`), and each
  Sportsbook instance binds a matching `r.oe.adapter.#.<NN>` routing key
  (`MessageBroker:RoutingKey`). Instance count is coupled to deployment config - see
  `docs/Architecture.md` before touching it.
- MongoDB writes use `w=0` (unacknowledged) in the committed connection string - write
  failures are silent.
- SignalR uses MessagePack with `ContractlessStandardResolver` (name-keyed) when
  `Custom:UseMessagePack` is true (`OE.Websocket.Service/Worker.cs`). Renaming a property
  on any outgoing message model is a breaking wire change against `platform-web`.

## Running locally

Build everything first:

```bash
dotnet build src/OddsEngine/OddsEngine.sln
# OE.Job.KeepAlive is not in the solution:
dotnet build src/OddsEngine/OE.Job.KeepAlive
```

Then run each service you need from its project folder (they are independent workers -
start only what your scenario requires):

```bash
cd src/OddsEngine/OE.Sportsbook.Service
DOTNET_ENVIRONMENT=Development dotnet run
```

(PowerShell: `$env:DOTNET_ENVIRONMENT = 'Development'; dotnet run`.)

Minimal real-time odds path:

1. Infrastructure: RabbitMQ + MongoDB running, config overridden to point at them.
2. `OE.Adapter.Service` - consumes feed messages from `e.central`
   (routing keys `r.bg.receiver.onbetstopfixture | r.uof.# | r.oe.ontranslationchange`)
   and republishes as `r.oe.adapter.<type>.<NN>`.
3. `OE.Sportsbook.Service` - persists the catalogue and publishes `r.oe.sb.engine.*`.
4. `OE.Websocket.Service` - consumes `r.oe.sb.engine.*` and pushes to the SignalR hub at
   `Custom:WebSocketAPI` (committed dev value: `http://localhost:3001/platform`).

Because there is no live upstream feed on a dev machine, driving the pipeline locally
means publishing test messages to `e.central` yourself (RabbitMQ management UI on
`http://localhost:15672`, or a small publisher script) with the routing keys above, or
running the upstream feed receiver from its own repository.

### How a local platform-web connects

`OE.Websocket.Service` does **not** host the SignalR hub - it is a client that calls
`Publish*` methods on the `/platform` hub, and `platform-web` is a second client of the
same hub that subscribes to the resulting events (`OnOddsChange`, `OnBetStopFixture`,
`OnWalletChange`, ...). The hub itself is a separate gateway service that lives in
neither this repo nor `platform-web` (see `docs/WebSocket.md` - the exact hub host is a
documented unknown).

To wire the two up locally, point both at the same hub URL:

- odds-engine-2: `Custom:WebSocketAPI` in `OE.Websocket.Service` (e.g.
  `http://localhost:3001/platform`),
- platform-web: `API:PlatformWebsocketAPI` in
  `Platform/Platform.Child.Web/appsettings.json` (per-domain override under
  `UrlMappings`).

Both sides must agree on transport settings (WebSockets, MessagePack).

### Docker

Each service has its own `Dockerfile`. Build from the **repository root** (the COPY paths
assume it) and pass a GitHub token for the private NuGet feed:

```bash
docker build -f src/OddsEngine/OE.Websocket.Service/Dockerfile \
  --build-arg NUGET_PASSWORD=<github-token> \
  -t oe-websocket .
```

The images expose no ports; configure via environment variables (`Section__Key=value`).
`appsettings.*.json` overrides are deleted during publish, so env vars are the only way
to configure containers. There is no docker-compose file in this repository.

## Running tests

Nine xUnit test projects are in the solution (`OE.Shared.Test`, `OE.Adapter.Service.Tests`,
`OE.Sportsbook.Service.Tests`, `OE.Websocket.Service.Tests`, `OE.Translation.Service.Tests`,
`OE.Translation.Job.Tests`, `OE.Job.KeepAlive.Tests`, `OE.Job.Lookup.Tests`,
`OE.Job.Settlement.Tests`). They are unit tests (wire-contract serialization, sharding
helper, translation composers, etc.) and do not need RabbitMQ/MongoDB/SQL running.

```bash
# everything
dotnet test src/OddsEngine/OddsEngine.sln

# one project
dotnet test src/OddsEngine/OE.Shared.Test

# one test
dotnet test src/OddsEngine/OE.Shared.Test --filter FullyQualifiedName~HelperInstanceNameTests
```

### Mutation testing

Passing tests prove the suite runs, not that it would catch a bug. [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/)
edits the production code and reports whether a test turns red. It is a global tool, installed per
developer, with shared settings in `src/OddsEngine/stryker-config.json`:

```bash
dotnet tool install -g dotnet-stryker

cd src/OddsEngine/OE.Shared.Test
dotnet stryker --project OE.Shared.csproj
```

No CI job runs it and no threshold is enforced. Current scores per project, the known coverage gaps,
and how to read the report are in [docs/Testing.md](docs/Testing.md).

## Upstream and downstream dependencies

| System | Direction | How it connects | Local setup |
|---|---|---|---|
| Feed receivers (BetGenius / UOF, sibling repos) | upstream | publish to `e.central` with `r.bg.receiver.*` / `r.uof.#` keys | run them separately, or hand-publish test messages |
| RabbitMQ (`e.central`, topic) | both | `MessageBroker:*` config; TLS auto-on when port is 5671 | local broker on 5672 |
| MongoDB (`sportsbook` DB) | downstream | `Custom:MongoDB-Sportsbook` | local mongod on 27017 |
| SQL Server (`Translation`, `Betting` DBs) | downstream | `ConnectionStrings:*` (EF Core 3.1) | local SQL Server on 1433; schema/seed scripts are not in this repo |
| SignalR `/platform` hub | downstream | `Custom:WebSocketAPI` (Websocket service is a hub client) | separate gateway service, not in this repo |
| Platform API | downstream | `Custom:PlatformAPI` / `API:PlatformAPI` | dev instance or local platform-final |
| External UOF broker (vhost `ext`) | downstream | `ExternalMessageBroker:*` (UOF.Client only) | second vhost on your local RabbitMQ |
| BetGenius video API | upstream | `Custom:BetGenius:*` (LiveStream job only) | requires real credentials; skip locally |

## Known gaps

- No docker-compose or one-command local stack exists in this repo.
- SQL schema/migration scripts for the `Translation` and `Betting` databases are not in
  this repo, so SQL-backed services cannot be fully exercised without a pre-existing
  database.
- The SignalR hub host application is not in this repo; without it (or a stand-in hub at
  `Custom:WebSocketAPI`) `OE.Websocket.Service` cannot deliver messages end-to-end.

## Keeping this README up to date

This README is part of the codebase. Update it in the same PR whenever anything it documents changes - project structure (projects/services added, renamed, or removed), configuration keys, run commands, ports, endpoints, or external dependencies. An outdated run guide is worse than none.
