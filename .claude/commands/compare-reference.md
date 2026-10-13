---
description: Compare current code against the /Reference baseline snapshot to detect regressions
argument-hint: [file or area to compare]
---

Compare `$ARGUMENTS` against the **`Reference`** sibling (the pre-AI baseline snapshot at
`../Reference`). **Never modify `Reference`** — it is read-only historical.

1. Locate the corresponding file(s) in `../Reference` and in the current repo.
2. Diff behavior (not just text): logic changes, contract changes, removed/added handling.
3. Classify: intended change vs **possible regression**; tag verified / assumption / unknown.
4. For behavioral deltas, trace the impact (use `/impact-analysis`).
5. Prefer the current implementation unless the comparison reveals a regression.

Note: `oddsengine` (sibling) is the **obsolete v1** — do not use it as a reference unless explicitly
told. Output: per-file delta + regression risk + evidence. Do not change code unless asked.
