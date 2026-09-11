---
name: sql-investigation
description: Investigate the SQL Server side (Translation + Trading, EF Core 3.1). Use for translation data, client feed config/entitlement, or EF query questions.
---

# SQL Investigation (OEv2)

OEv2 uses SQL Server via EF Core 3.1 (pinned, EOL). **Read-only applies to Trading only** — its writer
is backoffice-final. Since the 2026-07 redesign OEv2 both reads **and writes** `Translation`. Reference:
[docs/Database.md](../../../docs/Database.md).

## Two databases
- **Translation** (`TranslationContext`) → `TranslationCache2`, the live store since the **2026-07
  translation-flow redesign**: one flat row per `STRING_ID` with a `TARGET_TEXT_*` column per language.
  Consumers are `OE.Adapter.Service` (read), `OE.Translation.Service` (read+write) and
  `OE.Translation.Job` (write) — **`OE.Sportsbook.Service` does not register this context at all** any
  more. The old `SportsbookDynamic` read path (`TranslationRepository.GetTranslationsAsync` and its
  `SportsbookRepository` call sites) was deleted from Sportsbook. ⚠️ `SportsbookDynamic` and
  `SportsbookGlossary` are retired from OEv2's **code**, but still exist in the schema with data — no
  flyway migration drops them. See [translation-investigation](../translation-investigation/SKILL.md)
  for the full dead/live split.
- **Trading** (`TradingContext`) → `ClientOddsFeedConfiguration` (PK `{ClientId,SportId}`),
  `ClientOddsFeedLiveFixture`. Read by `OE.UOF.Client/Repository/TradingRepository.cs` (⚠️ `new`
  context per call, M9). ⚠️ entitlement enforcement disabled (H5/BR-17).

## Procedure
Identify which DB/entity → read the repo query → note keys/indexes/timeouts (`Custom:DbContextTimeout`
180s) → trace where the data is consumed. Schema is owned by the **`infrastructure/flyway`** repo (OEv2
ships no migrations, so an EF model here is a mapping that can drift — cross-check the DDL). String-id
format `"{Prefix}:{Id}"`. **Production = read-only.** Report file:line + confidence.
