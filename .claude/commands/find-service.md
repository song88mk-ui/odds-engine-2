---
description: Identify which OEv2 service/repository owns a responsibility
argument-hint: [responsibility, e.g. settlement | translations | livestream | lookups | egress]
---

Identify which of the 9 services owns `$ARGUMENTS`. ⚠️ Names mislead — verify from code, not class
names ([docs/RepositoryPatterns.md](../../docs/RepositoryPatterns.md)).

- Map responsibility → service → repository/method, with file:line. Use
  [docs/ProjectOverview.md](../../docs/ProjectOverview.md) +
  [docs/knowledge/CommonServices.md](../../docs/knowledge/CommonServices.md).
- State its consumed/published routing keys + data stores
  ([docs/ConfigurationOverview.md](../../docs/ConfigurationOverview.md)).
- If multiple services have a `SportsbookRepository`, identify the correct one (6 copies exist).
- Output: owning service, key file(s), routing keys, stores, and any cross-repo dependency.
