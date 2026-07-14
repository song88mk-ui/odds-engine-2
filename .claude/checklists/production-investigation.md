# Checklist — Production Investigation (OEv2) — READ-ONLY

- [ ] **READ-ONLY confirmed**: no publish, no Mongo/SQL writes, no cache invalidation, no deploy/
      restart, no admin actions (unless explicitly instructed by a human).
- [ ] Symptom matched in [docs/knowledge/Troubleshooting.md](../../docs/knowledge/Troubleshooting.md).
- [ ] Identified affected **service + shard (00–10) / client instance**.
- [ ] Pulled **real prod config**: `argocd/apps/oddsengine/<service>/prd.yaml` (NOT committed appsettings).
- [ ] Evidence gathered read-only: Tempo traces (end-to-end), metrics (`processed.messages`,
      `bulkwrite.duration`), Compact-JSON logs, `r.error.*` keys.
- [ ] Correlated with risk register ([docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md)):
      bet-stop race (R1), Websocket instability (R2), silent failures (R5), infra (R6).
- [ ] Remembered false-healthy: KeepAlive `IsHealthy` hardcoded true (H4); `w=0` silent write failures.
- [ ] Hypotheses ranked; Verified/Likely/Unknown marked; gaps noted.
- [ ] Any remediation requiring a write is proposed for **explicit human approval** only.
