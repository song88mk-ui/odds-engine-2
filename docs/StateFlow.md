# State Flow (End-to-End)

How state is held and propagated from the provider feed all the way to the browser. Tags: **[V]**
odds-engine-2, **[Vpw]** platform-web, **[Vpf]** platform-final.

## The state ladder (where "the truth" lives at each hop)

| Layer | State holder | Kind | Authority |
|---|---|---|---|
| Provider | bet-genius / UOF | transient messages | upstream |
| OEv2 ingress | RabbitMQ queues | durable in-flight | broker |
| **OEv2 catalogue** | **MongoDB Sportsbook** | **durable, system of record for catalogue** | **OEv2 [V]** |
| OEv2 reference | SQL `Translation` (names), SQL `Trading` (client feed config) | durable | external/ops [V] |
| OEv2 in-process | `_IsBetStopGlobally` flag, MemCache (`sb:translation:*`, `bg:*`), UOF monthly lists | volatile per-process | OEv2 [V] |
| Transport | RabbitMQ message payloads (carry a snapshot of fixture/market/selection) | transient | message [V] |
| platform read | (none persisted — reads Mongo on demand; 30 s client cache) | cache | platform [Vpf] |
| **UI server state** | platform-web circuit state (fixture lists, BetSlip, OperatorState…) | **volatile per circuit** | platform-web [Vpw] |
| Browser | DOM (projection of UI server state) | rendered | platform-web [Vpw] |

> The catalogue's **source of truth is MongoDB (OEv2)**. Everything downstream is a **cache or
> projection** of it (platform read-cache → circuit state → DOM). A push message carries a *current
> snapshot*; the UI applies it as a delta to its in-memory copy.

## 1. OEv2-side state — [V]

- **MongoDB Sportsbook** — the durable catalogue (Fixture/Market/Selection/BettingStatus/Lookups).
  Idempotent upserts keyed by natural id; per-fixture consistency guaranteed by the **sticky shard**
  (one Sportsbook instance owns a fixture's docs). See [EntityLifecycle.md](./EntityLifecycle.md).
- **Global bet-stop** — `BettingStatus("betstop").IsBeSo` (durable) mirrored into the in-memory
  `_IsBetStopGlobally` flag at startup and on `OnBetStopGlobal`. ⚠️ Not thread-safe (TechnicalDebt H3).
- **MemCache (in-process)** — translations (`sb:translation:*`, 60 min + negative cache),
  BetGenius auth/fixtures (`bg:auth`, `bg:fixture:*`). Volatile; rebuilt on miss; purged by
  `OnDynamicTranslationChange`.
- **UOF.Client in-memory** — client config + live-fixture lists, **reset monthly by wall-clock**
  (TechnicalDebt M12).
- **No shared distributed cache** — each process caches independently; bet-stop state is the only
  cross-instance coordinated state (via the dedicated shard 10 + Mongo).

## 2. Message-carried state — [V]
Each `OnOddsChange`/`OnBetStop*`/settlement message carries a **self-contained snapshot** (full
fixture→market→selection subtree, or the specific flag). There is no incremental "patch protocol" on
the wire — the consumer overwrites its copy. This makes consumers stateless w.r.t. history but means
**missed messages are not recoverable by replay** (a gap is healed only by the next snapshot or a
read-path refresh).

## 3. Platform-web circuit state — [Vpw]

State containers live **per Blazor circuit** (per browser tab), all extending `BaseState<T>` which
implements `INotifyPropertyChanged`; assigning `.Model` fires `PropertyChanged`, which the layout
subscribes to and turns into `InvokeAsync(StateHasChanged)`:

| Container | Holds | Lifetime |
|---|---|---|
| `OperatorState` | client/operator session (ClientId, token, language, currency) | circuit (rebuilt on lang/currency/token change) |
| `TranslationState` | `Dictionary<string,string>` of localized strings | circuit (30 s fetch cache) |
| `PageState` | current URL, layout, scope | circuit |
| `DeviceState` | screen size / device | circuit |
| Instance fields on `Index`/`BasePage` | `PrematchLeagueFixtures`, `LiveLeagueFixtures`, `FavoriteLeagueFixtures`, `OutrightsFixtures`, `FixtureDetailsModel`, `BetSectionModel` (bet slip + MyBets) | circuit |
| `ConnectedUser` (scoped) / `ConnectedUserList` (singleton) | per-circuit user + global active-circuit count (cap 300) | circuit / process |

**Update sources into circuit state:**
- **Push** (Flow 1/4/5): hub handlers mutate the fixture lists / bet slip / balance, bump `Counter`
  fields, set `RenderComponent=true`, call `StateHasChanged`.
- **Read** (Flow 2/3): REST results assigned into state containers / instance fields.

**Persistence vs reset on navigation [Vpw]:**
- **Reset** in `LoadPage()`: fixture lists, league filters, boosted odds, current sport/fixture ids.
- **Preserved** for circuit lifetime: `BetSectionModel` (bet slip + MyBets), `OperatorState`,
  `#CashoutChange`/`#WalletChange` subscriptions.
- **Survives transport blip** (reconnect): entire circuit state retained; only the socket reconnects.

## 4. The propagation chain (one odds tick)
```
Mongo write (OEv2)          ← durable truth
  └ RabbitMQ snapshot       ← transient copy
     └ hub fan-out          ← routed to group
        └ circuit field update + Counter++   ← volatile UI copy (server)
           └ PropertyChanged/StateHasChanged ← render trigger
              └ Blazor diff → /_blazor → DOM ← browser projection
```

## 5. Identity used to scope state
- **Catalogue scope:** `SpId/SegId/LeId/FiId/MaId/SeId` (denormalized everywhere).
- **Delivery scope (hub groups):** `endpoint:{clientCode}:sports:{SpId}:…`, `fixturedetails:{FiId}`,
  `onbetstatus:{cc}:{userId}`, `oncashoutchange:{cc}:{userId}`, `onwalletchange:{cc}:{userId}`,
  `onfavorites:{cc}:{userId}` — **clientCode** (tenant) + the catalogue id or **userId**. [V/Vpw]
- **UI list keys:** fixtures matched by `FixtureId`; markets/selections by `MaId`/`SeId`; bets by
  `BeId`. `Counter` fields force child-component change detection. [Vpw]

## 6. Consistency characteristics
- **Eventually consistent, snapshot-based.** No global ordering across fixtures; per-fixture ordering
  via sticky shard. Idempotent upserts tolerate redelivery. UI is best-effort live with read-path
  fallback. Missed push during disconnect → stale until next snapshot/navigation [Vpw].
- **Multi-tenant isolation** is by clientCode in both the catalogue (`ClCo`/`ClCoMa`) and the
  delivery groups. See [BusinessRules.md](./BusinessRules.md) BR-16.
