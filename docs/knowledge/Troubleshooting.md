# Troubleshooting Guide

Symptom → likely cause → where to look. Synthesized from the architecture + known debt/unknowns.
Cross-refs to canonical docs. Use with [FrequentlyModifiedAreas.md](./FrequentlyModifiedAreas.md).

## Real-time / odds

**Odds not updating in the UI for a fixture**
- Is the fixture on the **right shard?** All its events go to one Sportsbook instance via
  `GetInstanceName` (last 2 digits). Check the Adapter routed `r.oe.adapter.onmarketchange.<NN>` and
  that Sportsbook instance `<NN>` is up. ([../Architecture.md](../Architecture.md) ADR-003)
- Did `OE.Sportsbook.Service` publish `r.oe.sb.engine.onoddschange`? Check its logs + the
  `processed.messages`/`bulkwrite.duration` metrics.
- Is the UI **subscribed to the right group?** `endpoint:{clientCode}:sports:{SpId}:…` — wrong scope
  = no delivery. ([WebSocketMessages.md](./WebSocketMessages.md))
- Hub outage? Missed pushes are **not replayed** — UI stale until next push/navigation. ([../WebSocket.md](../WebSocket.md))
- MessagePack contract mismatch after a deploy (renamed property) → silent deserialize gaps. Deploy
  OEv2 + platform-web together. ([../Contracts.md](../Contracts.md) C6)

**Bets still accepted during a global bet-stop**
- `_IsBetStopGlobally` is **not thread-safe** (race under prefetch 1000). ([../TechnicalDebt.md](../TechnicalDebt.md) H3)
- Did `OnBetStopGlobal` reach Sportsbook? It dedupes (`_IsBetStopGlobally == message.IsBetStop` → no-op).
- Global bet-stop routes through reserved shard **10** — confirm that instance is healthy.

**A market won't suspend / bet buttons stay live**
- Check `MaStId` is actually set to Suspended/Inactive and `IsAc=false` on selections; the UI keys
  suspension off `MarketStatusId`. ([../ComponentDataFlow.md](../ComponentDataFlow.md))

## Translations

**Translations missing / showing keys** — walk the chain in this order:
1. **Is the row translated?** `SELECT * FROM TranslationCache2 WHERE STRING_ID = '<Prefix:Id>'`. An
   **empty string** (not NULL) in `TARGET_TEXT_ZH`/`_ES`/`_FR` means the external pipeline hasn't
   translated it yet. OEv2 never writes those columns.
2. **Did the row get seeded at all?** Seeding is gated by `Custom:Translation:CacheSeed` in
   `OE.Translation.Service` (default `false`; argocd sets it `true` in stg + prd).
3. **Did Adapter preload it?** `OE.Adapter.Service` loads the table **once at startup**
   (`TranslationCache.Preload()`). A row translated *after* the pod started is not picked up until a
   restart or an `ON_TRANSLATION_CHANGE`. Check for the startup warning — a blank
   `ConnectionStrings:Translation` skips the preload entirely (`Worker.cs:96-109`) and the service then
   publishes **no** `*Tr` at all.
4. **Was it gated out?** `OE.Sportsbook.Service.GateTranslations` suppresses a key's `*Tr` when they are
   unchanged since **that instance** last published them. An empty `*Tr` therefore means "unchanged", not
   "missing" — consumers must fall back to the English `*Na`. A restarted Sportsbook pod re-sends once.
5. **Mongo `*NaTr` empty?** Those are written only by `OE.Translation.Service` (update-only, with a short
   retry) — check its logs, and `BackfillActiveSnapshot` at startup. ([../MongoDb.md](../MongoDb.md))

- ⚠️ **Don't chase `sb:translation:*`** — that cache has been unpopulated since the 2026-07 redesign, and
  the `ON_DYNAMIC_TRANSLATION_CHANGE` purge is a no-op. The live change path is `ON_TRANSLATION_CHANGE`
  → `OE.Translation.Service`. ([../CacheInvalidation.md](../CacheInvalidation.md))
- **Static UI strings** (labels, not entity names) come from platform-web's own REST
  `v1/sportsbook/translations` (`GetTranslations()`) — a different system entirely.
- **Stale after a backoffice change:** the `OnDynamicTranslationChange` purge reaches every Sportsbook
  shard (`00–09`) via platform `CacheSync.Service`. **Known bug (fixed in platform-final 2026-06-28):**
  CacheSync relayed the bare boolean `true` instead of an `OnDeleteCacheMessage`, so OEv2 failed to
  deserialize it and the purge silently never ran → translations stayed stale/empty until the 60-min
  TTL (manifested as localized names reverting to base language on a push). Shard `10` is still not
  purged (fan-out loop `i<10`). ([../CacheInvalidation.md](../CacheInvalidation.md))
- ⚠️ If someone set a sub-60-min TTL, the `(int)…TotalHours` truncates to 0 → instant expiry → cache
  thrash. ([../TechnicalDebt.md](../TechnicalDebt.md) P2)

## Settlement / bets

**Settlement slow or selections not settling**
- The selection lookup `Find(PrFiId && MaTeId && SeTeId && SeSp)` is **unindexed** → collection scan
  on large Selection sets. ([../TechnicalDebt.md](../TechnicalDebt.md) P1)
- Settlement publishes per-selection to `r.oe.bet.settlement`; the **bet-level** outcome is
  platform's `Settlement.Service`. Check both. ([../SequenceDiagrams.md](../SequenceDiagrams.md) Flow 4)

**Duplicate-looking settlement messages / lost correlation**
- Settlement publishes use `Guid.NewGuid()` correlation ids (chain broken). ([../TechnicalDebt.md](../TechnicalDebt.md) M10)

## Persistence

**Mongo write seemingly succeeded but data missing**
- Connection uses **`w=0`** (unacknowledged) + `BulkWrite(IsOrdered=false)` → failures are silent.
  Check Mongo server logs, not just app logs. ([../PersistencePatterns.md](../PersistencePatterns.md))

**`OETranslation Error` with `Only acknowledged writes support the MatchedCount property`**
- The Translation.Service stamp path reads `MatchedCount`, and the driver throws on it when the write is
  unacknowledged (`w=0`). Dev logged this for every message with a translated fixture field, and the same
  text lands in `q.error` under `r.error.oe.translation`. Fixed by `WithAcknowledgedWrites` (GM-960); an
  older image needs an acknowledged sportsbook connection string instead.
  ([../TechnicalDebt.md](../TechnicalDebt.md) M14)

**Service crash-loops on startup**
- Sportsbook runs `MongoIndexBootstrapper` before `RunAsync` — **Mongo must be reachable** or it
  throws. This is intentional fail-fast. ([../StartupFlow.md](../StartupFlow.md))

## Messaging / connectivity

**Service not consuming**
- Check `MessageBroker:RoutingKey` bindings (`|`-separated, lowercased) vs the producer's key.
- TLS: port **5671 auto-enables TLS**; mismatch = connection failure.
- Exclusive queue? Name gets an MD5-hostname suffix — multiple replicas get separate queues.
- Connection retries 3× then throws `RetryableConnectionException`. ([../ImplementationPatterns.md](../ImplementationPatterns.md))

**Websocket pod is Running but pushes nothing; its log ends with `BackgroundService failed`**
- Pre-GM-791 signature (fixed 2026-09-03): `HttpRequestException: Connection refused
  (websocket-api.platform.svc:3000)` from `Worker.ExecuteAsync`, then the `StopHost` Fatal line, then
  silence. The host stopped but foreground dispatch threads kept the process alive, and there is no
  liveness probe to restart it. Since the fix the startup connect retries with backoff and the threads
  are background threads; if you still see this, the running image predates GM-791. Restart the pod and
  check the hub deployment. ([../ArchitectureRisks.md](../ArchitectureRisks.md) R2)
- A burst of `error ProcessFixtureSortOrder` / `ProcessBetBuilderMarketTemplates` /
  `ProcessMarketTemplatePlayerProps` stack traces at startup is the Platform API answering non-200 for
  some clients; since GM-791 these are single warnings carrying the API status. They do not stop the
  service.

**Messages pile up / redelivered repeatedly**
- A handler throwing → `BasicNack(requeue:true)` loops the poison message. Check error logs +
  `r.error.*` (the Adapter publishes `r.error.oe.adapter`). Idempotent upserts make safe retries safe, but a deterministic
  failure won't clear.

## Config / secrets

**Wrong env behavior / "it works locally but not in prod"**
- Committed `appsettings.json` = dev stubs; **prod = argocd env + AWS Secrets**. Read
  `argocd/apps/oddsengine/<service>/<env>.yaml`. ([../Configuration.md](../Configuration.md))

**Auth failures to Platform/BetGenius**
- Note the casing split `Custom:APIKey` vs `Custom:ApiKey` across services.
- BetGenius login path is hardcoded `Auth-v1/PROD/login` even with a configurable `Env` — possible
  env mismatch. ([../Unknowns.md](../Unknowns.md))

## Jobs

**LiveStream not updating stream URLs** — `Custom:Enabled` true? Only `ChannelType.BG` processed;
BetGenius token/`x-api-key` valid? 60s loop, 5-min backoff on error.
**Cleanup not deleting** — disabled by default (`Custom:Enabled=false`); only runs in the UTC window;
window can't cross midnight. Per-doc delete loop is slow.
**KeepAlive says healthy but things are broken** — `IsHealthy` is **hardcoded true**; it's a liveness
ping only. ([../TechnicalDebt.md](../TechnicalDebt.md) H4)

## Shutdown

**Pods don't drain / hang on shutdown** — several `while(true)` loops + Websocket threads ignore the
`CancellationToken`; Websocket may `Environment.Exit(1)`. Since GM-791 (2026-09-03) the Websocket
dispatch threads are background threads, so a stopped host lets the process exit; the loops still do
not observe cancellation. ([../TechnicalDebt.md](../TechnicalDebt.md) H2/M6)

## First-look checklist for any incident
1. Which **service + shard/client instance**? (sharding + per-client Websocket/UOF)
2. Trace it: OpenTelemetry trace spans the whole pipeline (header-propagated) → Tempo.
3. Metrics: `processed.messages`/`forwarded.messages`/`bulkwrite.duration`.
4. Logs: Compact JSON to stdout; `r.error.*` routing keys for handler errors.
5. Is it config (argocd) vs code vs infra (RabbitMQ/Mongo/RDS)?
