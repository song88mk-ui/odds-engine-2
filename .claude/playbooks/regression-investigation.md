# Playbook — Regression Investigation (OEv2)

Pairs with the `regression-analysis` skill + `regression-review` checklist.

## 1. Establish "before" and "after"
- What worked before; what changed. Suspected window (commit / `GM-####` feature).

## 2. Diff against a baseline
- Git history: `git log -p -- <path>` / `git blame` on the area, `git diff <good-commit>..origin/main -- <path>`
  against a known-good commit, `git log -S<symbol>` to date a change. Use hotspot intel
  ([docs/knowledge/FrequentlyModifiedAreas.md](../../docs/knowledge/FrequentlyModifiedAreas.md);
  `SportsbookRepository.cs` changes most). Ignore `oddsengine` (obsolete v1).

## 3. Look for the classic OEv2 regression: a silent contract break
- DTO/Mongo-field/enum-value/routing-key change, or a **MessagePack property rename**, that broke a
  consumer without a compile error ([docs/Contracts.md](../../docs/Contracts.md)). Also status-mapping
  changes (`GetBetStatus`/`GetVoidFactor`/`ToBetStatus`) affecting settlement; sharding/routing changes.

## 4. Confirm causation
- Trace the flow before/after; tie the behavior delta to a specific change (file:line + commit). Mark
  verified / assumption / unknown.

## 5. Scope & report
- `/impact-analysis` for blast radius; smallest corrective scope (await approval if architectural).
  Output: introducing change + evidence + affected consumers + validation plan.
