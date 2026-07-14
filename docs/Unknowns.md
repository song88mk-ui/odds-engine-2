# Unknowns

Open questions that could **not** be verified from available source/config. **Do not treat any as
fact.** Phase-10 format: each has Evidence/Files, current best guess + Confidence, and Suggested
investigation. Tags: [Likely] = plausible but unverified; [Unknown] = no basis to guess.
Companions: [KnowledgeValidation.md](./KnowledgeValidation.md) (assumptions), [ArchitectureRisks.md](./ArchitectureRisks.md).

## Message flow & topology
| # | Question | Evidence / Files | Best guess (conf.) | Suggested investigation |
|---|---|---|---|---|
| 1 | Who produces `r.uof.*`? | Adapter binds `r.uof.#` (argocd `r.bg.receiver.#|r.uof.#`); no producer in analyzed repos | `oddsfeed`/legacy `oddsengine` [Likely, Low] | grep `oddsfeed`/`oddsengine` for `r.uof.` publishes |
| 2 | What is `r.of.recovery.#.tso`? | UOF.Client binds it | oddsfeed recovery/replay for client TSO [Likely, Low] | find the `r.of.*` producer |
| 3 | Is `r.bg.receiver.onfixturecchange` a typo, and does any consumer bind it? | `OE.Job.Settlement` publish | typo for `onfixturechange` [Likely, Med] | grep consumers for the exact string |
| 4 | Does Adapter consume both `r.bg.receiver.#` and `r.uof.#` in all envs? | argocd dev yes; committed appsettings shows only `r.uof.#` | yes in prod [Likely, Med] | read stg/prd argocd values |

## Sharding & deployment
| # | Question | Evidence / Files | Best guess (conf.) | Suggested investigation |
|---|---|---|---|---|
| 5 | 11-shard fleet (00–10) in stg/prd? | dev confirmed in argocd; math → 00–09 + global 10 | same in stg/prd [Likely, Med] | read `apps/oddsengine/sportsbook-service/{stg,prd}.yaml` |
| 6 | Is digit-suffix distribution balanced? | `(last/2)*2 + secondLast%2` | depends on id uniformity [Unknown] | measure shard load in prod |
| 16 | Local dev orchestration exists? | no docker-compose in-repo | [Unknown] | check infra repo / team docs |
| 17 | Prod Mongo addressing | dev in-cluster; stg `10.49.100.55` | prd same IP [Likely, Med] | read prd argocd values |
| 18 | bet-genius consumes `OE.Shared` via NuGet or ProjectRef? | shares the types | NuGet (GitHub Packages) [Likely, Med] | inspect bet-genius csproj |

## Terminology / enums
| # | Question | Evidence | Best guess (conf.) | Suggested investigation |
|---|---|---|---|---|
| 7 | "UOF" = Unified Odds Feed? | outbound `uof.*` + sibling `oddsfeed` | yes [Likely, Med] | internal docs / oddsfeed README |
| 8 | `ChannelType` EM=1 / OD=2 providers? | enum; BG=0 confirmed (BetGenius) | [Unknown] | provider config / team |

## Behavior / correctness
| # | Question | Evidence / Files | Best guess (conf.) | Suggested investigation |
|---|---|---|---|---|
| 9 | UOF.Client validation: intentionally off or unfinished? | commented `IsMessageValid`/cap logic | unfinished [Likely, Med] | ask owners; check ticket history |
| 10 | Where is the fixture actually marked closed? | Settlement publishes but doesn't write Mongo (M8) | downstream consumer writes it [Likely, Low] | trace `OnFixtureChange`(close) consumers |
| 11 | Commented translation `.Set()`s — deferred or abandoned? | `SportsbookRepository.cs:386-398,…` | deferred multi-language [Likely, Med] | confirm whether doc-level `*Tr` ever needed |
| 23 | ~~How does `OnDynamicTranslationChange` reach Sportsbook (+ all shards)?~~ **RESOLVED 2026-06-28** | platform `CacheSync.Service` fans out to `r.oe.adapter.ondynamictranslationchange.{00..09}`; OEv2 binds `r.oe.adapter.#.NN` (argocd) → matches. Real defect was the relay **payload** (`true` instead of `OnDeleteCacheMessage`), fixed in platform-final; shard `10` still uncovered (loop `i<10`). See [CacheInvalidation.md](./CacheInvalidation.md). | — | — |
| 12 | Exact `OnDeleteCacheMessage` producer→consumer path | platform `CacheSync.Service` binds `r.oe.cachesync.#`; OEv2 handles `OnDynamicTranslationChange` | backoffice→platform + OEv2 [Likely, Med] | map producers/consumers per key |

## Cross-repo
| # | Question | Evidence / Files | Best guess (conf.) | Suggested investigation |
|---|---|---|---|---|
| 13 | `Outgoing*` SignalR DTO parity OEv2↔platform-web | both define `EngineMessages`/`Messages` | assumed identical [Med] | diff the two model sets |
| 14 | Any Mongo writer besides OEv2? | platform/backoffice read (secondary) | OEv2 only writes [Likely, Med] | grep siblings for Mongo writes |
| 15 | Auth repo role in OEv2 path | none found (grep) | none [High that none] | (closed — Auth not an OEv2 dep) |
| 19 | Where is the `/platform` hub hosted? | platform-web docs contradict; hub `.cs` in neither repo | separate gateway `Platform.Websocket.API2` [Med] | locate the hub host project |
| 20 | Betslip/top-markets odds hub routing | platform-web handles them; OE routing unconfirmed | via `PublishOddsChangeForAllMarkets` + group [Likely, Low] | trace OE.Websocket group routing |
| 21 | `r.bet.*`/`r.wallet.*` producers | OEv2 relays; produced by platform | platform BetStatus/CashOut svcs [Likely, Med] | confirm in platform-final config |
| 22 | Azure SignalR backplane used? | platform-web `EnableAzureSignalRService` flag | maybe in some env [Unknown] | check gateway deploy + connection string source |
| 24 | backoffice inline `OE.Shared` schema drift | backoffice carries inline copies | may drift [Unknown] | diff backoffice `Data/Sportsbook` vs `OE.Shared` |
| 25 | `r.bo.config.*` consumer | backoffice publishes | platform/config worker [Unknown] | grep platform for the binding |
| 26 | Does OEv2 read SQL `Sportsbook` order/template DB? | OEv2 known SQL reads = Trading + Translation only — **(2026-06-30) Translation read path now removed; OEv2 SQL reads = Trading only** | no (platform applies it) [Likely, Med] | grep OEv2 for that context |
| 30 | **Does platform-web consume the websocket message `*NaTr`, or rely solely on REST `GetTranslations()`?** | OEv2 removed `*NaTr` from `OutgoingOddsChange`/`OutgoingBoostedOddsChange` (2026-06-30); platform-web mirror `OddsChangeFixtureMessageModel` + its REST `TranslationState` path both exist | likely REST-only (the REST path is the documented UI translation source) [Likely, Med] | inspect platform-web odds-change handler — does it read `*NaTr` and render it, or only `GetTranslations()`? Determines UI impact of removing `*NaTr`. See [ArchitectureRisks.md](./ArchitectureRisks.md) R9 |

## Config / external
| # | Question | Evidence / Files | Best guess (conf.) | Suggested investigation |
|---|---|---|---|---|
| 27 | BetGenius `Auth-v1/PROD/login` hardcoded vs configurable `Env` | `BetGeniusRepository.cs:69` vs `:147` | env mismatch risk [Med] | confirm per-env login URL |
| 28 | Rotation status of committed creds (H1) | committed in appsettings | treat as compromised [High] | rotate + audit |

## Validation / quality
| # | Question | Evidence | Best guess (conf.) | Suggested investigation |
|---|---|---|---|---|
| 29 | Are there tests anywhere? | no test project in-repo | tests absent or external [Unknown] | search workspace for OEv2 tests |
| 30 | Are the 6 `SportsbookRepository` copies intentionally divergent? | 6 files (validated) | per-service divergence [Likely, Med] | diff the copies |

## How to close most of these
(a) Read `argocd/apps/oddsengine/<service>/{stg,prd}.yaml` (closes #4,5,17,23, parts of #19/#22).
(b) Grep `oddsfeed`/`oddsengine` siblings for `r.uof.`/`r.of.` (closes #1,#2).
(c) Diff shared DTOs + backoffice inline schema (closes #13,#24,#30).
(d) Ask original engineers about disabled features (#9,#10,#11).
