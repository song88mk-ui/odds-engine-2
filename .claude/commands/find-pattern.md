---
description: Find where an OEv2 implementation pattern is used, to copy it correctly
argument-hint: [pattern, e.g. bulk upsert | cache-aside | publish | factory | extension method]
---

Find existing usages of the pattern `$ARGUMENTS` so new code matches the house style. Do **not**
propose alternative patterns ([docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md)
is the catalog).

- Grep the codebase for representative implementations; cite file:line.
- Summarize the canonical example + variations (e.g. `BulkWrite<T>` upserts in
  `SportsbookRepository`; cache-aside in `TranslationRepository`; `Publish/CPublish` in
  `MessageBroker`; factory in `LiveStreamFactory`; `*Extensions` in `OE.Shared/Extensions`).
- Note known anti-pattern variants to avoid copying ([docs/AntiPatterns.md](../../docs/AntiPatterns.md)).
- Output: "use this as your template: <file:line>" + the conventions it embodies.
