# PR — <GM-#### / DEVOPS-### : short title>

## What & why
<one-paragraph summary of the change and the requirement it serves>

## Services / files touched
- <service> — <file:area>
- …

## Contract impact
- [ ] No shared-contract change. **OR** changes: <message DTO / Mongo field / enum / routing key /
      SignalR model> — **additive / breaking**.
- Siblings affected: <none / bet-genius / platform-final / platform-web / backoffice-final>
- Coordinated deploy required: <no / which repos> (MessagePack rename → OEv2 + platform-web together).

## Sharding / concurrency
- [ ] Per-fixture routing unaffected. [ ] No new unsynchronized shared Singleton state.

## Persistence
- [ ] Idempotent upsert; `Set`/`SetOnInsert` correct; index added for new queries; `w=0` understood.

## Config / secrets
- [ ] No committed secrets; flags via `Custom:*`; argocd/External-Secrets updated if needed
      (incl. new routing-key bindings).

## Validation
- [ ] `dotnet build src/OddsEngine/OddsEngine.sln` clean.
- How behavior was validated (no test projects in-repo): <describe>

## Risks & assumptions
- <risks> / <assumptions> / <unknowns>

## Docs
- [ ] `/docs` + `CLAUDE.md`/assets updated for architectural changes (`/update-docs`).
