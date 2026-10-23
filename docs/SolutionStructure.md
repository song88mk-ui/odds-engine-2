# Solution Structure

## Solution file — [Verified]

`src/OddsEngine/OddsEngine.sln` (`OddsEngine.sln:1-78`). VS 2022 (format 17).

### Projects **in** the solution (10)

| Project | GUID suffix | Kind |
|---|---|---|
| OE.Adapter.Service | …F9F7FA | Worker (Exe) |
| OE.Shared | …FA68A7 | Library (netstandard2.1) |
| OE.Sportsbook.Service | …F762B837 | Worker (Exe) |
| OE.UOF.Client | …9238802 | Worker (Exe) |
| OE.Websocket.Service | …F9316 | Worker (Exe) |
| OE.Job.Settlement | …D374C5 | Worker (Exe) |
| OE.Job.Cleanup | …DA84CE | Worker (Exe) |
| OE.Job.BoostedOddsCap | …A5C5BD | Worker (Exe) |
| OE.Job.LiveStream | …6019F74B | Worker (Exe) |
| OE.Job.Lookup | …08FD62D | Worker (Exe) |

Plus a **Solution Items** folder containing `.editorconfig` (`OddsEngine.sln:10-14`).

### Projects **NOT** in the solution — [Verified] (important)

These exist on disk and are built by CI, but are absent from `OddsEngine.sln`:

| Project | Status | Evidence |
|---|---|---|
| **OE.Job.KeepAlive** | Real, deployed service (in CI matrix + argocd) but missing from the .sln | `OE.Job.KeepAlive/OE.Job.KeepAlive.csproj` exists; not referenced in `OddsEngine.sln` |
| **OE.API** | **Orphaned / stale.** Folder has **no `.cs` files and no `.csproj`** — only an `appsettings.Development.json` and empty subfolders (ActionFilters/Controllers/Models/Repositories/Telemetry). Not in .sln, not in CI. | `find OE.API -name '*.cs'` → 0 files; `OE.API/appsettings.Development.json` only |

> **Implication:** opening `OddsEngine.sln` will *not* show KeepAlive. CI builds projects by
> directory matrix, not via the .sln (see [DependencyGraph.md](./DependencyGraph.md)). `OE.API`
> appears to be a removed/never-finished ASP.NET API — treat as dead. Recorded in
> [TechnicalDebt.md](./TechnicalDebt.md).

There is also a second, nested solution `OE.Sportsbook.Service/OE.Sportsbook.Service.sln`
(a per-project convenience solution).

## On-disk layout — [Verified]

```
odds-engine-2/
├── README.md                       # one-liner: "Odds Engine based from Bet Genius Odds Feed"
├── .github/workflows/              # ci.yml, oe-shared-npkg.yml, clean.yml
├── .gitignore                      # ignores appsettings.Development.json, NOT appsettings.json
├── .dockerignore
└── src/OddsEngine/
    ├── OddsEngine.sln
    ├── .editorconfig
    ├── OE.Shared/                  # shared kernel (70 .cs files)
    │   ├── Constants/  Data/  Enums/  Extensions/  Messages/
    │   ├── Models/  Providers/  Repositories/  Utilities/
    ├── OE.Adapter.Service/         # Program.cs, Worker.cs, Repositories/, Telemetry/, Dockerfile
    ├── OE.Sportsbook.Service/      # + MongoIndexBootstrapper.cs, nested .sln
    ├── OE.UOF.Client/              # + Models/{ExternalMessage,DTOs}/, Repository/
    ├── OE.Websocket.Service/       # + Messages/ (Incoming*/Outgoing*)
    ├── OE.Job.Lookup/
    ├── OE.Job.Settlement/
    ├── OE.Job.LiveStream/          # + Extensions/ServiceCollectionBettingExtensions.cs
    ├── OE.Job.Cleanup/
    ├── OE.Job.BoostedOddsCap/      # Alerts/, Boosts/, Data/, Evaluation/, Telemetry/ + OddsChangeWorker.cs
    ├── OE.Job.KeepAlive/           # + Messages/OnHeartbeatMessage.cs
    └── OE.API/                     # STALE — no code
```

## Conventional per-service folder shape — [Verified]

Every worker service follows the same skeleton:

| File/Folder | Role |
|---|---|
| `Program.cs` | Host builder + DI registration (the composition root) |
| `Worker.cs` | The `BackgroundService`/`IHostedService` — connect broker, handle messages |
| `Repository/` or `Repositories/` | Data access + message transformation (interface + impl) |
| `Telemetry/Instrumentation.cs` | `ActivitySource` + `Meter` names and metric instruments |
| `appsettings.json` | Baked-in defaults (overridden at deploy time) |
| `Dockerfile` | Multi-stage .NET build → aspnet runtime |
| `Messages/` (some) | Service-local message DTOs not shared in OE.Shared |

This regularity is a strong convention — new services should replicate it (see
[CodingStandards.md](./CodingStandards.md)).

## Target frameworks — [Verified]

| Project | TFM |
|---|---|
| OE.Shared | `netstandard2.1` |
| All services, jobs, and test projects | `net10.0` |

Unified on `net10.0` + `sdk:10.0`/`aspnet:10.0` images by GM-751 (2026-08-24); the former
`net8.0`/`net9` mix (TechnicalDebt M2) is resolved.

## SDK styles — [Verified]

- `Microsoft.NET.Sdk.Worker`: OE.UOF.Client, OE.Websocket.Service, OE.Job.Lookup, OE.Job.Settlement.
- `Microsoft.NET.Sdk` (+ `OutputType=Exe`): the rest of the executables.
- `Microsoft.NET.Sdk`: OE.Shared (library).
