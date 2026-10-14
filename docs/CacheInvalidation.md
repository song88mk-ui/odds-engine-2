# Cache Invalidation

How cached data is kept fresh across odds-engine-2. Today **no OEv2 cache is message-invalidated**:
everything is either **TTL-expiry only** or reloaded at startup. All [Verified] unless tagged.

> **The `sb:translation:*` purge is a no-op.** That cache had one writer,
> `TranslationRepository.GetTranslationsAsync` in `OE.Sportsbook.Service`, and the 2026-07
> translation-flow redesign removed it. The `ON_DYNAMIC_TRANSLATION_CHANGE` handler is **retained** —
> and backoffice-final still publishes the message — but it now purges an empty cache. The mechanics
> below are kept for the cross-repo delivery contract.
>
> ⚠️ **Two different messages, easily confused:**
> - `ON_DYNAMIC_TRANSLATION_CHANGE` (payload `OnDeleteCacheMessage`, from backoffice-final) → consumed
>   by `OE.Sportsbook.Service` → the no-op purge described here.
> - `ON_TRANSLATION_CHANGE` (`r.oe.ontranslationchange`) → consumed by **`OE.Translation.Service`** →
>   the **live** path: persist the Mongo `*NaTr` delta and republish translation-only odds changes.
>   That is how a changed translation actually reaches clients now.
>   A `MaTe:` delta is scoped by its `SrcTx` (the row's SOURCE_TEXT, sent by every producer since
>   GM-852): only open markets whose `MaNa` matches it, ignoring case, are updated. A base `MaTe:{id}`
>   row therefore no longer updates every wording of its template; a delta without `SrcTx` (older
>   senders) still does. Tested in `MongoTranslationRepositoryTests` (GM-874).
>   Both `OE.Translation.Service` and `OE.Adapter.Service` also apply each delta to their in-process
>   `TranslationCache` (`OE.Translation.Service/Worker.cs:188`,
>   `OE.Adapter.Service/Repositories/TradingRepository.cs:107`). A delta whose `SrcTx` differs from the
>   row's known SOURCE_TEXT, ignoring case, is a **rename**: `Apply` drops the content-index entry under
>   the old text, but only while that entry is still the renamed row's own list, so another row's entry
>   for the same text survives (GM-875). A case-only change is not a rename. Clearing a row's last
>   language removes the entry under its text with the same ownership check, so another row's entry
>   survives that too (GM-875 follow-up). Remaining gaps:
>   [TechnicalDebt.md](./TechnicalDebt.md) L11.

## Invalidation mechanisms in play
| Cache | Invalidation | Trigger |
|---|---|---|
| `sb:translation:*` | **message-driven purge** — **now a no-op (cache never populated)** | `ON_DYNAMIC_TRANSLATION_CHANGE` / `OnDeleteCacheMessage` |
| Adapter `TranslationCache` | **no purge**; preloaded at process start, then updated in place by `ON_TRANSLATION_CHANGE` deltas (`TranslationCache.Apply`, dispatched at `Worker.cs:189-193`) | restart, or an `ON_TRANSLATION_CHANGE` delta (`OE.Translation.Service` applies the same deltas to its own copy) |
| `bg:auth` | TTL = token `ExpiresIn`; **delete on API error** | expiry / error |
| `bg:fixture:*` | TTL ~5h (incl. negative `Bet_Not_Found`) | expiry |
| `mtpp` (player props) | TTL **1 year**; loaded once at startup | expiry only (effectively never) |

## Translation invalidation (handler retained — now a no-op) — [Verified]
> The handler still runs but the `sb:translation:*` cache is never populated, so the purge below
> removes nothing. Kept because backoffice-final still publishes the message.

`SportsbookRepository.ProcessCacheInvalidationMessage(OnDeleteCacheMessage)` (`:340`, dispatched from
`Worker.cs:138`):
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
The old Sportsbook translation cache used negative caching (empty result cached for 1h) so
permanently-missing `StringId`s didn't hit SQL on every message. That path is gone — `OE.Adapter.Service`
preloads the whole table instead, so a miss is simply "not in the dictionary" and costs nothing.

Negative caching is still live for **BetGenius fixtures** (`bg:fixture:*` caches `Bet_Not_Found`), with
the usual trade-off: a fixture that *becomes* available won't be seen until the entry expires.

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
