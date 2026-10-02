# Logging

Logging, tracing, and metrics across the worker fleet. All [Verified].

## 1. Framework — Serilog + OpenTelemetry

- **Serilog** is the logging framework in 8/9 services, wired via
  `.UseSerilog((ctx,_,cfg) => cfg.ReadFrom.Configuration(ctx.Configuration))` in `Program.cs`.
  Config-driven (the `Serilog` appsettings section), so sinks/levels/enrichers are deployment-tunable.
- **OpenTelemetry** (via the private `Infra.Extensions.Telemetry` package + `OpenTelemetry.Api`)
  provides traces and metrics. This is the operationally important pillar (see §4–5).
- **Outlier:** `OE.Job.Lookup` does **not** call `UseSerilog` — it uses the default
  `Microsoft.Extensions.Logging` console logger only. Normalize when touched
  ([TechnicalDebt.md](./TechnicalDebt.md) M5).

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
