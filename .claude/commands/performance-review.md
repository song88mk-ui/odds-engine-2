---
description: Performance review of an OEv2 hot path (throughput, Mongo, cache, render volume)
argument-hint: [area, e.g. odds change | settlement | bulk write | translation cache]
---

Performance review of `$ARGUMENTS`. Report with file:line + confidence; suggest investigation, not
refactors (unless asked).

OEv2-specific hot spots to check:
- **Mongo:** are query filters index-covered? Known gap: settlement lookup
  `PrFiId+MaTeId+SeTeId+SeSp` is **unindexed** (P1). BulkWrite is unordered (`bulkwrite.duration`
  histogram); `w=0` write concern. See [docs/MongoDb.md](../../docs/MongoDb.md).
- **Cache:** cache-aside + negative caching (translations); `DeleteCacheByPrefix` is O(n) (P7);
  TTL-in-hours truncation (P2); 1yr `mtpp` no-refresh (P6). See [docs/Caching.md](../../docs/Caching.md).
- **Sharding/throughput:** per-fixture serialization via shard; prefetch 1000; check shard load
  distribution (digit-suffix uniformity).
- **Message volume → UI render volume:** every published snapshot triggers a server render per
  subscribed circuit (platform-web). Redundant `OnOddsChange` = wasted renders
  ([docs/RenderingPipeline.md](../../docs/RenderingPipeline.md)).
- **EF:** `AsNoTracking` reads, pooled `TranslationContext` (good); `new TradingContext` per call in
  UOF.Client (M9).

Output: bottleneck hypotheses ranked, evidence, and the metrics/traces to confirm (Tempo +
`processed.messages`/`bulkwrite.duration`).
