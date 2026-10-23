# AGENTS.md — odds-engine-2

Headless .NET RabbitMQ pipeline (12 services and jobs plus the `OE.Shared` kernel) that writes the
MongoDB Sportsbook catalogue and pushes odds to platform-web over SignalR. Read
[CLAUDE.md](CLAUDE.md) first: it holds the standing rules every agent follows and the full operating
manual. This file is the short version for agents.

## Mission boundaries
- Subject = odds-engine-2: it owns and writes the Mongo catalogue. It has no UI and no inbound HTTP;
  UI work belongs to `platform-web`.
- Only modify files inside this repo; siblings (`bet-genius`, `platform-final`, `platform-web`,
  `backoffice-final`, `argocd`) are read-only references.
- Deployed config lives in the separate `argocd` repo, not in the committed `appsettings.json`.

## Hard rules
- Sharding couples `OE.Adapter.Service`, `OE.Sportsbook.Service` and the argocd instance count;
  change them together (CLAUDE.md § Critical rules, rule 1).
- `OE.Shared` DTOs, enums, routing keys and the Mongo schema are cross-repo contracts (CLAUDE.md §
  Critical rules, rule 3).
- `OE.Shared` must stay within `netstandard2.1`; services target `net10.0` (CLAUDE.md § Tech stack).
- SignalR MessagePack is name-keyed: renaming an `Outgoing*` property breaks the platform-final hub
  and platform-web (CLAUDE.md § Critical rules, rule 4).
- New persisted or wire fields follow the abbreviated-name glossary (CLAUDE.md § Critical rules,
  rule 5).
- Mongo writes use `w=0`, so failures are silent; check Mongo logs (CLAUDE.md § Critical rules, rule
  6).
- The `MemCache` `int` TTL overload is in hours (CLAUDE.md § How this team writes code).
- Six `SportsbookRepository` classes exist; confirm which one you are editing (CLAUDE.md § Scope &
  build reminders).

## See also
[CLAUDE.md](CLAUDE.md) · [.claude/README.md](.claude/README.md) · [docs/README.md](docs/README.md)
