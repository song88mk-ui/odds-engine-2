---
description: Trace a Platform REST API interaction at the OEv2 seam
argument-hint: [endpoint or feature, e.g. markettemplateplayerprops]
---

Trace Platform API interaction `$ARGUMENTS`. file:line evidence; verified / assumption / unknown.

- **Does OEv2 call it?** Known outbound calls: `OE.UOF.Client` → `GET
  v1/sportsbook/client/markettemplateplayerprops` (`x-api-key`, cached 1yr); `OE.Websocket.Service`
  → Platform API templates/metadata; `OE.Shared/RabbitMQRepository` → `POST v1/broker/publish`.
- **Or is it the read surface over OEv2 data?** platform-web → platform-final REST → reads the Mongo
  catalogue OEv2 wrote (fixtures/markets/selections/translations). OEv2 doesn't call these.
- Report: caller, method+path, auth headers (`x-api-key`/`x-client-id`/`x-operator-token`/`x-og-*`),
  `BaseResponseDTO<T>` envelope, backing store, retry/caching.

Refs: [docs/PlatformApi.md](../../docs/PlatformApi.md),
[docs/knowledge/PlatformApiEndpoints.md](../../docs/knowledge/PlatformApiEndpoints.md). Mark unverified
platform-side handlers as [Unknown] — do not invent sibling behavior.
