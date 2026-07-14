---
name: sql-investigation
description: Investigate the SQL Server side (Translation + Trading, EF Core 3.1). Use for translation data, client feed config/entitlement, or EF query questions.
---

# SQL Investigation (OEv2)

OEv2 uses SQL Server **read-only** via EF Core 3.1. The **writer is backoffice-final**. Reference:
[docs/Database.md](../../../docs/Database.md).

## Two databases
- **Translation** (`TranslationContext`) → `SportsbookDynamic` (StringId, TargetLanguageId, TargetText,
  Processed, …). **(Corrected 2026-06-30: translations removed from pipeline.)** This read path is **NO
  LONGER INVOKED by Sportsbook** — `TranslationRepository.GetTranslationsAsync` and its `SportsbookRepository`
  call sites were deleted; `ITranslationRepository`/`TranslationContext` remain registered but **orphaned**
  (and `ConnectionStrings:Translation`/`Custom:Translation:UseContentKey` are unused). The repo
  (`OE.Sportsbook.Service/Repositories/TranslationRepository.cs`: `AsNoTracking`, batched
  `Where(... Contains(ids) && Processed && TargetText != null)`, pooled context, indexes
  `UX_SBD_STRING_LANG` + redundant `IX_SBD_STRING_LANG`, P8) is documented for reference only.
- **Trading** (`TradingContext`) → `ClientOddsFeedConfiguration` (PK `{ClientId,SportId}`),
  `ClientOddsFeedLiveFixture`. Read by `OE.UOF.Client/Repository/TradingRepository.cs` (⚠️ `new`
  context per call, M9). ⚠️ entitlement enforcement disabled (H5/BR-17).

## Procedure
Identify which DB/entity → read the repo query → note keys/indexes/timeouts (`Custom:DbContextTimeout`
180s) → trace where the data is consumed. Schema is owned externally (no migrations in-repo). String-id
format `"{Prefix}:{Id}"`. **Production = read-only.** Report file:line + confidence.
