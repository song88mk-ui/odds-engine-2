---
name: translation-investigation
description: Investigate localized names (translations) in OEv2. Since the 2026-07 translation-flow redesign OEv2 is back in the translation path — OE.Translation.Service reads and writes SQL TranslationCache2, persists *NaTr fields to Mongo, and OE.Websocket.Service pushes SpTr/SegTr/LeTr/HoTr/AwTr/MaTeTr/SeNaTr on SignalR. The older 2026-06-30 "translations removed from pipeline" note applies to the retired SQL path in OE.Sportsbook.Service only. Use for translation data, cache, wire-contract or missing-localized-name questions.
---

# Translation Investigation (OEv2)

## Dead — the retired SQL path (what the 2026-06-30 note got right)
- `OE.Sportsbook.Service` no longer has `TranslationRepository`/`TranslationContext`; the
  `LoadTranslationsAsync`/`MapTranslations`/`AddTranslationKey`/`AddContentKey` call sites are gone and
  the context is not registered in that service at all.
- Nothing writes the `sb:translation:*` MemCache namespace any more. One live remnant:
  `ProcessCacheInvalidationMessage` still deletes by that prefix on `ON_DELETE_CACHE`
  (`OE.Sportsbook.Service/Repositories/SportsbookRepository.cs:340`), so the handler purges a namespace
  nothing populates.
- The old `*NaTr` wire property names (`SpNaTr`/`SegNaTr`/`LeNaTr`/`FiNaTr`/`MaNaTr`) are gone — see
  the rename warning under **Live** below.
- ⚠️ "Retired" means retired from OEv2's code, **not** dropped from the database. `SportsbookDynamic`
  and `SportsbookGlossary` still exist in the `Translation` schema with data; no flyway migration drops
  them. Expect to find them if you look.

## Live — the current path (2026-07 redesign)
Chain: `OE.Adapter.Service` (preload, read) → `OE.Sportsbook.Service` → `OE.Websocket.Service`, with
`OE.Translation.Service` and `OE.Translation.Job` owning the store.

- **SQL** — `TranslationCache2` in the shared `Translation` database is the single translation store
  OEv2 uses: one flat row per `STRING_ID`, one `TARGET_TEXT_*` column per language (`zh`/`es`/`fr`).
  OEv2 both reads **and** writes it. `STRING_ID` collates `Latin1_General_CI_AS`, so two keys differing
  only in case are the same row. See [docs/Database.md](../../../docs/Database.md) for columns, the
  three indexes (only one of which EF declares), and the column-ownership split with the external
  pipeline.
- **Mongo** — `*NaTr` fields on the `Fixture`/`Market`/`Selection` collections (the `*Lookup`
  collections carry none), written by `WriteFixtureTranslations` / `WriteMarketTranslations` /
  `ProcessTranslationChange` in
  `OE.Translation.Service/Repositories/MongoTranslationRepository.cs:682` (moved there from
  `OE.Sportsbook.Service`).
- **SignalR** — translation-only `ON_ODDS_CHANGE` messages built in `PublishTranslationOnlyOddsChanges`
  and mapped into `OutgoingOddsChange` at
  `OE.Websocket.Service/Repository/SportsbookRepository.cs:1547-1927`.
- ⚠️ **The wire props were renamed, not just re-added** (C6/R9): `OutgoingOddsChange` now carries
  `SpTr`/`SegTr`/`LeTr`/`HoTr`/`AwTr` (fixture), `MaTeTr` (market) and `SeNaTr` (selection), all
  `List<OutgoingTranslation> {LaCo, Tx}`. MessagePack Contractless keys on property name, so a consumer
  still reading `SpNaTr` silently deserializes empty — OEv2 and platform-web must deploy together.
  `OutgoingBoostedOddsChange` carries no translations. **Null or empty means "unchanged" or "not
  translated yet", never "no translation"** — clients keep the current name and fall back to the English
  `*Na`.

## platform-web's own REST path
Separate from all of the above, platform-web has its own REST `GetTranslations()` → `TranslationState`
(30s in-process cache), accessed in markup via `_translationState.GetValue("key")`. That path serves
**static UI labels**, not entity names. Entity names come from OEv2 over SignalR. Pick the right path
before investigating a missing name.

**Highest-value open question** ([docs/Unknowns.md](../../../docs/Unknowns.md) #30): does platform-web
bind the renamed props, and does it treat an empty `*Tr` as "keep current"? If not, names blank out on
every delta-gated message.

## Procedure
1. Decide which layer the symptom lives in: SQL row missing/untranslated → `TranslationCache2`; row
   present but Mongo doc lacks `*NaTr` → `MongoTranslationRepository`; Mongo has it but the UI does not
   → the SignalR rename/empty-list semantics above; static label wrong → platform-web REST.
2. Read the owning repository query, then check keys, indexes and the column-ownership split.
3. **Production is read-only.**

Refs: [docs/Database.md](../../../docs/Database.md),
[docs/Contracts.md](../../../docs/Contracts.md) C6,
[docs/ArchitectureRisks.md](../../../docs/ArchitectureRisks.md) R9,
[docs/CacheInvalidation.md](../../../docs/CacheInvalidation.md),
[docs/TechnicalDebt.md](../../../docs/TechnicalDebt.md) H7/H8.
