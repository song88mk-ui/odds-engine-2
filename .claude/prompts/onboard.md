# Prompt — Onboard to odds-engine-2

Use this to get productive fast.

> You are working in **odds-engine-2 (OEv2)**: 9 headless .NET RabbitMQ worker services + `OE.Shared`,
> ingesting oddsfeed messages, persisting a MongoDB Sportsbook catalogue, and fanning out to the
> platform (SignalR) + an external UOF feed. **No UI here** (that's platform-web).
>
> Before doing anything: read [CLAUDE.md](../../CLAUDE.md) and the relevant [docs/](../../docs/) files
> ([docs/README.md](../../docs/README.md) is the index). Treat the repo + `/docs` as the source of
> truth. Internalize the non-negotiables: sharding via `GetInstanceName`; committed appsettings ≠ prod;
> `OE.Shared`/Mongo/routing-key/SignalR are cross-repo contracts; abbreviated field names; `w=0` writes;
> MessagePack name-keying. Tag every claim Verified/Likely/Unknown. Don't introduce new patterns;
> production is read-only.
>
> Then tell me: what's the task, and I'll trace it end-to-end before proposing anything.
