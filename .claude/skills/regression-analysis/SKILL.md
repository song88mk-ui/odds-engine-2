---
name: regression-analysis
description: Detect behavioral regressions by comparing against the /Reference baseline and git history. Use when something "used to work" or after a refactor.
---

# Regression Analysis (OEv2)

## Sources of truth for "before"
- **`../Reference`** — pre-AI baseline snapshot (read-only; never modify). Use `/compare-reference`.
- **git history** — 271 commits; hotspots in [docs/knowledge/FrequentlyModifiedAreas.md](../../../docs/knowledge/FrequentlyModifiedAreas.md)
  (esp. `SportsbookRepository.cs`, 70 changes). `git log -p <file>` / `git blame`.
- ⚠️ `oddsengine` sibling is **obsolete v1** — not a reference unless told.

## Procedure
1. Pin the changed behavior + the suspected window (commit/feature `GM-####`).
2. Diff current vs `Reference` (and/or prior commit) for the area — behavior, not just text.
3. Classify intended-change vs regression; trace impact (`/impact-analysis`).
4. Check whether a **contract change** (DTO/Mongo/enum/routing key/MessagePack rename) silently broke
   a consumer — these are the classic OEv2 regressions ([docs/Contracts.md](../../../docs/Contracts.md)).
5. Verify/assume/unknown; evidence file:line + commit.

## Output
Regression verdict + the introducing change + evidence + blast radius. Don't fix unless asked.
