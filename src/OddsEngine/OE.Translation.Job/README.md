# OE.Translation.Job

Maintenance companion of `OE.Translation.Service` (migrated from bet-genius's `BG.Translation.Job`
in the 2026-07 translation-flow redesign). Two hosted workers against the `Translation` SQL
database, both targeting the flat `TranslationCache2` — the single translation store (the retired
`SportsbookDynamic`/`SportsbookGlossary` tables are gone):

- **BetPlacementWorker** — consumes `r.bet.placement` (`OnBetPlacement`, published by Betting.API
  for every persisted bet) and flags the referenced `TranslationCache2` template rows
  (`MaTe:{id}`, `SeTe:{MaTeId}:{SeTeId}`) with `HAS_BET_PLACEMENT = 1` so cleanup never removes
  translations a bet history still needs.
- **CleanupWorker** — periodic retention sweep: stale unflagged `TranslationCache2` rows
  (`MaTe:/SeTe:/Te:/SpId:/SegId:/LeId:` prefixes) whose `COALESCE(UPDATED_AT, CREATED_AT)` is past
  the retention window. Rows the feed still emits stay fresh via the seeding path's liveness touch.
  Batched `DELETE TOP (n)`; see `Translation/TranslationCleanupSql.cs` (unit-tested SQL builders).

Configuration under `Custom:Cleanup:*` (enabled, interval, retention days, batch size, prefixes).
Never writes `TARGET_TEXT_*` — those belong to the external translation pipeline.
