# Frequently Modified Areas

Where change concentrates, from git history (271 commits; ~44 `GM-` feature tickets, 2 `DEVOPS-`).
Use this to know what's volatile, what's high-blast-radius, and where to be careful. [Verified] (git).

## Hotspots — most-changed files (all history)
| Changes | File | Why it churns |
|---|---|---|
| **70** | `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs` | The catalogue heart — every odds/market/settlement/bet-stop write + message shaping. **Highest-risk file in the repo.** |
| 28 | `OE.Websocket.Service/Repository/SportsbookRepository.cs` | Push-shaping + template/metadata reads |
| 24 | `OE.Sportsbook.Service/Worker.cs` | Message dispatch for the catalogue |
| 23 | `OE.Adapter.Service/Worker.cs` | Normalize + shard routing |
| 22 | `OE.UOF.Client/Repository/ClientRepository.cs` | Odds math, formats, external publish |
| 19 | `OE.Websocket.Service/Worker.cs` | Hub push dispatch + threads |
| 19 | `OE.Adapter.Service/Repositories/TradingRepository.cs` | Routing/transform helper |
| 18 | `.github/workflows/ci.yml` | CI/deploy tuning |
| 17 | `OE.Shared/Utilities/MessageBroker.cs` | The shared broker — **touches every service** |
| 16 | `OE.UOF.Client/Worker.cs` | External feed lifecycle |
| 13 | `OE.Shared/Constants/MessageType.cs` | Message-type contract additions |
| 10–12 | `OnFixtureChangeMessage.cs`, `OnInternalOddsChangeMessage.cs`, `OnMarketChangeMessage.cs`, `OnMarketSettlementMessage.cs`, `OutgoingAdapterMessageBase.cs` | **Shared message contracts** — high blast radius (cross-repo) |
| 11 | `OE.Shared/Repositories/BetGeniusRepository.cs` | Provider integration |
| 7–10 | `BetGeniusLiveStream.cs`, `Job.LiveStream SportsbookRepository.cs`, per-service `appsettings.json`/`Dockerfile`/`.csproj` | provider + deploy config |

## Recent activity by area (last 100 commits)
| Commits | Area |
|---|---|
| 75 | **OE.Shared** (contracts/broker/models — the shared kernel) |
| 38 | OE.Sportsbook.Service |
| 36 | OE.Websocket.Service |
| 27 | OE.Job.LiveStream |
| 16 | OE.Job.Cleanup |
| 11 | OE.Job.Lookup |
| 10 | OE.Adapter.Service |
| 7 | OE.Job.Settlement |
| 3 | OE.UOF.Client |

## What this tells you
- **The pipeline core is the catalogue writer.** `SportsbookRepository` (Sportsbook.Service) is by
  far the most-touched file (70×, 3× the next). Most feature work lands here. Read it first; test it
  hardest. ([../MongoDb.md](../MongoDb.md), [../RepositoryPatterns.md](../RepositoryPatterns.md))
- **OE.Shared is the most active *area*** — and the **highest blast radius**: message contracts,
  enums, `MessageBroker`. A change here can ripple to bet-genius, platform-final, platform-web,
  backoffice-final. Treat as a coordinated/contract change. ([../Contracts.md](../Contracts.md))
- **Real-time delivery (Sportsbook + Websocket) is where most behavior lives**; the jobs
  (LiveStream/Cleanup/Lookup/Settlement) and UOF.Client change far less.
- **Active feature stream:** livestream (Job.LiveStream + BetGeniusLiveStream churn) and
  cleanup/retention were recently active areas.

## High-blast-radius files (change with care)
1. `OE.Shared/Constants/MessageType.cs` + `Messages/**` — wire/DB contract (cross-repo).
2. `OE.Shared/Utilities/MessageBroker.cs` — every broker-using service.
3. `OE.Shared/Utilities/Helper.cs` (`GetInstanceName`) — sharding; couples Adapter + Sportsbook + argocd instance count.
4. `OE.Sportsbook.Service/Repositories/SportsbookRepository.cs` — catalogue correctness + settlement.
5. MongoDB document schema (`OE.Shared/Data/Sportsbook/**`) — platform-final + backoffice-final readers.

## Workflow conventions (from history)
- Branches/commits tagged with **`GM-####`** (feature) and **`DEVOPS-###`** (infra).
- CI builds changed services per-directory matrix → ECR → argocd image-updater. ([../Hosting.md](../Hosting.md))

> Tip: before editing a hotspot, check [../AntiPatterns.md](../AntiPatterns.md) + [../TechnicalDebt.md](../TechnicalDebt.md)
> for the known issues clustered there (esp. SportsbookRepository: H3, P1, P4; MessageBroker: H1 secrets;
> Websocket Worker: H2 threads).
