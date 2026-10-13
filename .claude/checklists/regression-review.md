# Checklist — Regression Review (OEv2)

- [ ] Identified the changed behavior + the suspected introducing change/feature (`GM-####`).
- [ ] Compared against a known-good commit (`git diff <good-commit>..origin/main -- <path>`) — behavior,
      not just text. (`oddsengine` v1 is obsolete, not a reference.)
- [ ] `git log -p` / `git blame` on the area; checked the hotspots
      ([docs/knowledge/FrequentlyModifiedAreas.md](../../docs/knowledge/FrequentlyModifiedAreas.md)).
- [ ] Checked for a silent **contract regression** (DTO/Mongo/enum/routing-key/**MessagePack rename**)
      that broke a consumer ([docs/Contracts.md](../../docs/Contracts.md)).
- [ ] Checked sharding/routing changes (mis-routed fixtures).
- [ ] Checked status-mapping changes (`GetBetStatus`/`GetVoidFactor`/`ToBetStatus`) affecting settlement.
- [ ] Verified vs assumed vs unknown; evidence = file:line + commit.
- [ ] Blast radius assessed (`/impact-analysis`); fix scoped minimally (await approval if architectural).
