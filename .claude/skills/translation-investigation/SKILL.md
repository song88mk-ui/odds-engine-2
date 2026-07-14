---
name: translation-investigation
description: Investigate localized names (translations). NOTE (2026-06-30) the OEv2 translation pipeline was REMOVED — OEv2 no longer reads SQL translations, embeds them in messages, or pushes them on SignalR. The surviving UI translation source is platform-web's REST GetTranslations(). Use to orient, but expect to investigate in platform-web.
---

# Translation Investigation (OEv2)

## ⚠️ The OEv2 translation pipeline was REMOVED (Corrected 2026-06-30: translations removed from pipeline)
OEv2 **no longer participates in translations**:
- `OE.Sportsbook.Service` **no longer reads SQL `SportsbookDynamic`** — `LoadTranslationsAsync`/
  `MapTranslations`/`AddTranslationKey`/`AddContentKey` and their call sites were deleted from
  `SportsbookRepository`, and `ITranslationRepository` is no longer injected.
- The `*Tr` translation lists were **removed from the published messages** (`OnInternalOddsChange`/
  `OnFixtureChange`/`OnMarketChange`).
- The SignalR wire models `OutgoingOddsChange`/`OutgoingBoostedOddsChange` **no longer have `*NaTr`
  properties**, and `OE.Websocket.Service` no longer maps them — OEv2 **does not push localized names**.
- The Mongo `*NaTr` fields **still exist but remain never-written** (unchanged — those `.Set()`s were
  already commented out).
- `TranslationRepository`/`TranslationContext`/`TranslationBase`/`TranslationItem`/`Outgoing*Translation`
  still exist and are still DI-registered, but are **orphaned/never called**. `ConnectionStrings:Translation`
  and `Custom:Translation:UseContentKey` are **unused**. The `sb:translation:*` MemCache is **never
  populated**, so the `OnDynamicTranslationChange` invalidation handler is now a **no-op** (retained).

## Where translations live NOW
**platform-web's own REST `GetTranslations()` → `TranslationState`** (30s in-process cache), accessed in
markup via `_translationState.GetValue("key")`. If the UI is missing/wrong localized names, investigate
**platform-web** (the REST path), not OEv2. Without `*NaTr` on the wire, the UI relies on this REST path
or falls back to default-language names (`SpNa`/`FiNa`). [Unknown] whether platform-web ever consumed the
message `*NaTr` (see [docs/Unknowns.md](../../../docs/Unknowns.md) #30).

Refs: [docs/CacheInvalidation.md](../../../docs/CacheInvalidation.md),
[docs/Database.md](../../../docs/Database.md), [docs/TechnicalDebt.md](../../../docs/TechnicalDebt.md) H7,
[docs/ArchitectureRisks.md](../../../docs/ArchitectureRisks.md) R9.

## Procedure
Confirm OEv2 is no longer involved (read path/messages/wire all removed) → pivot to platform-web's REST
`GetTranslations()`/`TranslationState`. **Production = read-only.**
