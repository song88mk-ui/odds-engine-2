# Playbook — Performance Investigation (OEv2)

Pairs with the `performance-analysis` skill + `/performance-review`. **Production = observe-only.**

## 1. Define the symptom
- Latency vs throughput vs backlog? Which stage (ingest/persist/publish/push)? Which shard/instance?

## 2. Inspect the hot path
- **Mongo:** is the query index-covered? (⚠️ settlement `PrFiId+MaTeId+SeTeId+SeSp` is unindexed, P1.)
  BulkWrite latency = `bulkwrite.duration` histogram. `w=0` write concern.
- **Cache:** hit rate; O(n) `DeleteCacheByPrefix` (P7); TTL truncation (P2); 1yr `mtpp` (P6).
- **Shard load:** skew from provider-id last-two-digit distribution; prefetch 1000.
- **Publish→render amplification:** redundant `OnOddsChange` → wasted platform-web server renders.
- **EF:** `new TradingContext` per call (M9).

## 3. Measure
- Tempo traces for span timings; metrics for rates/durations; queue depth for backpressure. Compare
  shards/instances to find skew.

## 4. Conclude
- Bottleneck (ranked) + evidence + the metric/trace that proves it + confidence. Suggest investigation,
  not refactors, unless asked. Note anything you couldn't measure (Unknown).
