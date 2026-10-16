# Logging

Logging, tracing, and metrics across the worker fleet. All [Verified].

## 1. Framework — Serilog + OpenTelemetry

- **Serilog** is the logging framework in every service and job except `OE.Translation.Job` (checked 2026-10-16), wired via
  `.UseSerilog((ctx,_,cfg) => cfg.ReadFrom.Configuration(ctx.Configuration))` in `Program.cs`.
  Config-driven (the `Serilog` appsettings section), so sinks/levels/enrichers are deployment-tunable.
- **OpenTelemetry** (via the private `Infra.Extensions.Telemetry` package + `OpenTelemetry.Api`)
  provides traces and metrics. This is the operationally important pillar (see §4–5).
- **Job.Lookup** is on Serilog since DEVOPS-443 ([TechnicalDebt.md](./TechnicalDebt.md) M5, resolved). It used
  the default console logger before, which wrote every line of a stack trace as its own Loki entry (about 21
  entries per failed message, measured on STG 2026-10-15). The argocd
  `Serilog__WriteTo__0__Args__formatter` env was already set for dev, stg and prd, so it now logs compact JSON
  like the other services, and it registers OpenTelemetry the way they do (section 5).

> Note platform-web (the web repo) uses plain `builder.Logging.ClearProviders().AddConsole()` —
> different from OEv2's Serilog. Don't assume parity between repos.

## 2. Serilog configuration — [Verified]

From `appsettings.json` (`Serilog` section), representative (Sportsbook):
```jsonc
"Serilog": {
  "Using": [ "Serilog.Sinks.Console" ],
  "Minimumlevel": { "Default": "Information",
    "Override": { "Microsoft": "Warning", "Microsoft.Hosting.Lifetime": "Warning" } },
  "WriteTo": [ { "Name": "Console" } ],
  "Enrich": [ "FromLogContext" ]
}
```
- **Sink:** Console only (→ container stdout → collected by the cluster log pipeline). No file/Seq/
  Elastic sink in-repo.
- **Format:** argocd sets the **Compact JSON formatter**
  (`Serilog__WriteTo__0__Args__formatter = Serilog.Formatting.Compact.CompactJsonFormatter`) so prod
  logs are structured JSON, parseable downstream. Packages: `Serilog.Formatting.Compact`,
  `Serilog.Settings.Configuration`, `Serilog.Sinks.Console`, `Serilog.AspNetCore`.
- **Enrichment:** `FromLogContext` (carries pushed properties). No custom enrichers
  (no machine/thread/correlation enricher registered) — correlation is carried via the *trace*
  context instead (§4), not via a Serilog enricher.
- **Minimum level:** Information default; Microsoft framework noise suppressed to Warning. (Note the
  typo `Minimumlevel` vs Serilog's expected `MinimumLevel` — Serilog config binding is
  case-insensitive so it still applies; cosmetic.)

## 3. Structured logging in code — [Verified]

- Services use Serilog message templates and `OE.Shared/Extensions/LoggerExtension.cs` helpers
  (`LogInformationEx`, `LogErrorEx`, `LogWarningEx`) for consistent structured output.
- Mixed quality: some logs use proper templates (`"Connecting to RabbitMQ on [{host}:{port}]…"`,
  `MessageBroker.cs:151`); others use string interpolation (esp. `BetGeniusRepository` error logs)
  which loses structure. Prefer templates.

## 3a. Exception logging standard (required for new and changed code)

When logging an exception, pass the **exception object itself as the first argument**. Serilog then
records the exception type, message, full stack trace and inner exceptions as a structured
`Exception` field, instead of a flattened string.

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error processing message: Payload: {@Payload}", payload);
}
```

- **Do:** `_logger.LogError(ex, "...")`. With the `LogXxxEx` helpers, pass it as the `ex` argument
  (`_logger.LogErrorEx("...", ex)`); the helper forwards it to `LogError(ex, ...)`.
- **Don't** put `ex.Message` or `ex.StackTrace` into the message text
  (`$"... {ex.Message}-{ex.StackTrace}"`), and don't pass the exception as a template argument
  (`"... {exception}", ex`). Both drop the structured exception and its inner exceptions.
  (SonarQube S6667)
- **Don't** swallow exceptions. A `catch` block must at least log. (S2486)
- Use **PascalCase** template placeholders (`{Message}`, `{Payload}`, `{State}`) and keep the same
  name for the same value across services. (S6678)
- **Keep operational log lines.** Start, connecting, connected and progress lines (for example
  `"Connecting to RabbitMQ Server"`, `"GetLiveStream Start : {fixtureId}"`) are how a service's
  lifecycle is followed in production. Don't remove or merge them to satisfy SonarQube's "too many
  logging calls" rule (S6664); accept that finding instead.
- Prefer message templates over `$"..."` interpolation in new code (see §3).
- **Never pass a secret or credential value as a template argument** (API keys, tokens, connection
  strings, signing keys), mask it first (for example keep only the last few characters, blank the
  rest) rather than logging the raw value. A sibling repo (platform-final, GM-880, 2026-09-30) had
  `_logger.LogError` printing an OG private API key on every failed bet placement, visible to
  anyone with Grafana/Loki read access on PRD; fixed there with a reusable `Mask()` string
  extension (`Platform.Shared/Extensions/StringExtensions.cs`).

This standard was applied across the services in DEVOPS-370.

## 3b. Job.Lookup consumer health logging (DEVOPS-443)

`OE.Job.Lookup` watches its own RabbitMQ consumer and says what it is doing. All lines come from
`OE.Job.Lookup/Worker.cs`.

| Line | Level | Meaning |
|---|---|---|
| `Connected. Processing Incoming messages from queue {Queue} with prefetch {PrefetchCount}` | Information | startup; `PrefetchCount` is `unset` when the key is absent (the broker wrapper then uses 1000) |
| `Lookup consumer status: IsConsuming=... Received=... Failed=... AvgMs=... MaxMs=... LastMessageAgoSeconds=...` | Information | every `Custom:StatusLogSeconds` (60 s); the counters cover the last interval. No line at all means the process is gone, `Received=0` for several lines while the queue grows means the consumer is stuck, `IsConsuming=False` means the consumer was lost |
| `RabbitMQ consumer on queue {Queue} is not consuming, check {Misses} of {Limit}` | Warning | `IsConsuming` was false at a `Custom:ConsumerCheckSeconds` (30 s) check |
| `Lookup consumer status` at Error, then `BackgroundService failed` | Error | the consumer stayed down for `Custom:ConsumerDeadChecks` (2) checks: the worker throws, the host stops, `Program.Main` returns 1 and Kubernetes restarts the pod |
| `Error processing message {MessageType} for fixture {FixtureId}, redelivered {Redelivered}, after {ElapsedMs} ms` | Error | the handler failed, the exception is attached; the message is acked and dropped |
| `Slow message {MessageType} for fixture {FixtureId} took {ElapsedMs} ms` | Warning | a message took at least `Custom:SlowMessageMilliseconds` (5000 ms) |

Why a watchdog: RabbitMQ.Client recovers a lost connection but not a channel the broker closes or a consumer
it cancels. `IsConsuming` (`MessageBroker.IsConsuming`) is false in those cases and true again once automatic
recovery re-registers the consumer, so only a consumer that stays down fails the host. The reason for a lost
consumer is logged by `MessageBroker.LogConsumerStops` (`RabbitMQ consumer channel on queue {Queue} shut down:
{ReplyCode} {ReplyText}` or `... cancelled by the broker`). See
[knowledge/Troubleshooting.md](./knowledge/Troubleshooting.md) "Queue grows, 0 consumers, but the pod is Running".

## 4. Correlation IDs & distributed tracing — [Verified] (the real correlation mechanism)

Correlation across the pipeline is **trace-based**, not a logged `correlationId` field:

- Every published message carries a RabbitMQ `CorrelationId` property **and** the **W3C trace
  context injected into message headers** by `MessageBroker.Publish*`
  (`Propagator.Inject(...)`, `MessageBroker.cs:391`). Consumers **extract** it
  (`ExtractTraceFromBasicProperties`) so a single trace spans Adapter → Sportsbook → Websocket etc.
- Each service defines an `ActivitySource` (per-service name) and starts an `Activity` per message
  (`"{routingKey} receive"` / `"{routingKey} send"`, `ActivityKind.Consumer/Producer`) with tags:
  `message.type`, `client.code`, `correlation.id`, provider ids, counts, `sport.name`, etc.
- External HTTP calls also create activities (e.g. `BetGenius.API` source: `betgenius.authorization`,
  `betgenius.fixture(s)` with `ActivityStatusCode` set on success/error,
  `BetGeniusRepository.cs:43,129,229`).
- Export: OTLP/protobuf → Tempo (`OTEL_EXPORTER_OTLP_ENDPOINT`). This is where you reconstruct an
  end-to-end request, not from console logs.

## 5. Metrics — [Verified]

Per-service `Telemetry/Instrumentation.cs` defines a `Meter` and instruments. Examples
(Sportsbook, `Instrumentation.cs:20-21`):
| Instrument | Type | Unit | Meaning |
|---|---|---|---|
| `processed.messages` | Counter<long> | count | messages processed (tagged `message.type`) |
| `bulkwrite.duration` | Histogram<double> | ms | Mongo bulk-write latency |

Other services expose `processed.messages` / `forwarded.messages` (UOF.Client) and job counters
(e.g. LiveStream fixtures updated). Metrics scraped on `:8080/metrics` (argocd, 30s interval).

Job.Lookup (DEVOPS-443) follows the Adapter: `processed.messages` (tag `message.type`, a failed message counts as
`error`), a `handler.duration` histogram (ms, tag `message.type`) and one consumer span per message named
`{routing key} receive` that continues the trace from the message headers. Export is switched by `Telemetry:Enabled`
(`Telemetry__Enabled: true` was already in its argocd values). Its argocd values also set `metrics.enabled: false` in
dev, stg and prd; in the `betgenius-app` chart that removes the metrics port, the service port and the ServiceMonitor
(which of the two charts serves the job was not checked), so Prometheus does not scrape it until that changes.
The span code reads `Propagators.DefaultTextMapPropagator` per message: the default is a no-op until the OpenTelemetry
SDK initialises, so a value cached in a static field can silently stop traces linking.

## 6. Log levels & noise control
- Default Information; `Microsoft*` → Warning. Activity/circuit detailed errors are **not** a worker
  concern (those are platform-web settings).
- High-volume hot paths (per-message) log mostly at Information/Debug-via-template; errors at Error
  with stack + payload.

## 7. Known logging issues — [Verified] (see TechnicalDebt.md)
- **Silent swallows:** UOF.Client `PlatformRepository` (`async void` + empty catch), so failures
  are invisible in logs. (The Lookup message handler's `catch {}` now logs, DEVOPS-370.)
- **KeepAlive `IsHealthy=true` hardcoded** — heartbeat log/metric is not a true health indicator.
- **String-interpolated error logs** in `BetGeniusRepository` reduce structured-log value. Its
  exception catches now use `LogError(ex, ...)` (DEVOPS-370); the non-exception HTTP-error logs are
  still interpolated.
- **No correlation enricher** in Serilog — correlation lives only in traces; a log line in
  isolation can't be tied to a trace without the trace/span id (consider adding a trace-id enricher).
