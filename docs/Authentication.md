# Authentication

## Applicability — odds-engine-2 has NO inbound user authentication

[Verified — by absence.] There is **no JWT, no cookies, no OpenID Connect, no
`AddAuthentication`, no claims, no token refresh, no identity** anywhere in `src/OddsEngine`.
These services are **headless workers** with no human/web callers — there is nothing to
authenticate *into* them. Grep confirms no `AddAuthentication`/`JwtBearer`/`AddCookie`/`OpenIdConnect`/
`[Authorize]`/`ClaimsPrincipal` usage.

> Inbound user authentication (operator token, `x-api-key`, OGAuth signatures, OIDC) lives in
> **platform-final** (the API) and **platform-web** (the UI). See `platform-web/docs/Authentication.md`
> and Phase 1 [CrossRepositoryDependencies.md](./CrossRepositoryDependencies.md). This document
> covers what authentication *does* exist in OEv2: **outbound/service-to-service credentials**.

## The real authentication surface: outbound service credentials

OEv2 services **authenticate as clients** to the infrastructure and external APIs they call. This
is the "auth" you must manage.

| Target | Mechanism | Where | Notes |
|---|---|---|---|
| **RabbitMQ** (internal `e.central`) | username/password (+ TLS when port 5671) | `MessageBroker.Connect` reads `MessageBroker:Username/Password` (`MessageBroker.cs:118-119`) | Prod creds from AWS Secrets Manager via env (`MessageBroker__Password`) |
| **RabbitMQ** (external, vhost `ext`) | username/password | `ExternalConnect` reads `ExternalMessageBroker:*` | UOF.Client only |
| **MongoDB** (Sportsbook) | connection-string creds (`Custom:MongoDB-Sportsbook`) | `MongoClient(...)` | Prod conn string from secrets |
| **SQL Server** (Translation) | conn-string `User Id`/`Password` | `ConnectionStrings:Translation` (`Program.cs:50`) | RDS, `TrustServerCertificate=True` |
| **SQL Server** (Trading) | conn-string creds (`Custom:Trading`) | UOF.Client `TradingRepository` | — |
| **Platform REST API** | header **`x-api-key`** (+ `x-client-id` in Websocket) | UOF.Client / Websocket repositories | static API key from config/secret |
| **BetGenius Video API** | **login → bearer token + `x-api-key`** | `BetGeniusRepository` (below) | the only real token flow in OEv2 |

## The one token flow: BetGenius authentication — [Verified]

`OE.Shared/Repositories/BetGeniusRepository.cs` implements a classic token-acquire-then-bearer
pattern (used by `OE.Job.LiveStream` via `BetGeniusLiveStream`):

1. **Acquire token** (`GetBetGeniusAuthorizationToken`, `:41`):
   - `POST Auth-v1/PROD/login` with `{ user, password }` from `Custom:BetGenius:User/Password`.
   - Response → `BGAuth { IdToken, ExpiresIn }`.
   - **Cached** in MemCache under key `bg:auth` with TTL = `ExpiresIn` seconds (`:84`) — i.e.
     token refresh is *cache-expiry driven* (re-login when the cached token expires). On error the
     cache entry is deleted so the next call retries. This is the closest thing to "token refresh."
   - ⚠️ The login path is hardcoded `Auth-v1/**PROD**/login` even though `Custom:BetGenius:Env`
     exists and is used for the fixtures path (`Video-v3/{env}/...`). Possible env-mismatch —
     see [Unknowns.md](./Unknowns.md).
2. **Use token** (`GetBetGeniusFixture(s)`, `:127/:227`):
   - Headers `Authorization: Bearer {token}` + `x-api-key: {Custom:BetGenius:Key}`.
   - `GET Video-v3/{env}/fixtures...`. Results cached ~5h.

There are **no claims, roles, or scopes** parsed from the token — it's an opaque bearer credential
for the BetGenius API.

## Claims / policies / JWT / cookies / OIDC
**None present.** [Verified — absence.] If a future requirement adds an inbound API to OEv2
(e.g., resurrecting the dead `OE.API` shell), authentication would need to be designed from
scratch — there is no existing pattern in this repo to follow; reuse platform-final's approach.

## Secret handling for these credentials
All of the above credentials are **config/secret values**, resolved per
[Configuration.md](./Configuration.md): committed `appsettings.json` holds dev stubs (some with
**live values still in git** — security finding H1 in [TechnicalDebt.md](./TechnicalDebt.md)),
overridden in prod by AWS Secrets Manager via External Secrets. Rotate the committed ones.

## Summary
- **Inbound auth:** none (not applicable to headless workers).
- **Outbound auth:** broker/DB/API credentials (config+secrets) + one BetGenius bearer-token flow.
- **The interesting code:** `BetGeniusRepository` (token acquire/cache/use). Everything else is
  static credentials injected via configuration.
