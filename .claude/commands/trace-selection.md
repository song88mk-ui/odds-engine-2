---
description: Trace a selection / its odds through OEv2 (odds → formats → settlement → UI)
argument-hint: [SeId or SeTeId]
---

Trace selection `$ARGUMENTS`. Report hops with file:line; mark Verified/Likely/Unknown.

1. Persist: `Selection` doc fields — `Od` (master decimal odds), probabilities
   (`Pr/WoPr/LoPr/VoPr/HaWoPr/HaLoPr`), `IsAc`, `SeStId`, `DeHeFa`, boosted (`ClBoOd/ClBoSeNa`).
   Written by `SportsbookRepository.ToMarketAndSelectionChange`.
2. Odds formats: `OE.UOF.Client/Repository/ClientRepository` applies **+2% margin** and converts to
   Malaysian/Hong Kong/Indonesian (`ClientOdds`: `DeOd/MaOd/HoOd/InOd`).
3. Settlement: `ProcessOnMarketSettlementMessage`/`ProcessOnBetSettlementMessage` → per-selection
   `r.oe.bet.settlement` (status via `int.GetBetStatus()` / `ToBetStatus(result, voidFactor)`).
   ⚠️ The settlement lookup `Find(PrFiId+MaTeId+SeTeId+SeSp)` is unindexed (P1).
4. UI: `OutgoingSelection` (`DeOd/MaOd/HoOd/InOd/IsAc`) → `Selection` component (odds up/down anim).

Refs: [docs/EntityLifecycle.md](../../docs/EntityLifecycle.md) §3–4,
[docs/BusinessRules.md](../../docs/BusinessRules.md) (BR-8…BR-13).
