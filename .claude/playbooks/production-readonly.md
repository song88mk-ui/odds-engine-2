# Playbook — Production Read-Only Investigation (OEv2)

**Hard rule:** production is READ-ONLY. No publish, no Mongo/SQL writes, no cache invalidation, no
deploy/restart, no admin actions — unless a human explicitly authorizes a specific action. Pairs with
the `production-investigation` skill + checklist.

## 1. Scope
- Symptom, time window, blast radius. Which service + **shard (00–10)** / client instance?

## 2. Get the REAL config
- `argocd/apps/oddsengine/<service>/prd.yaml` (+ External Secrets references). **Committed
  `appsettings.json` is NOT prod.**

## 3. Observe (read-only)
- **Traces:** Tempo (end-to-end via header propagation) — follow one correlation across services.
- **Metrics:** `processed.messages`, `forwarded.messages`, `bulkwrite.duration` (per shard/instance).
- **Logs:** Compact JSON to stdout; filter by `[Project.Class.Member (file:line)]`; `r.error.*` keys.
- **Queues:** depth/consumers (read-only) if accessible.

## 4. Correlate with known risks
- bet-stop race (R1/H3), Websocket instability (R2/H2), silent failures (R5: `w=0`, `catch {}`),
  external infra (R6: single Mongo IP), false-healthy KeepAlive (H4), translation cache staleness (R7).

## 5. Report
- Ranked hypotheses + read-only evidence + the confirming checks. If remediation needs a write/deploy,
  present it as an **explicit, human-approval-gated** proposal — do not execute.
