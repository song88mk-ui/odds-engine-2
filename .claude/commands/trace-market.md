---
description: Trace a market through OEv2 (market change → Mongo → odds-change → UI)
argument-hint: [MaId or MaTeId]
---

Trace market `$ARGUMENTS`. Report hops with file:line; mark Verified/Likely/Unknown.

1. Source: `OnMarketChangeMessage` (`Ma[]` MarketMessage → `Se[]` SelectionMessage) from
   `OE.Adapter.Service` → `r.oe.adapter.onmarketchange.<NN>`.
2. Persist: `SportsbookRepository.ProcessOnMarketChangeMessage` → `ToMarketAndSelectionChange`
   upserts `Market` + `Selection` (`SetOnInsert` for `MaId/SeId`; `Set` for status/odds). Note
   `MaStId` (`MarketStatus`), `CaStId` (`CashoutStatus`), `InPl`, `MaSoOr`, `ClCo`/`ClCoMa`.
3. Lookups: `OE.Job.Lookup` → `MarketLookup`/`SelectionLookup` (keyed by `SpId,MaTeId,FiTyId`).
4. Odds change: `PrepareInternalOddsChangeMessage` → `r.oe.sb.engine.onoddschange` → Websocket →
   `/platform` hub → UI market column.
5. Templates: market type = `MaTeId`; player-props/bet-builder eligibility involves the Platform API
   (`markettemplateplayerprops`) and Websocket template processing.

Refs: [docs/DomainModel.md](../../docs/DomainModel.md), [docs/MongoDb.md](../../docs/MongoDb.md),
[docs/ComponentDataFlow.md](../../docs/ComponentDataFlow.md).
