---
description: Trace a message/request lifecycle through a worker (consume → handle → ack → publish)
argument-hint: [message type or routing key]
---

Trace the lifecycle of `$ARGUMENTS`. OEv2 has no HTTP request lifecycle — the unit is a **RabbitMQ
delivery**. file:line evidence.

1. **Bind/consume:** which service binds the routing key (`MessageBroker:RoutingKey`, `|`-separated,
   lowercased); `EventingBasicConsumer.Received` → `Worker.MessageReceivedEvent`.
2. **Middleware analogue:** extract trace context from headers → start Activity → deserialize on
   `BasicProperties.Type` → `switch` dispatch → `Process*` handler. See
   [docs/MiddlewarePipeline.md](../../docs/MiddlewarePipeline.md).
3. **Handle:** repository `Process*` → Mongo/SQL access + downstream `Publish`.
4. **Ack policy:** success → `BasicAck`; throw → `BasicNack(requeue:true)` + log + `r.error.*` publish.
5. **Egress:** `Publish/CPublish` sets `DeliveryMode=2`, `Type`, `CorrelationId`, injects trace headers.

Refs: [docs/RequestLifecycle.md](../../docs/RequestLifecycle.md),
[docs/ImplementationPatterns.md](../../docs/ImplementationPatterns.md).
