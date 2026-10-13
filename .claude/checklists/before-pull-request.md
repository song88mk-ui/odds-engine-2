# Checklist — Before Pull Request (OEv2)

- [ ] `dotnet build src/OddsEngine/OddsEngine.sln` clean (remember: KeepAlive built by CI, not in .sln).
- [ ] `dotnet test src/OddsEngine/OddsEngine.sln` (or the matching `*.Tests` project) green; edge cases
      considered; state anything you could not validate.
- [ ] **No committed secrets** (don't add to `appsettings.json`; `.gitignore` only excludes
      `appsettings.Development.json`).
- [ ] Patterns match the repo ([docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md) /
      [docs/CodingStandards.md](../../docs/CodingStandards.md)); naming per
      [docs/NamingConventions.md](../../docs/NamingConventions.md); abbreviated fields used.
- [ ] Persistence: idempotent upsert, `Set`/`SetOnInsert` correct, **new query has an index**; Mongo
      writes are `w=0`, so a failed write is silent.
- [ ] Sharding and concurrency: per-fixture routing unaffected; no unsynchronized shared Singleton
      state (cf. H3).
- [ ] **Contract change?** additive vs breaking noted; siblings flagged; MessagePack rename →
      coordinated deploy with the platform-final hub and platform-web noted
      ([docs/Contracts.md](../../docs/Contracts.md)).
- [ ] Config: new flags under `Custom:*`; argocd values (and new routing-key bindings in
      `MessageBroker__RoutingKey`) updated in the same change.
- [ ] Observability: Activities + metrics + `LogXxxEx` added where relevant.
- [ ] Ran `/review-feature` and addressed findings.
- [ ] Docs/assets updated if architectural knowledge changed (`/update-docs`).
- [ ] PR description filled from [.github/pull_request_template.md](../../.github/pull_request_template.md),
      naming only this PR's own ticket key.
- [ ] Summary of changes, risks, validation results included.
