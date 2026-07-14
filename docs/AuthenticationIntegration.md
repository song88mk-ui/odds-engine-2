# Authentication Integration (cross-repo)

How authentication works **across** the platform and where odds-engine-2 sits in it. The short
version: **odds-engine-2 is outside the user-authentication flow** — it has no inbound user auth
([Authentication.md](./Authentication.md)); its only credentials are *outbound* service credentials.
This doc maps the wider auth picture so that boundary is clear. Classification: **[Verified]** ·
**[Verified-sibling:Auth/platform-final/platform-web]** · **[Unknown]**.

## The platform auth flow (where OEv2 is NOT) — [Verified-sibling:Auth/platform-final/platform-web]

```
User ──login──► Auth.Api (/v1/login)
                  │  issues JWT (HS256, issuer "bo.auth.api") + Operator Token (GUID, DB-stored, cached)
                  ▼
        platform-final  Platform.Operator.API  (calls Auth /v1/login, /v1/authentication/operator)
                  │  hands operator token to the client
                  ▼
        platform-web  sends  x-operator-token  on every REST call to platform-final
                  │  (+ x-api-key, x-client-id, x-language-code, …)
                  ▼
        platform-final validates the operator token (via Auth) and authorizes the request
```
- **Auth repo** = .NET 8 API; sole **issuer/validator** of `x-operator-token` + JWT; own SQL `Auth`
  DB; own `x-api-key`; **no RabbitMQ**, **no Mongo**. [Verified-sibling:Auth]
- **OGAuth (Oriental Gaming)**: an MD5-HMAC signature scheme (`x-og-*` headers, `x-og-signature`)
  handled in **platform-web** (`OGAuth` page) / platform-final — **not** in the Auth repo.
  [Verified-sibling:platform-web]; whether Auth participates in OGAuth is [Unknown].

## odds-engine-2's position — [Verified]
- **No inbound authentication.** No JWT/cookie/OIDC/`[Authorize]`/operator-token validation anywhere
  in `src/OddsEngine` (grep-confirmed). OEv2 has no user-facing endpoints to protect.
- **No dependency on the Auth repo.** No code path from OEv2 to Auth (grep-confirmed,
  [Verified-sibling:Auth]).
- **Trust is topological**: OEv2 trusts any message delivered on its RabbitMQ queue (broker creds +
  vhost + network are the boundary) — see [Authorization.md](./Authorization.md).

## OEv2's actual authentication = outbound service credentials — [Verified]

| Target | Credential | Source/config | Notes |
|---|---|---|---|
| RabbitMQ (internal `e.central`) | username/password (+TLS on 5671) | `MessageBroker:*` (secrets in prod) | shared broker |
| RabbitMQ (external vhost `ext`) | username/password | `ExternalMessageBroker:*` | UOF.Client egress |
| MongoDB Sportsbook | conn-string creds | `Custom:MongoDB-Sportsbook` | — |
| SQL Trading / Translation | conn-string `User Id`/`Password` | `Custom:Trading` / `ConnectionStrings:Translation` | RDS, `TrustServerCertificate=True` |
| Platform API | `x-api-key` (+ `x-client-id` for Websocket) | `Custom:APIKey`/`API:APIKey` | the only platform-facing key OEv2 holds |
| BetGenius Video API | login → **Bearer token** + `x-api-key` | `Custom:BetGenius:User/Password/Key` | the one real token flow ([Authentication.md](./Authentication.md)) |

Detail of the BetGenius token acquire/cache/refresh: [Authentication.md](./Authentication.md) §"BetGenius".

## API-key landscape (who holds what) — [Verified]/[Verified-sibling]
- **OEv2 → Platform API:** `x-api-key` (`Custom:APIKey`). [Verified]
- **backoffice-final → Platform API:** its own `x-api-key`. [Verified-sibling:backoffice-final]
- **platform-web → Platform API:** `x-api-key` + `x-operator-token` (+ `x-og-*`). [Verified-sibling:platform-web]
- **Auth API:** its own `x-api-key` for callers (platform-final). [Verified-sibling:Auth]
- These keys are **distinct per service** and injected via config/secrets; several are still
  committed in OEv2 `appsettings.json` (security finding H1, [TechnicalDebt.md](./TechnicalDebt.md)).

## SignalR auth — [Verified-sibling:platform-web]
The `/platform` hub connection carries **no bearer token** (neither OEv2's publisher connection nor
platform-web's subscriber connection). User isolation is by **hub group naming**
(`{clientCode}:{userId}`), and the hub gateway is reached over TLS within the trusted network.
This means hub-level security depends on the gateway + network, not on per-connection identity.

## Trust boundaries summary
| Boundary | Enforced by |
|---|---|
| Who may publish/consume on `e.central` | RabbitMQ credentials + vhost + network policy |
| Who may read/write the DBs | DB credentials (secrets) |
| Who may call the Platform API | `x-api-key` (+ operator token for user-scoped) validated by platform-final/Auth |
| Who may call BetGenius | login + API key |
| Who a user is (UI) | Auth-issued operator token / JWT (OEv2 not involved) |

## Unknowns
- Whether the Auth repo participates in OGAuth signing/validation, or that's purely platform-side. [Unknown]
- Where the `/platform` hub enforces (or doesn't) any connection-level auth. [Unknown] (#19)
- Rotation status of the committed OEv2 API keys/credentials. [Unknown] (treat as compromised — H1)
