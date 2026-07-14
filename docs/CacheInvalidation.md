# Cache Invalidation

How cached data is kept fresh across odds-engine-2. Historically the only **actively invalidated** cache
was the translation cache; everything else is **TTL-expiry only**. All [Verified] unless tagged.

> **(Corrected 2026-06-30: translations removed from pipeline.)** The `sb:translation:*` cache is no
> longer populated (its only writer, `TranslationRepository.GetTranslationsAsync`, is never called now —
> the SQL read path was removed from `SportsbookRepository`). The `OnDynamicTranslationChange` /
> `OnDeleteCacheMessage` handler is **retained** but now purges an empty/unused cache — effectively a
> **no-op**. The mechanics below are kept for reference and the cross-repo delivery contract.

## Invalidation mechanisms in play
| Cache | Invalidation | Trigger |
|---|---|---|
| `sb:translation:*` | **message-driven purge** + 60 min TTL — **now a no-op (cache never populated as of 2026-06-30)** | `OnDynamicTranslationChange` / `OnDeleteCacheMessage` |
| `bg:auth` | TTL = token `ExpiresIn`; **delete on API error** | expiry / error |
| `bg:fixture:*` | TTL ~5h (incl. negative `Bet_Not_Found`) | expiry |
| `mtpp` (player props) | TTL **1 year**; loaded once at startup | expiry only (effectively never) |

## Translation invalidation (handler retained — now a no-op) — [Verified]
> **(Corrected 2026-06-30: translations removed from pipeline.)** The handler still runs but the
> `sb:translation:*` cache is never populated, so the purge below removes nothing.

`SportsbookRepository.ProcessCacheInvalidationMessage(OnDeleteCacheMessage)` (`:323`):
```csharp
if (message.PurgeAll) { _memCache.DeleteCacheByPrefix("sb:translation:"); return; }
foreach (var cache in message.CacheKeys) _memCache.DeleteCache($"sb:translation:{cache}");
```
- Reached via the `ON_DYNAMIC_TRANSLATION_CHANGE` message type (`Worker` dispatch). Either **purges
  all** `sb:translation:*` keys or removes specific keys.
- `OnDeleteCacheMessage` carries `PurgeAll`, `CacheKey(s)`, `ClientId`, `ClientCode`, `CacheType`.
- **Producer:** backoffice-final publishes config/translation changes (`r.oe.cachesync.*`); platform
  `CacheSync.Service` consumes `r.oe.cachesync.#`. [Verified-sibling]

> ✅ **Delivery path confirmed** [was Unknown #23 — resolved 2026-06-28]: backoffice-final publishes
> to `r.oe.cachesync.ondynamictranslationchange` (payload = `OnDeleteCacheMessage`); platform
> `CacheSync.Service.ProcessDynamicTranslationChange` purges platform's own caches, then **fans out
> to OEv2 shards** on `r.oe.adapter.ondynamictranslationchange.{00..09}`. OEv2 Sportsbook's per-shard
> binding `r.oe.adapter.#.NN` ([argocd] `sportsbook-service/*-NN.yaml`) matches, so the message **does**
> reach Sportsbook. [Verified-sibling:platform-final]
>
> ⚠️ **Contract bug (fixed in platform-final):** CacheSync published the bare boolean `true` as the
> relay payload — OEv2 then failed `JsonSerializer.Deserialize<OnDeleteCacheMessage>("true")`, the
> handler threw (swallowed), and **the purge never ran**, so stale / negative-cached translations
> persisted up to the 60-min TTL (the user-visible symptom: localized names reverting to the base
> language on a push). Fixed by publishing
> `new OnDeleteCacheMessage { CacheType = DynamicTranslation, PurgeAll = true }`
> (`CacheSyncRepository.ProcessDynamicTranslationChange`). OEv2 needed **no** change.
>
> ⚠️ **Residual gap:** the fan-out loop is `i < 10` (shards `00–09`); shard `10` (global bet-stop)
> receives no purge.

## Negative caching (anti-stampede) — [Verified]
`TranslationRepository` (`:112-127`) caches **empty results** for not-found `StringId`s (1h) so
permanently-missing translations don't hit SQL on every message. Trade-off: a translation **added**
after a negative-cache entry won't appear until that entry expires (or a purge arrives).
**(Corrected 2026-06-30: translations removed from pipeline.)** `TranslationRepository` is now orphaned/
uncalled, so negative caching is no longer exercised.

## TTL-only caches — [Verified]
- **BetGenius auth/fixtures**: refreshed on expiry; auth also deleted on any API error so the next
  call re-logs-in. No external invalidation.
- **Player-props (`mtpp`, 1 year)**: loaded once at UOF.Client startup; **no refresh mechanism** — if
  the platform's player-props config changes, UOF.Client won't see it until restart
  ([TechnicalDebt.md](./TechnicalDebt.md)).

## Cross-instance considerations — [Verified] (important)
Caches are **per-process** (each Sportsbook shard, each Websocket/UOF instance has its own
MemoryCache). There is **no fan-out/broadcast** of invalidation built into MemCache:
- A translation purge message must be **delivered to every instance** that caches translations (i.e.
  all Sportsbook shards) — a single queue consumer would purge only one shard. Whether the
  invalidation routing key is bound per-shard (fan-out) is part of Unknown #23.
- No cache versioning/generation token; correctness during the purge window relies on TTL.

## Consistency model
- **Eventually consistent caches.** Worst-case staleness = the TTL (60 min for translations) if a
  purge is missed or not delivered to an instance.
- Catalogue data itself (Mongo) is **not cached** in OEv2 — every odds/market write reads/writes Mongo
  directly, so there is no catalogue-cache to invalidate (only translations + provider lookups are cached).

## Invalidation footguns (see TechnicalDebt.md)
- **TTL truncation:** `(int)TimeSpan.FromMinutes(n).TotalHours` truncates sub-hour values to 0 →
  instant expiry. Only safe at exactly 60 min.
- **O(n) prefix purge:** `DeleteCacheByPrefix` scans all cache keys.
- **No purge for `mtpp`/`bg:*`:** TTL-only; config drift is invisible until expiry/restart.
- **Per-instance scope:** invalidation must reach all instances; otherwise shards diverge.

## Downstream invalidation (context, not OEv2)
platform-final has a dedicated `CacheSync.Service` (binds `r.oe.cachesync.#`) that invalidates its
per-pod `IMemoryCache` on `OnClientChange`/`OnMarketSortOrderChange`/`OnDynamicTranslationChange`/etc.;
platform-web caches expire on their own 30 s TTLs. These are separate from OEv2's translation cache.
[Verified-sibling]
