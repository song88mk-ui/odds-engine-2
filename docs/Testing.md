# Testing

How this repository tests itself, what the tests are actually worth, and how to measure that.

Baseline figures below were measured on 2026-09-10 against `main` at the time of writing. Re-measure
before quoting them; they are a snapshot, not a contract.

## Test inventory

Eleven xUnit test projects, all in `OddsEngine.sln`. Framework is xunit 2.5.3 (not v3). There is no
mocking library anywhere in the solution: test doubles are hand-written `Fake*` classes in
`OE.Shared.Test` (`FakeConfiguration`, `FakeMemCache`, `FakeMessageBroker`, `FakeHttpMessageHandler`,
`FakeModel`, ...), which the service test projects reuse via a project reference.

| Test project | Tests | Covers |
|---|---|---|
| `OE.Shared.Test` | 186 | Shared kernel: sharding helper, wire contracts, MemCache, MessageBroker, providers |
| `OE.Websocket.Service.Tests` | 165 | Client registry and presence, SignalR groups, odds maths, outgoing payload mapping |
| `OE.Job.BoostedOddsCap.Tests` | 123 | Cap alerts: header scanner, gap maths, client-odds repricing (hand-worked prices), alert tracker, evaluator, boost snapshot and EF mapping, toast and email digest, rate limiter, dispatcher, and the consumer through `FakeMessageBroker` (never throws, watchdog) |
| `OE.Translation.Service.Tests` | 102 | Translation persistence and the translation-only odds-change path |
| `OE.Adapter.Service.Tests` | 61 | Adapter routing, trading repository, translation cache |
| `OE.Sportsbook.Service.Tests` | 41 | Catalogue writes, translation gate, worker dispatch, dormant `OnMarketCancel` tripwire (GM-959) |
| `OE.Translation.Job.Tests` | 37 | Bet placement worker, cleanup worker, key builder |
| `OE.Job.Cleanup.Tests` | 21 | Orphan sweep: which fixture ids are swept, the delete filters, sweep interval, UTC window |
| `OE.Job.Lookup.Tests` | 20 | Lookup worker: dispatch, error and slow-message logging, consumer watchdog and status line, telemetry counter, duration histogram and spans (DEVOPS-443) |
| `OE.Job.KeepAlive.Tests` | 5 | Heartbeat worker |
| `OE.Job.Settlement.Tests` | 5 | Settlement worker |

All 766 are unit tests. None needs RabbitMQ, MongoDB or SQL Server running. Counts re-measured
2026-10-14 from a full `dotnet test` run; the previous table had drifted (its total was also one
below the sum of its rows). The `OE.Job.Lookup.Tests` row and the total were re-measured 2026-10-16 (DEVOPS-443). The `OE.Job.BoostedOddsCap.Tests` row and the total were added 2026-10-22; `FakeMessageBroker` now also records `CPublish` calls in `CPublished`.

```bash
# everything
dotnet test src/OddsEngine/OddsEngine.sln

# one project
dotnet test src/OddsEngine/OE.Shared.Test

# one test
dotnet test src/OddsEngine/OE.Shared.Test --filter FullyQualifiedName~HelperInstanceNameTests
```

CI runs the whole solution on every pull request (`.github/workflows/ci.yml`).

## Mutation testing

A passing test suite proves the tests run, not that they would notice a bug. Mutation testing
answers the second question: it edits the production code (flips a boolean, swaps an operator,
empties a string) and reports whether some test turns red. A mutant that survives is a change to
production behaviour that the suite would let through.

The tool is [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/introduction/). It is not a
package reference; it is a global .NET tool, installed per developer:

```bash
dotnet tool install -g dotnet-stryker
```

Shared settings live in `src/OddsEngine/stryker-config.json`. Stryker runs one project at a time,
launched from the corresponding test project directory:

```bash
cd src/OddsEngine/OE.Shared.Test
dotnet stryker --project OE.Shared.csproj
```

Substitute any pair from the inventory above, for example `OE.Sportsbook.Service.Tests` with
`--project OE.Sportsbook.Service.csproj`. The HTML report is written to `StrykerOutput/`, which is
git-ignored. Verified runtime on the shared kernel is about 40 seconds.

Nothing enforces a mutation score. `thresholds.break` is `0` in the config and no CI job runs
Stryker, so a low score is information, not a build failure. That is deliberate: the current scores
are far below any threshold worth setting, and a gate would only block unrelated work.

String mutations are deliberately left enabled. Routing keys and the abbreviated field names
(`FiId`, `MaStId`, `Od`, ...) are exactly the constants that must not drift silently, so mutating
them is signal rather than noise.

## Baseline scores, 2026-09-10

| Project | Score | Killed | Survived | No coverage |
|---|---|---|---|---|
| `OE.Job.Lookup` | 5.45% | 6 | 6 | 98 |
| `OE.Job.Settlement` | 6.74% | 6 | 31 | 52 |
| `OE.Sportsbook.Service` | 18.75% | 63 | 60 | 213 |
| `OE.Translation.Service` | 34.74% | 303 | 111 | 462 |
| `OE.Job.KeepAlive` | 38.71% | 10 | 10 | 9 |
| `OE.Shared` | 41.17% | 303 | 165 | 268 |
| `OE.Websocket.Service` | 43.56% | 565 | 281 | 451 |
| `OE.Adapter.Service` | 55.43% | 147 | 58 | 61 |
| `OE.Translation.Job` | 58.09% | 77 | 26 | 31 |

"No coverage" means no test executes that line at all, so the mutant cannot be killed by definition.
Across most projects it dwarfs the survivor count: the dominant problem is untested code, not weak
assertions in tested code.

Largest single gaps, in priority order:

1. `OE.Translation.Service`, 462 uncovered.
2. `OE.Websocket.Service`, 451 uncovered, down from 968 (GM-808). What is left is listed under
   "Websocket coverage" below.
3. `OE.Sportsbook.Service`, 213 uncovered, in the catalogue-write hot path.
4. `OE.Shared/Utilities/MessageBroker.cs`, 234 uncovered of 273. The publish and acknowledge paths
   are now tested; `Connect`, `ConnectWithManualAck`, `BindQueue`, `PrepareExternalConnection` and
   `GetConnection` all open real AMQP connections and would need an integration test with a live
   broker, which this repository does not have.

## Websocket coverage (GM-808)

`OE.Websocket.Service` went from 11.4% to 43.6%, and `SportsbookRepository.cs` alone from 10.6% to
54.5%. The service owns the SignalR wire contract that CLAUDE.md flags as a cross-repo breaking
change, and none of it was executed by a test before: not the group routing, not the client odds
maths, not the payload mapping.

The blocker was `HubConnection`. It is not sealed and `SendCoreAsync` is virtual, so
`OE.Websocket.Service.Tests/RecordingHubConnection.cs` subclasses it and records the published
payload with no hub, no transport and no network. That is what makes the four `ProcessMatchOddsChange*`
mappers testable. The private members are reached by reflection
(`SportsbookRepositoryInternals.cs`) rather than widened to `internal`, so no production code
changed; a renamed member fails the reflection lookup instead of silently passing.

Note the survivor count rose from 180 to 281 at the same time. That is expected and is not a
regression: mutants that were previously never executed are now executed, and the ones the
assertions do not detect land in "survived" instead of "no coverage". Real behaviour is covered;
some assertions are still weaker than they could be.

Still uncovered in `OE.Websocket.Service/Repository/SportsbookRepository.cs`:

| Member | Line | No coverage |
|---|---|---|
| `ProcessMatchOddsChangeForAllMarketsOE` (bet-builder block) | 1779 | 54 |
| `ProcessMatchOddsChangeForAllMarkets` | 1704 | 24 |
| `ProcessMarketCount` | 300 | 19 |
| `GetFixtureDetails` (HTTP error branches) | 715 | 17 |
| `ProcessWebPropsChange` / `ProcessCashOutChange` / `ProcessWalletChange` | 602 / 197 / 222 | 31 |

`Worker.cs` was not touched by GM-808 and still carries 161 uncovered and 85 surviving mutants,
mostly in the `MessageReceivedEvent` dispatch switch (`Worker.cs:620`).

Three defects surfaced while writing these tests. They were pinned by test but deliberately not
fixed there, since GM-808 changed no production code; later tickets fixed all three:

1. **GM-810** (fixed, PR #66). Both OE mappers dereferenced the per-selection `ClientOdds` returned by
   `FirstOrDefault` with no null check. `GetClientOdds` returns an empty list when the market's
   `ClCoMa` is empty or does not name the client, so the mapper threw, the caller's `catch`
   swallowed it, and the whole fixture update was dropped rather than that one market. Now, when
   `GetClientOdds` returns empty, `SportsbookRepository.cs:1537` (main markets) and `:1836` (all
   markets) log one warning per market and publish that market with **source odds**, no client
   margin, via `BuildSourceOddsFallback` (`:1172`). The rest of the fixture publishes normally.
   Pinned by `MainMarketsOE_MarketWithNoClientOddsConfiguration_PublishesWithSourceOddsAndLogsOnce`
   and `AllMarketsOE_MarketWithNoClientOddsConfiguration_PublishesWithSourceOddsAndLogsOnce`.
   **None of this runs in a deployed environment today.** The OE mappers only run when
   `Custom:IsOEEnabled` is true, and `argocd/apps/oddsengine/websocket-service/{dev,stg,prd}.yaml:44`
   all set `Custom__IsOEEnabled: "false"`, as they have since each environment was created. The
   client margin is not enforced at bet placement either: platform-final's
   `Platform.Sportsbook.API/Repository/SportsbookRepository.cs` re-reads the selection from Mongo
   and compares the bet against the raw `Od` (`GetBetSelectionStatus`, `:7059`), because its own
   `ClCoMa` repricing sits behind `IsOEEnable`, which always returns `false` (`:11758`). Market
   status, selection `IsAc` and bet stop are checked there too, so a stale UI cannot place a bet
   on a market that Mongo already shows as suspended. Turning OE pricing back on is a cross-repo
   change, see [ArchitectureRisks.md](./ArchitectureRisks.md) R10.
   Two things are still open, and both only matter if OE pricing is switched back on:
   - **The fallback choice is not confirmed by trading.** Source odds show the client a price with
     no client margin, which is the price platform-final accepts today anyway. Skipping the market
     instead would freeze it on screen, because platform-web merges odds-change payloads per
     market (`platform-web` `BasePage.cs:677` `UpdateMarketsAndSelections`), so a market
     suspended upstream would stay active at stale odds. Publishing the market as suspended is a
     third option.
   - **Defect, pinned not fixed.** The per-selection guard at `:1547` and `:1846` builds its
     fallback from a one-item list, so `isBinary` is always `true` and a three-way market gets the
     wrong Malaysian, Hong Kong and Indonesian odds. PR #66 calls the guard unreachable, but it is
     reachable for the pseudo-client `OE`: `GetClientOdds` prices every client named in `ClCoMa`,
     none of those entries carry `OE`, so the list is not empty and no warning is logged, yet the
     per-selection lookup finds nothing. Whether a live client is registered as `OE` is unknown.
     Pinned by `MainMarketsOE_OEClientThreeWayMarket_PerSelectionFallbackTreatsItAsBinary`.

   Follow-up tests cover `ClCoMa` naming only another client, the `TSOX`-for-`TSO` substring case,
   binary and three-way fallback formats, a fixture mixing a configured and an unconfigured market
   (both mappers), a market with no selections, the `OE` per-selection path (both mappers),
   per-selection matching with distinct prices (both mappers), and `BuildSourceOddsFallback`
   directly. Stryker on the lines PR #66 changed (`SportsbookRepository.cs` `1164-1187`,
   `1535-1548`, `1834-1847`) went from 7 killed, 12 survived and 5 no-coverage to 22 killed,
   0 survived and 2 no-coverage. The two left are the `c.SportId == message.SpId` player-props
   filter on the unchanged `GetClientOdds` call lines (`:1535`, `:1834`), which no test reaches
   because no test caches player-props templates. The whole file went from 56.98% to 59.95%.
2. **GM-811** (fixed, both halves). The asian-odds branch in `GetClientOdds` was guarded by an empty
   `_MaTeIds`, so its mutants could never be covered. It is now removed, along with the
   `Custom:AsianClientCodes` key that only that branch read, and
   `GetClientOdds_AsianClient_GetsMarginLikeAnyOtherClient` pins that an Asian client is priced like
   any other client. Restoring it was never an option: the trigger (sport 1, market templates
   16/18/66/68) is Betradar-era and cannot match a Genius Sports id.
   `IsVirtualSportId` went the other way and was **populated**, because virtual sports are live:
   Virtual Cricket is Genius Sports sport **11205870**, already acted on in platform-web
   (`Utility2.cs`) and backoffice-final (`SportHelper.cs`) and configured in the Sportsbook
   market-template lookup. That makes six branches in `OE.Websocket.Service` and one in
   `OE.UOF.Client` reachable for the first time. `OE.Shared.Test/Extensions/IntExtensionsTests.cs`
   pins the single valid id and the ids that must not creep in (the previous provider's 1001-1058,
   and 11205863, which is real 3x3 Basketball in the same id block).
   `ProcessTopFixtures_VirtualSport_SkipsEntirely` and `ProcessTopLeagues_VirtualSport_SkipsEntirely`
   cover two of the six call sites; the remaining four are reachable but still untested.
3. **GM-812** (fixed, PR #65). `GetGroupsForMainMarkets` and `GetGroups` appended the league and
   fixture favorites groups to a non-concurrent `List<string>` from inside `Parallel.ForEach`, which
   could drop a user's `onfavorites:{cc}:{userId}` group or throw during the list's resize. The four
   loops (`SportsbookRepository.cs:1358`, `:1368`, `:1458`, `:1468`) are now plain `foreach`.
   `MainMarkets_LargeLeagueAndFixtureFavoritesLists_AddEveryUserWithNoDataRace` and
   `BetStop_LargeLeagueAndFixtureFavoritesLists_AddEveryUserWithNoDataRace` build 20,000 league and
   20,000 fixture favorites and assert exactly one group per user. With the fix reverted, both
   failed on 3 of 3 runs. They detect the race by probability, not by a forced interleaving.
   Stryker on the whole file (2026-10-05, branch head, 59.77%; line numbers as of that run): on
   the changed lines (`1335-1348`, `1435-1448`) the 4 `== null` checks, the 4 `result.Add` statement removals and the
   4 group-string mutants are all killed. The 8 block removals are "Removed by block already
   covered filter". Nothing survived. Stryker has no mutator that turns `foreach` back into
   `Parallel.ForEach`, so the score cannot show the race is guarded; only the revert check above
   does. It also does not mutate `userId.ToLower()`, so no test is shown to depend on the
   lowercasing. One mutant survives next to the change, on an unchanged line: `:1427`
   `topFixtures.FiIds.Any` to `All` in `GetGroups`, because no test caches a top-fixtures list
   with more than one id.

   A plain `foreach` over a `List<T>` throws `Collection was modified` if another thread changes
   the list mid-loop, and the favorites lists were changed in place: `ProcessFavorites` and
   `ProcessRemoveFavorites` called `.Add`/`.Remove` on the cached `UserIds` from the
   `ws-favorites` and `ws-remove-favorites` threads, while `GetGroups` (RabbitMQ consumer thread)
   and `GetGroupsForMainMarkets` (`ws-main-markets-odds`) enumerated the same list, since
   `MemCache.GetCache` returns the cached object itself. A throw there drops the whole
   `PublishBetStopFixture` or main-markets push for that client. Both writers now store a new
   `Favorites` with a copied list, so a cached list never changes after it is stored and the
   readers need no lock. Both writers also take `_favoritesLock` (`SportsbookRepository.cs:43`)
   around the read-copy-write, which also fixes an older lost update between the two writer
   threads. `ProcessFavorites_ExistingKey_DoesNotMutateCachedList` and
   `ProcessRemoveFavorites_ExistingUser_DoesNotMutateCachedList` pin the copy deterministically;
   `ProcessFavoritesAndRemove_Concurrent_LosesNoUpdate` races 2,000 adds against 2,000 removes on
   one key. Against the old in-place code all three failed on 3 of 3 runs; with the copy but no
   lock, the concurrent test failed on 3 of 3 runs. The concurrent test detects the race by
   probability, like the two above.

## Why there are no `*MutationCheck` tests any more

An earlier pass (GM-797) added 383 tests suffixed `MutationCheck`, 44% of the suite. Each re-ran an
existing assertion against a deliberately wrong value, wrapped so that the wrong value failing is
what made the test pass:

```csharp
var wrong = expected == "00" ? "09" : "00";
Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(wrong, _helper.GetInstanceName(id)));
```

The intent was to prove an assertion is not vacuous. The effect is different: the test passes for
any implementation whose output differs from one hand-picked constant, so no mutation of production
code can make it fail.

That was measured rather than argued. Every project was run through Stryker twice, once with those
tests and once with them stripped out, and then a third time in the repository after they were
deleted for real. Every project scores the same with the tests present and with them gone.

One reading needed a second look: in the stripped-copy pass `OE.Adapter.Service` came out at 55.06%
against 55.43% with the tests present, which looked like a small real contribution. It was not. With
the tests actually deleted the project scores 55.43% again, reproduced three times in a row, so the
55.06% was an artifact of that throwaway copy. Deleting all 383 tests changed no mutant's status in
any of the nine projects.

The lesson generalises: a test that cannot fail when production code changes is not evidence of
quality, however green it looks and however many there are of it. Before adding tests to raise a
count, run Stryker and check the count is buying killed mutants.
