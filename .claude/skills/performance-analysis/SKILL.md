---
name: performance-analysis
description: Analyze throughput/latency of OEv2 hot paths (Mongo writes, settlement reads, cache, shard load, render volume). Use for slowness or scaling questions.
---

# Performance Analysis (OEv2)

Reference: [docs/MongoDb.md](../../../docs/MongoDb.md), [docs/Caching.md](../../../docs/Caching.md),
[docs/RenderingPipeline.md](../../../docs/RenderingPipeline.md). Use `/performance-review`.

## Hot spots
- **Mongo:** index coverage (⚠️ settlement `PrFiId+MaTeId+SeTeId+SeSp` unindexed, P1); unordered
  BulkWrite + `bulkwrite.duration` histogram; `w=0`.
- **Cache:** O(n) prefix purge (P7); TTL truncation (P2); 1yr `mtpp` (P6); negative caching.
- **Shard load:** distribution depends on provider-id last-two-digit uniformity; prefetch 1000.
- **Message→render amplification:** every published snapshot → a server render per subscribed
  platform-web circuit; redundant `OnOddsChange` wastes renders.
- **EF:** `new TradingContext` per call (M9); pooled TranslationContext (good).

## Procedure
Identify the hot path → measure with metrics/traces (Tempo) → find the bottleneck (query plan / cache
hit-rate / shard skew / publish volume) → report with evidence + confidence. Suggest investigation,
not refactors, unless asked. **Production = read-only** (observe, don't tune live).
