---
name: platform-api-investigation
description: Investigate the Platform REST seam — what OEv2 calls and the read surface over OEv2 data. Use for REST integration questions or "where does the UI get this data".
---

# Platform API Investigation (OEv2)

Reference: [docs/PlatformApi.md](../../../docs/PlatformApi.md),
[docs/knowledge/PlatformApiEndpoints.md](../../../docs/knowledge/PlatformApiEndpoints.md).

## Two directions
- **OEv2 calls** (narrow): UOF.Client → `GET v1/sportsbook/client/markettemplateplayerprops`
  (`x-api-key`, 1yr cache); Websocket → templates/metadata; `OE.Shared/RabbitMQRepository` → `POST
  v1/broker/publish`.
- **Read surface over OEv2 data**: platform-web → platform-final REST → reads the Mongo catalogue
  OEv2 wrote (fixtures/markets/selections/translations) + SQL bet/wallet. OEv2 doesn't call these.

## Conventions
`BaseResponseDTO<T>` envelope; `StatusCode` enum (shared); headers `x-api-key`/`x-client-id`/
`x-operator-token`/`x-og-*`; platform-web 2× retry + 30s cache.

## Procedure
Determine direction → identify caller/handler/backing store → cite file:line. For platform-final
handlers tag [Verified-sibling] or [Unknown] — **don't invent sibling behavior**. **Production =
read-only.**
