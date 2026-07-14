# Prompt — Production-Safe Mode

Use when touching anything that could reach a live environment.

> Operate in **PRODUCTION READ-ONLY** mode for this task. Do NOT publish messages, write to Mongo/SQL,
> invalidate caches, deploy, restart services, or run admin actions against any real environment.
> Investigate and recommend only. If a remediation requires a write/deploy, present it as an explicit,
> human-approval-gated proposal and stop.
>
> Use real prod config from `argocd/apps/oddsengine/<service>/prd.yaml` (committed appsettings are NOT
> prod). Gather evidence only from read-only sources: Tempo traces, metrics, logs, `r.error.*`, queue
> depth. Identify service + shard (00–10)/client instance. Correlate with
> [docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md) and
> [docs/knowledge/Troubleshooting.md](../../docs/knowledge/Troubleshooting.md). Mark Verified/Likely/
> Unknown and state what you could not check.
