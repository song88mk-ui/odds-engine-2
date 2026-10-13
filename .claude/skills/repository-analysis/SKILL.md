---
name: repository-analysis
description: Build or refresh understanding of odds-engine-2 (or a sibling integration). Use to answer "how does the whole thing work" or to re-validate docs after big changes.
---

# Repository Analysis (OEv2)

The repo has already been analyzed across 10 phases → `/docs`. Start there; this skill is for
extending/refreshing that knowledge.

## Procedure
1. **Orient** from [docs/README.md](../../../docs/README.md) (the phase index) — don't re-derive what's
   documented.
2. For a new area: read the actual code (Program/Worker/Repository), cite file:line, tag
   verified / assumption / unknown.
3. For cross-repo: scope to the integration seam; classify [Verified] / [Verified-sibling:X] /
   [Unknown]; **never invent sibling behavior**
   ([docs/CrossRepositoryDependencies.md](../../../docs/CrossRepositoryDependencies.md)).
4. For broad fan-out searches, dispatch read-only Explore agents (names mislead here).
5. Reconcile contradictions explicitly (as with the translations-not-persisted + hub-host findings —
   [docs/KnowledgeValidation.md](../../../docs/KnowledgeValidation.md)).
6. **Update `/docs` + assets** for any new architectural knowledge ([/update-docs](../../commands/update-docs.md)).

## Output
Evidence-backed findings + confidence + which docs to update. Keep the abbreviated-field glossary
([docs/SharedLibraries.md](../../../docs/SharedLibraries.md)) open as reference.
