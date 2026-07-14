# Authorization

## Applicability — odds-engine-2 has NO authorization layer

[Verified — by absence.] There is **no `[Authorize]`, no authorization policies, no roles, no
`AddAuthorization`, no `IAuthorizationHandler`, no claims-based checks** anywhere in
`src/OddsEngine`. Authorization is a concept for gating *callers* of an application's
endpoints/resources; OEv2 workers have **no callers** and **no endpoints** — there is nothing to
authorize. (Inbound user/operator authorization lives in **platform-final**/**platform-web**; see
`platform-web/docs/Authorization.md` and Phase 1 [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md).)

This document records the **trust model** and the **business-rule "gates"** that play an
authorization-like role, so the absence is understood rather than assumed.

## Trust model — implicit, network/topology-based

OEv2 operates inside a **trusted boundary**:
- Services trust any message that arrives on their RabbitMQ queue (delivery implies the publisher
  was authenticated to the broker and bound to the right routing key). There is **no per-message
  authorization** — no signature, no allow-list of senders, no claim checks on `BasicProperties`.
- Trust is enforced **outside the app**: broker credentials + vhost isolation (the external
  white-label feed uses a separate vhost `ext`), Kubernetes network policy, and AWS account
  boundaries. See [Hosting.md](./Hosting.md) / [Configuration.md](./Configuration.md).
- The **only** boundary where OEv2 presents authorization-style credentials is *outbound*
  (`x-api-key`, bearer token) — covered in [Authentication.md](./Authentication.md), not here.

## "Authorization-like" gates that DO exist (business rules, not security)

These are functional gates, not access control, but they are the nearest analogue to
"is this action allowed":

| Gate | Service | Effect | Where |
|---|---|---|---|
| `Custom:Enabled` | LiveStream, Cleanup | job loop runs only if true (Cleanup default false) | Worker loop |
| `Custom:EditSportsbook` | Settlement | only mutate/publish if true | Settlement repo |
| **UTC time window** (`UtcWindowHours`) | Cleanup | deletes permitted only within the window | Cleanup Worker |
| **Global bet-stop** (`_IsBetStopGlobally` / `BettingStatus`) | Sportsbook | suppresses betting activity when set | Sportsbook |
| **Per-fixture bet-stop** (`IsBetStop`/`IsSu`) | Sportsbook | pauses a fixture's markets | Sportsbook |
| **Client config** (`ClientOddsFeedConfiguration`: LiveOdds/PrematchOdds/MaxFixtures) | UOF.Client | which sports/fixtures a client is *entitled* to in the outbound feed | UOF.Client `IsMessageValid`/validation — **currently commented-out (disabled)** |
| **Client-code masking** (`ClCo`/`ClCoMa` on markets, `AsianClientCodes`) | Sportsbook/Websocket | restricts which clients see a market / odds format | document fields + Websocket filtering |

> ⚠️ The UOF.Client entitlement/validation gate (the closest thing to per-client authorization of
> feed content) is **disabled** — the validation and `MaxFixtures` enforcement blocks are commented
> out (`ClientRepository`/`Worker`). So client entitlement is **not currently enforced** in the
> outbound feed. Tracked in [TechnicalDebt.md](./TechnicalDebt.md) (H5) and [Unknowns.md](./Unknowns.md).

## Policies / roles / claims
**None.** [Verified — absence.] No policy definitions, no role constants, no claim transformations.

## Risk note
Because there is no per-message authorization, the security posture depends entirely on the broker
and network being correctly locked down. A misconfigured binding or a compromised publisher would
be implicitly trusted. This is a standard trade-off for internal event-driven systems but is worth
stating explicitly for anyone extending the topology (e.g., exposing a queue to a less-trusted
producer). Also note the **abbreviated trust on the bet-stop flag** (non-thread-safe `_IsBetStopGlobally`,
H3) is a correctness risk that intersects with the "is betting allowed" gate.

## Summary
- **App-level authorization:** none (not applicable to headless workers).
- **Effective authorization:** infrastructure trust boundary (broker creds, vhost, network) +
  business gates (feature flags, bet-stop, client config — the last currently disabled).
- If inbound endpoints are ever added to OEv2, an authorization model must be designed fresh;
  reuse platform-final's `x-api-key`/operator-token approach.
