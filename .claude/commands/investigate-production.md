---
description: Read-only production incident investigation for OEv2
argument-hint: [symptom, e.g. odds not updating | duplicate settlements | pod crash-loop]
---

Investigate the production symptom `$ARGUMENTS`. **READ-ONLY**: do not publish messages, mutate
Mongo/SQL, invalidate caches, deploy, or run admin actions. Diagnose and report only.

1. Start from [docs/knowledge/Troubleshooting.md](../../docs/knowledge/Troubleshooting.md) — match the
   symptom to likely causes.
2. Identify the **service + shard/client instance** involved (sharding; per-client Websocket/UOF).
3. Evidence to gather (read-only): OpenTelemetry traces (Tempo, header-propagated end-to-end), metrics
   (`processed.messages`/`forwarded.messages`/`bulkwrite.duration`), Compact-JSON logs, `r.error.*`
   routing keys, and the **real prod config** in `argocd/apps/oddsengine/<service>/prd.yaml`.
4. Correlate with known risks ([docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md)) and debt
   ([docs/TechnicalDebt.md](../../docs/TechnicalDebt.md)) — e.g. bet-stop race (H3), silent `w=0`
   writes (P3), unindexed settlement (P1), hub-outage non-replay.
5. Distinguish Verified / Likely / Unknown. Note what you could NOT check.

Output: hypothesis ranked by likelihood, supporting evidence, and the read-only checks to confirm —
plus any escalation that would require a (human-approved) write action.
