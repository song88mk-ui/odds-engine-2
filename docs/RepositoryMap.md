# Repository Map

How `odds-engine-2` sits inside the larger Song88 sportsbook workspace at
`d:/Gimli/Repositories/`.

## Workspace siblings (one level up)

| Folder | What it is | Relationship to odds-engine-2 | Confidence |
|---|---|---|---|
| **odds-engine-2** | *This repo.* .NET worker services that ingest, enrich, persist, and fan out odds/fixture/settlement data (a.k.a. **OEv2**). | — | [Verified] |
| **bet-genius** | .NET services integrating the **Genius Sports / BetGenius** external feed (XML over HTTP). Normalizes provider events and publishes to RabbitMQ. | **Upstream source.** Publishes `r.bg.receiver.*` messages consumed by `OE.Adapter.Service`. Shares the `OE.Shared` message contracts. | [Verified-bet-genius] |
| **platform-final** | .NET backend (≈39 projects: REST APIs, SignalR `Platform.Websocket.API2`, worker services). | **Downstream consumer.** Hosts the `/platform` SignalR hub that `OE.Websocket.Service` pushes to; consumes `r.oe.bet.settlement`; **reads** the Sportsbook MongoDB that OEv2 writes; serves the REST API that OEv2 calls. | [Verified-platform-final] |
| **platform-web** | **Blazor Server** (.NET 7) multi-tenant sportsbook UI. | **End consumer.** SignalR client of the `/platform` hub + REST client of the platform API. Renders OEv2-originated odds/bets in real time. | [Verified-platform-web] |
| **Auth** | Authentication service/repo. | Not in the OEv2 data path; likely issues operator/user tokens used by platform-web/-final. | [Unknown] |
| **argocd** | GitOps deployment manifests (Helm chart `betgenius-app`, per-service `{dev,stg,prd}.yaml`, image-updater). | **Deploys** OEv2. Defines real runtime config (queues, hosts, secrets) overriding the committed `appsettings.json`. | [Verified-argocd] |
| **infrastructure** | Helm/helmfile for shared infra (RabbitMQ cluster, MongoDB, secret-stores). | Hosts the RabbitMQ + MongoDB that OEv2 depends on. No OE-specific deploy logic. | [Verified-infrastructure] |
| **Reference** | Baseline snapshot of source **before** AI-assisted development. | Historical comparison only. **Never modify.** Not analyzed in this pass. | [Verified] (existence) |
| **oddsengine** | Legacy/obsolete Odds Engine (v1). | **Obsolete** — superseded by odds-engine-2. `argocd/apps/oddsengine/` still references v1 deployments. Do **not** use as implementation reference. | [Verified-argocd] |
| **oddsfeed** | Sibling repo, contents not analyzed. | **[Likely]** a producer of the `r.uof.*` (Unified Odds Feed) messages that `OE.Adapter.Service` also consumes — i.e. a second upstream source besides bet-genius. **Unverified.** | [Unknown] |

## The end-to-end pipeline (high level)

```
 External providers
 ┌─────────────────────────┐        ┌──────────────────────────┐
 │ Genius Sports / BetGenius│        │  (UOF source — oddsfeed?)│ [Unknown producer]
 │  XML feed over HTTP      │        │                          │
 └───────────┬─────────────┘        └────────────┬─────────────┘
             │ r.bg.receiver.*                    │ r.uof.*
             ▼                                    ▼
        ╔════════════════════════ RabbitMQ  e.central (topic) ════════════════════════╗
        ║                                                                              ║
        ║   OE.Adapter.Service  — normalizes + sticky-shards to r.oe.adapter.<type>.NN ║
        ║          │                                                                   ║
        ║          ├──────────────► OE.Sportsbook.Service (instances 00–10)            ║
        ║          │                 → writes MongoDB Sportsbook                        ║
        ║          │                 → publishes r.oe.sb.engine.*, r.oe.bet.settlement  ║
        ║          │                                                                   ║
        ║          ├──────────────► OE.Job.Lookup  → MongoDB *Lookup collections        ║
        ║          │                                                                   ║
        ║          └──────────────► OE.UOF.Client  → EXTERNAL broker (vhost "ext")      ║
        ║                            (white-label egress: uof.onoddschange.* etc.)      ║
        ║                                                                              ║
        ║   OE.Websocket.Service ◄── r.oe.sb.engine.*, r.bet.*, r.wallet.*              ║
        ║          │  (SignalR *client*)                                               ║
        ╚══════════╪═══════════════════════════════════════════════════════════════════╝
                   ▼ invokes hub methods
        platform-final  Platform.Websocket.API2  "/platform" SignalR hub
                   │  broadcasts to subscribed groups
                   ▼
        platform-web  (Blazor Server) — renders live odds/bets to the browser

  Side paths:
   • OE.Job.LiveStream  → BetGenius Video API (HTTP) → MongoDB Fixture (stream URLs)
   • OE.Job.Settlement  → reads MongoDB, publishes fixture-close
   • OE.Job.Cleanup     → deletes aged fixtures/markets/selections from MongoDB
   • OE.Job.KeepAlive   → publishes r.oe.uof.heartbeat every 5s
   • platform-final reads the same Sportsbook MongoDB (read-only) for its REST API
```

See [Architecture.md](./Architecture.md) for the detailed message flow and
[CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md) for the contract details.
