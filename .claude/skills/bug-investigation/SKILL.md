---
name: bug-investigation
description: Diagnose a bug in odds-engine-2 by tracing the failing flow and correlating with known issues. Use for non-production defects; for prod use production-investigation (read-only).
---

# Bug Investigation (OEv2)

## Procedure
1. **Reproduce / pin the symptom.** Which service + shard/client instance?
2. **Start from Troubleshooting** ([docs/knowledge/Troubleshooting.md](../../../docs/knowledge/Troubleshooting.md))
   — symptom → likely cause.
3. **Trace the flow** with `/trace-*` (fixture/market/selection/websocket/request). Read the actual
   handler (`Process*`) + repo method.
4. **Correlate with known issues:** [docs/TechnicalDebt.md](../../../docs/TechnicalDebt.md),
   [docs/AntiPatterns.md](../../../docs/AntiPatterns.md), [docs/ArchitectureRisks.md](../../../docs/ArchitectureRisks.md).
   Usual suspects: bet-stop race (H3), silent `w=0`/`catch {}` (R5), unindexed settlement (P1),
   TTL truncation (P2), MessagePack rename, hub non-replay, wrong shard routing.
5. **Evidence:** OTel traces (end-to-end, header-propagated), metrics, Compact-JSON logs, `r.error.*`.
6. **Verify vs assume vs unknown** — never guess. Note what you couldn't check.

## Output
Root-cause hypothesis (ranked) + evidence (file:line/trace) + the smallest fix scope (await approval
if architectural) + validation plan. Don't change code until the diagnosis is confirmed/approved.
