# Caching

odds-engine-2 caching is **in-process only** — `System.Runtime.Caching.MemoryCache.Default` wrapped
by `OE.Shared/Utilities/MemCache.cs`. **No Redis, no distributed cache, no browser/session/local
storage** (headless workers). All [Verified] unless tagged.

## The cache implementation — `MemCache` (`IMemCache`)
- Backed by the **process-wide static `MemoryCache.Default`** (`MemCache.cs:12`). Registered as a DI
  **Singleton**, but the underlying store is the shared default instance regardless.
- **Expiration: AbsoluteExpiration only** (no sliding, no size limit set). Two TTL overloads:
  - `AddOrUpdateCache(key, obj, int cacheTimeout)` → **`AddHours(cacheTimeout)`** — the int is **hours** (`:14`).
  - `AddOrUpdateCache(key, obj, DateTimeOffset date)` → absolute time (`:22`).
- **Reads:** `GetCache<T>(key)`, `GetCache<T>(key, Func<T> factory, expiration)` (read-through),
  `GetCacheData<T>`, `GetCache(key)` (a `KeyValuePair<bool,long>` variant), `Contains`.
- **Eviction/invalidation:** `DeleteCache(key)`, `DeleteCacheByPrefix(prefix)` (`:79` — **O(n) LINQ
  scan over all keys**, `StartsWith` ordinal-ignore-case), `GetExistingKeys(substring)`.

> ⚠️ **TTL-in-hours footgun** [Verified]: `TranslationRepository` passes
> `(int)TimeSpan.FromMinutes(60).TotalHours` = `(int)1.0` = **1 hour** — correct only by luck.
> Any sub-hour value truncates: e.g. 30 min → `(int)0.5` = **0 → AddHours(0) → immediate expiry**.
> Fragile; recorded in [TechnicalDebt.md](./TechnicalDebt.md). **(Corrected 2026-06-30: translations
> removed from pipeline.)** `TranslationRepository` is now **orphaned/never called**, so this footgun is
> no longer exercised in practice — but the footgun in `MemCache`'s int overload still applies to any
> future caller.

> ⚠️ **`DeleteCacheByPrefix` is O(n)** over the entire process cache; fine at current scale but scans
> all keys (including unrelated `bg:*`/`mtpp` entries).

## What is cached, where, and for how long
| Cache | Key(s) | TTL | Where | Purpose |
|---|---|---|---|---|
| **Translations** | `sb:translation:{Prefix:Id}` (e.g. `sb:translation:MaId:123`) | 60 min (1h) + **negative cache** of misses | OE.Sportsbook.Service `TranslationRepository` | avoid per-message SQL hits — **NO LONGER POPULATED as of 2026-06-30** (only writer `GetTranslationsAsync` is never called; repo orphaned) |
| **BetGenius auth token** | `bg:auth` | `BGAuth.ExpiresIn` seconds | OE.Shared `BetGeniusRepository` (Job.LiveStream) | reuse bearer token until expiry |
| **BetGenius fixture(s)** | `bg:fixture:{id}` | ~5 hours | same | reduce Video API calls; also negative-caches `Bet_Not_Found` |
| **Market-template player props** | `mtpp` | **1 year** | OE.UOF.Client `PlatformRepository` | startup-loaded player-props config |

> The translation cache was the only **invalidated** cache (via messages — see
> [CacheInvalidation.md](./CacheInvalidation.md)); the others rely purely on TTL expiry.
> **(Corrected 2026-06-30: translations removed from pipeline.)** Since the translation cache is no
> longer populated, its `OnDynamicTranslationChange` invalidation now purges an empty cache (a no-op);
> handler retained.

## Cache strategy = cache-aside (lazy)
> **(Corrected 2026-06-30: translations removed from pipeline.)** The example below describes
> `GetTranslationsAsync`, which is **no longer called** (repo orphaned). It is retained here only to
> document the cache-aside pattern; the live cache-aside/read-through users are now the BetGenius caches.

`TranslationRepository.GetTranslationsAsync` (`:38`) is the canonical pattern:
1. For each `StringId`, check `sb:translation:{id}`.
2. Batch-fetch all misses from SQL in one query.
3. Store each found result (1h).
4. **Negative-cache** not-found ids (empty list, 1h) to prevent repeated SQL hits for permanently
   missing translations.

BetGenius caches are **read-through** (`GetBetGeniusAuthorizationToken`/`GetBetGeniusFixture`):
check cache → call API on miss → store with TTL → delete on error.

## Scope & coordination — [Verified]
- **Per-process / per-instance.** Each worker (and each Sportsbook shard 00–10, each Websocket
  client instance) has its **own** MemoryCache. There is **no shared/distributed cache** and **no
  cross-instance invalidation broadcast** — a translation purge message must reach **every** instance
  that caches it. See [CacheInvalidation.md](./CacheInvalidation.md).
- **Thread-safety:** `MemoryCache.Default` itself is thread-safe. (Other in-process state like
  `_IsBetStopGlobally` is **not** a cache and is not synchronized — [TechnicalDebt.md](./TechnicalDebt.md) H3.)

## Not present (so you don't look)
- **Redis / distributed cache:** none. [Verified — absence]
- **`IMemoryCache` (Microsoft.Extensions.Caching.Memory):** not used by OEv2 workers — they use the
  older `System.Runtime.Caching.MemoryCache`. (platform-web uses `IMemoryCache` with 30 s TTLs — a
  *separate* cache layer, see `platform-web/docs/Caching.md`.)
- **Browser / Session / Local storage:** N/A — OEv2 has no browser. These exist on the **platform-web**
  side (e.g. `ProtectedSessionStorage` for line-view session) and are documented there.
- **EF second-level cache:** none; translation caching is done manually in MemCache.

## End-to-end caching layers (for context)
```
OEv2 MemoryCache (sb:translation:*, bg:*, mtpp)   ← per worker instance, this repo
platform-final per-pod IMemoryCache + CacheSync.Service invalidation   ← sibling
platform-web per-circuit IMemoryCache (30s: sport, templates, bet-config, translations)   ← sibling
browser sessionStorage/localStorage   ← platform-web
```
Only the first layer is odds-engine-2's; the rest are downstream and independently invalidated.
