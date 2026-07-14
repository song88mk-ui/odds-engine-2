# Checklist — Before Coding (OEv2)

- [ ] Requirement understood; restated in one sentence.
- [ ] Searched for an **existing implementation** (`/find-service`, `/find-pattern`) — am I about to
      duplicate? (6 `SportsbookRepository` copies exist — edit the right one.)
- [ ] Consulted the relevant `/docs` (start [docs/README.md](../../docs/README.md)) + read the actual code.
- [ ] **Traced the flow end-to-end** (`/trace-feature` / `/trace-fixture|market|selection|websocket`).
- [ ] Identified which **service(s)** + **shard/client instance** are involved.
- [ ] Identified **reusable** `OE.Shared` contracts/helpers to use (not re-implement).
- [ ] Contract impact assessed (`/impact-analysis`): does it touch `OE.Shared` DTOs / Mongo schema /
      enums / routing keys / SignalR models? → cross-repo + additive-vs-breaking.
- [ ] Sharding invariant unaffected (`GetInstanceName`, per-fixture routing).
- [ ] Approach + risks/assumptions explained; **approval obtained for architectural changes**.
- [ ] Confirmed: **smallest viable change**; no new patterns/libraries without approval.
- [ ] Production stays read-only.
