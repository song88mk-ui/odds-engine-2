# Playbook — Trace an API Interaction (OEv2)

Use `/trace-platform-api` for a single endpoint; this is the deeper procedure. Remember OEv2 has **no
inbound HTTP** — APIs here are either outbound calls OEv2 makes, or the read surface over OEv2 data.

## 1. Classify
- **Outbound (OEv2 calls):** `markettemplateplayerprops` (UOF.Client), templates/metadata
  (Websocket), `v1/broker/publish` (`RabbitMQRepository`). These are the only OEv2→Platform calls.
- **Read surface (OEv2 doesn't call):** platform-web → platform-final → reads the Mongo catalogue
  OEv2 wrote. Document the seam but tag platform handlers [Verified-sibling]/[Unknown].

## 2. Trace the call
- Caller repo + `HttpClient` config (base address, `x-api-key`/`x-client-id`), method+path, request/
  response DTO (`BaseResponseDTO<T>`), retry/cache (UOF 1yr `mtpp`; platform-web 30s/2×).

## 3. Backing store
- For read-surface endpoints, identify the Mongo collection / SQL table behind it
  ([docs/knowledge/PlatformApiEndpoints.md](../../docs/knowledge/PlatformApiEndpoints.md)).

## 4. Auth & errors
- `StatusCode` enum semantics; auth header flow; OG (`x-og-*`) where relevant.

Output: caller → endpoint → handler/store chain, file:line, confidence. Do not invent platform-side
behavior. **Production = read-only.**
