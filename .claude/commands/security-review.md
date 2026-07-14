---
description: Security review of OEv2 (secrets, trust boundaries, outbound creds)
argument-hint: [optional: file/area to focus]
---

Security review of `$ARGUMENTS` (or the whole repo if blank). Report findings with file:line +
confidence; suggest investigation, not fixes (unless asked).

Focus areas specific to OEv2:
- **Committed secrets** (H1): RabbitMQ/BetGenius/SQL/API creds in `appsettings.json` (`.gitignore`
  excludes only `appsettings.Development.json`). Flag any new ones; never echo secret values.
- **Trust model:** OEv2 has **no inbound auth** — it trusts any message on its queue. Verify the
  change doesn't widen the trust boundary (new external producer, exposed queue). See
  [docs/Authorization.md](../../docs/Authorization.md), [docs/AuthenticationIntegration.md](../../docs/AuthenticationIntegration.md).
- **Outbound creds:** broker/DB/API/BetGenius — sourced from config/secrets, prod via External
  Secrets. Check no hardcoding.
- **TLS:** broker port 5671, DocumentDB/RDS cert bundle.
- **PII/financial:** wallet/bet data flows through (relayed) — ensure no logging of sensitive values.
- **Disabled controls:** UOF entitlement validation is off (H5/BR-17) — note if relevant.

Cross-ref [docs/TechnicalDebt.md](../../docs/TechnicalDebt.md) (H1/H3) and
[docs/ArchitectureRisks.md](../../docs/ArchitectureRisks.md) (R3). Also consider running `/security-review`'s
built-in counterpart if deeper analysis is needed.
