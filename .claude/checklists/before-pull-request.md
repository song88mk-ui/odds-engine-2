# Checklist — Before Pull Request (OEv2)

- [ ] `dotnet build src/OddsEngine/OddsEngine.sln` clean (remember: KeepAlive built by CI, not in .sln).
- [ ] Behavior validated/reasoned through; edge cases considered (no test projects exist — state how
      you validated).
- [ ] **No committed secrets** (don't add to `appsettings.json`; `.gitignore` only excludes
      `appsettings.Development.json`).
- [ ] Patterns match the repo ([docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md) /
      [docs/CodingStandards.md](../../docs/CodingStandards.md)); naming per
      [docs/NamingConventions.md](../../docs/NamingConventions.md); abbreviated fields used.
- [ ] Persistence: idempotent upsert, `Set`/`SetOnInsert` correct, **new query has an index**.
- [ ] Concurrency: no unsynchronized shared Singleton state (cf. H3).
- [ ] **Contract change?** additive vs breaking noted; siblings flagged; MessagePack rename →
      coordinated deploy noted ([docs/Contracts.md](../../docs/Contracts.md)).
- [ ] Observability: Activities + metrics + `LogXxxEx` added where relevant.
- [ ] Ran `/review-feature` and addressed findings.
- [ ] Docs/assets updated if architectural knowledge changed (`/update-docs`).
- [ ] PR description filled (template: [.claude/templates/pr-description.md](../templates/pr-description.md));
      ticket `GM-####`/`DEVOPS-###`.
- [ ] Summary of changes, risks, validation results included.
