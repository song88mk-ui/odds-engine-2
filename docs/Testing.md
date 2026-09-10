# Testing

How this repository tests itself, what the tests are actually worth, and how to measure that.

Baseline figures below were measured on 2026-09-10 against `main` at the time of writing. Re-measure
before quoting them; they are a snapshot, not a contract.

## Test inventory

Nine xUnit test projects, all in `OddsEngine.sln`. Framework is xunit 2.5.3 (not v3). There is no
mocking library anywhere in the solution: test doubles are hand-written `Fake*` classes in
`OE.Shared.Test` (`FakeConfiguration`, `FakeMemCache`, `FakeMessageBroker`, `FakeHttpMessageHandler`,
`FakeModel`, ...), which the service test projects reuse via a project reference.

| Test project | Tests | Covers |
|---|---|---|
| `OE.Shared.Test` | 174 | Shared kernel: sharding helper, wire contracts, MemCache, MessageBroker, providers |
| `OE.Translation.Service.Tests` | 92 | Translation persistence and the translation-only odds-change path |
| `OE.Adapter.Service.Tests` | 54 | Adapter routing, trading repository, translation cache |
| `OE.Sportsbook.Service.Tests` | 40 | Catalogue writes, translation gate, worker dispatch |
| `OE.Websocket.Service.Tests` | 38 | Client registry and presence, outgoing message shapes |
| `OE.Translation.Job.Tests` | 37 | Bet placement worker, cleanup worker, key builder |
| `OE.Job.Lookup.Tests` | 7 | Lookup worker |
| `OE.Job.KeepAlive.Tests` | 5 | Heartbeat worker |
| `OE.Job.Settlement.Tests` | 5 | Settlement worker |

All 452 are unit tests. None needs RabbitMQ, MongoDB or SQL Server running.

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
| `OE.Websocket.Service` | 11.41% | 142 | 181 | 968 |
| `OE.Sportsbook.Service` | 18.75% | 63 | 60 | 213 |
| `OE.Translation.Service` | 34.74% | 303 | 111 | 462 |
| `OE.Job.KeepAlive` | 38.71% | 10 | 10 | 9 |
| `OE.Shared` | 41.17% | 303 | 165 | 268 |
| `OE.Adapter.Service` | 55.43% | 147 | 58 | 61 |
| `OE.Translation.Job` | 58.09% | 77 | 26 | 31 |

"No coverage" means no test executes that line at all, so the mutant cannot be killed by definition.
Across most projects it dwarfs the survivor count: the dominant problem is untested code, not weak
assertions in tested code.

Largest single gaps, in priority order:

1. `OE.Websocket.Service`, 968 uncovered mutants. Owns the SignalR wire contract, which CLAUDE.md
   flags as a breaking change to touch, and it is the least covered service in the repo.
2. `OE.Translation.Service`, 462 uncovered.
3. `OE.Sportsbook.Service`, 213 uncovered, in the catalogue-write hot path.
4. `OE.Shared/Utilities/MessageBroker.cs`, 234 uncovered of 273. The publish and acknowledge paths
   are now tested; `Connect`, `ConnectWithManualAck`, `BindQueue`, `PrepareExternalConnection` and
   `GetConnection` all open real AMQP connections and would need an integration test with a live
   broker, which this repository does not have.

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
