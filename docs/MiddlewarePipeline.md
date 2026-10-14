# Middleware Pipeline

## Applicability — there is NO HTTP middleware pipeline in odds-engine-2

[Verified — by absence.] An ASP.NET middleware pipeline (`app.UseRouting()`,
`UseAuthentication()`, `UseAuthorization()`, `UseResponseCompression()`, `UseExceptionHandler()`,
localization, etc.) exists only in a **web host**. Every odds-engine-2 service is a **Generic Host
worker** (`Host.CreateDefaultBuilder`) with **no `WebApplication`, no Kestrel, no `IApplicationBuilder`,
no `app.Use*` calls**. Grep confirms: no `UseRouting`/`UseAuthentication`/`UseEndpoints`/
`UseResponseCompression`/`UseRequestLocalization` anywhere in `src/OddsEngine`.

> For comparison, the sibling **platform-web** (Blazor Server) *does* have a real pipeline
> (`UseStaticFiles → UseHttpsRedirection → MaintenanceMiddleware → UseRouting → UseCors →
> MapBlazorHub → MapControllers → MapFallbackToPage`, `platform-web/Platform/Platform.Child.Web/Program.cs`)
> and is documented in `platform-web/docs/MiddlewarePipeline.md`. **That** is where the web
> middleware topics live. This document covers the **worker analogue**.

## The worker analogue: the message-processing pipeline

The functional equivalent of "the request goes through ordered middleware then a handler" for a
worker is **"a RabbitMQ delivery goes through an ordered set of steps then a handler."** That
pipeline is implemented in `OE.Shared/Utilities/MessageBroker.cs` + each service's `Worker.cs`.

### Inbound delivery pipeline (per message) — [Verified]

```
RabbitMQ delivery (BasicDeliverEventArgs)
  │
  1. Consumer.Received → HandleMessage / HandleMessageWithManualAck   (MessageBroker.cs:332/320)
  │      → raises MessageReceived event
  │
  2. Worker.MessageReceivedEvent(sender, e)                            (each Worker.cs)
  │   a. Extract W3C trace context from BasicProperties headers   ← "tracing middleware"
  │   b. Start Activity ("{routingKey} receive", Consumer) + tags ← "diagnostics middleware"
  │   c. Read messageType = e.BasicProperties.Type
  │   d. Deserialize payload (System.Text.Json) into Outgoing*Message ← "model binding"
  │   e. switch(messageType) → ProcessXxx(...)                    ← "endpoint/handler"
  │   f. (handler does DB writes / publishes downstream)
  │
  3. Ack policy                                                    ← "exception handling middleware"
  │   • auto mode (HandleMessage): success → BasicAck;
  │                                throw   → BasicNack(requeue:true)
  │   • manual mode (ConnectWithManualAck): handler controls Ack/Nack
  │   • per-service try/catch also: log + Activity error + publish to r.error.<domain>.<service> (Adapter: r.error.oe.adapter)
  └─ increment processed.messages / forwarded.messages metric     ← "metrics middleware"
```

### Mapping web-middleware concepts → worker equivalents

| ASP.NET middleware | Worker equivalent in OEv2 | Where |
|---|---|---|
| Routing (`UseRouting`/endpoints) | `switch(messageType)` dispatch in each Worker | `*/Worker.cs` |
| Model binding | `JsonSerializer.Deserialize<TMessage>(payload)` | each handler |
| Exception handling (`UseExceptionHandler`) | try/catch → log + Nack/requeue + `r.error.*` publish | Worker + `MessageBroker.HandleMessage*` |
| Authentication | **none inbound** (broker-level creds only) — see [Authentication.md](./Authentication.md) | — |
| Authorization | **none** — see [Authorization.md](./Authorization.md) | — |
| CORS | N/A (no browser callers) | — |
| Response compression | N/A (no HTTP responses; payloads minimized via abbreviated fields) | — |
| Localization (`UseRequestLocalization`) | **not request-scoped** — there is no request. Localization happens as *message enrichment*: `OE.Adapter.Service` stamps every language's `*Tr` onto the message from a startup-preloaded cache, and consumers pick the language they need | `OE.Adapter.Service/Repositories/TradingRepository.cs:119-159` |
| Static files / HTTPS redirect / HSTS | N/A | — |
| Rate limiting / QoS | RabbitMQ `BasicQos` prefetch (default 1000) | `MessageBroker.cs:205` |
| Health checks middleware | none (no endpoint); KeepAlive heartbeat instead | `OE.Job.KeepAlive` |

### Outbound publish "pipeline" — [Verified]
`MessageBroker.Publish*` is the symmetric egress step:
1. Start Activity (`"{routingKey} send"`, Producer).
2. Set `CorrelationId`, `DeliveryMode=2` (persistent), `Type`.
3. **Inject** trace context into headers (`Propagator.Inject`).
4. Serialize + `BasicPublish` to `e.central` (or external vhost for UOF.Client).

## Ordering guarantees
- There is no cross-message middleware ordering to reason about beyond the steps above.
- Per-fixture **ordering** is provided by the sticky shard (all of a fixture's messages hit the
  same Sportsbook instance/queue) — see [StartupFlow.md](./StartupFlow.md) and Phase 1
  [Architecture.md](./Architecture.md). Within a process, message handling is event-driven
  (RabbitMQ consumer thread) except Websocket.Service which hands off to per-class worker threads.

## Takeaway
If you came here looking for `UseAuthentication`/`UseAuthorization`/`UseResponseCompression`:
those belong to **platform-web**, not OEv2. In OEv2 the "pipeline" is the
**deserialize → dispatch-by-message-type → handle → ack/nack** loop above, instrumented with
OpenTelemetry at both ingress and egress.
