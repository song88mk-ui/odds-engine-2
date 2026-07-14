# Checklist — Architecture Review (OEv2)

- [ ] Preserves the event-driven worker pipeline; no worker→worker dependency introduced (keep acyclic).
- [ ] No new pattern/library/paradigm without explicit approval (no AutoMapper/MediatR/Options/Redis).
- [ ] Contract boundary respected: message DTOs / Mongo schema / enums / routing keys / SignalR models
      — additive vs breaking; siblings identified ([docs/Contracts.md](../../docs/Contracts.md)).
- [ ] Sharding invariant intact (per-fixture sticky routing; shard count couples Adapter+Sportsbook+
      argocd) — ADR-003.
- [ ] Doesn't rely on guarantees that don't exist (no transactions; `w=0` writes; no SignalR replay).
- [ ] Concurrency/thread-safety for shared Singleton state.
- [ ] Config/secrets via `Custom:*` + argocd/External Secrets (nothing hardcoded/committed).
- [ ] Observability preserved (traces propagate through messages; metrics).
- [ ] Risk register consulted ([docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md) R1–R8).
- [ ] Cross-repo dependencies identified, not invented; classified Verified/Verified-sibling/Unknown.
- [ ] Decision + rationale recorded (update [docs/ArchitectureDecisions.md](../../docs/ArchitectureDecisions.md) if a new ADR).
