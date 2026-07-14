---
name: production-investigation
description: READ-ONLY production incident diagnosis for OEv2. Use for live issues. Never write/deploy/mutate/invalidate against production unless explicitly instructed.
---

# Production Investigation (OEv2) — READ-ONLY

**Hard rule:** do not publish messages, mutate Mongo/SQL, invalidate caches, deploy, restart, or run
admin actions against production. Diagnose and recommend only; any write action requires explicit
human approval.

## Procedure
1. Identify service + **shard (00–10)** / client instance (per-client Websocket/UOF).
2. [docs/knowledge/Troubleshooting.md](../../../docs/knowledge/Troubleshooting.md) for symptom → cause.
3. Read-only evidence: **Tempo traces** (end-to-end via header propagation), metrics
   (`processed.messages`/`forwarded.messages`/`bulkwrite.duration`), Compact-JSON logs, `r.error.*`
   routing keys, and **real prod config** at `argocd/apps/oddsengine/<service>/prd.yaml` (committed
   appsettings are NOT prod).
4. Correlate with risk register ([docs/ArchitectureRisks.md](../../../docs/ArchitectureRisks.md)):
   bet-stop race (R1), Websocket instability (R2), silent failures (R5), external infra (R6).
5. Beware false-healthy: KeepAlive `IsHealthy` is hardcoded true (H4).

## Output
Ranked hypotheses + supporting read-only evidence + the confirming checks + (if a write is needed) an
explicit, human-approval-gated remediation proposal. Mark Verified/Likely/Unknown.
