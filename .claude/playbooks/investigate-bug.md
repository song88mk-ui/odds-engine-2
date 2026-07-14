# Playbook — Investigate a Bug (OEv2)

Non-production defect. For prod, use [production-read-only-investigation.md](./production-read-only-investigation.md).

## 1. Pin the symptom
- Exact behavior, inputs, env. Which service + shard (00–10) / client instance?

## 2. Hypothesize from known causes
- [docs/knowledge/Troubleshooting.md](../../docs/knowledge/Troubleshooting.md) (symptom → cause).
- Usual suspects: bet-stop race (H3), silent `w=0`/`catch {}` (R5/M4), unindexed settlement (P1),
  TTL truncation (P2), MessagePack rename, hub non-replay, wrong shard routing, negative-cached
  translation.

## 3. Trace the failing flow
- `/trace-fixture|market|selection|websocket|request`. Read the actual `Process*` handler + repo
  method. Decode abbreviated fields.

## 4. Gather evidence
- OTel traces (end-to-end), metrics, Compact-JSON logs, `r.error.*`. Compare vs `../Reference` if a
  regression (`/compare-reference`). Mark Verified/Likely/Unknown.

## 5. Confirm root cause → scope fix
- Smallest change. `/impact-analysis` for blast radius. **Await approval if architectural.**

## 6. Validate & document
- Build + reason through behavior. `/update-docs` if architectural knowledge changed. Summarize.
