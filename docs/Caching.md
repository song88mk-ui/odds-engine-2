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

> ⚠️ **TTL-in-hours footgun** [Verified]: `MemCache`'s `int` TTL overload is in **HOURS**, not minutes.
> The old `TranslationRepository` passed `(int)TimeSpan.FromMinutes(60).TotalHours` = `(int)1.0` =
> **1 hour** — correct only by luck. Any sub-hour value truncates: e.g. 30 min → `(int)0.5` =
> **0 → AddHours(0) → immediate expiry**. That call site is gone, but the overload is unchanged and
> still bites any future caller; recorded in [TechnicalDebt.md](./TechnicalDebt.md).

> ⚠️ **`DeleteCacheByPrefix` is O(n)** over the entire process cache; fine at current scale but scans
> all keys (including unrelated `bg:*`/`mtpp` entries).

## What is cached, where, and for how long
| Cache | Key(s) | TTL | Where | Purpose |
|---|---|---|---|---|
| **Translations (live)** | `{Prefix:Id}` in a dedicated in-process cache | **no TTL** — full reload at startup | OE.Adapter.Service `TranslationCache` | stamp `*Tr` onto every outgoing message with zero SQL on the hot path |
| **Translation backfill throttle** | `BackfillCachePrefix + {StringId}` | short-lived | OE.Translation.Service `TranslationRepository` | stop a hot, already-checked key re-querying the legacy table on every message |
| **~~Translations (legacy)~~** | `sb:translation:{Prefix:Id}` | ~~60 min + negative cache~~ | OE.Sportsbook.Service | **never populated** since the 2026-07 redesign; only the purge handler survives |
| **BetGenius auth token** | `bg:auth` | `BGAuth.ExpiresIn` seconds | OE.Shared `BetGeniusRepository` (Job.LiveStream) | reuse bearer token until expiry |
| **BetGenius fixture(s)** | `bg:fixture:{id}` | ~5 hours | same | reduce Video API calls; also negative-caches `Bet_Not_Found` |
| **Market-template player props** | `mtpp` | **1 year** | OE.UOF.Client `PlatformRepository` | startup-loaded player-props config |

> The `sb:translation:*` cache was the only **message-invalidated** cache; every remaining cache relies
> purely on TTL expiry. Since the 2026-07 redesign that cache is **never populated**, so its
> `ON_DELETE_CACHE` purge is a **no-op** (handler retained) — see
> [CacheInvalidation.md](./CacheInvalidation.md).

## Cache strategy = cache-aside (lazy)
> The live cache-aside / read-through users are the **BetGenius** caches (`bg:auth`, `bg:fixture:*`).
> Translations no longer use cache-aside at all — see the preload note below.

The BetGenius pattern:
1. Check the key in `MemCache`.
2. On a miss, call the upstream API once.
3. Store the result under a TTL (auth: the token's `ExpiresIn`; fixtures: ~5h).
4. **Negative-cache** not-found results (e.g. `Bet_Not_Found`) so a permanently-missing id doesn't hit
   the API on every message.

## Cache strategy = full preload (translations)
`OE.Adapter.Service` does **not** read translations lazily. `TranslationCache.Preload()`
(`OE.Adapter.Service/Repositories/TranslationCache.cs:67-73`) loads the entire `TranslationCache2` table
into an in-process dictionary **once at startup**, and `TradingRepository` then stamps `*Tr` onto every
outgoing message from memory — zero SQL on the hot path.
- **Trade-off:** a translation added after startup is not picked up until the next restart *or* until an
  `ON_TRANSLATION_CHANGE` message drives `OE.Translation.Service`'s delta path.
- If `ConnectionStrings:Translation` is blank the preload is **skipped with a warning** and the service
  starts anyway (`Worker.cs:96-109`) — it simply publishes no `*Tr`.

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
