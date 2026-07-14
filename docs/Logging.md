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
- **Silent swallows:** Lookup message handler (`catch {}`), UOF.Client `PlatformRepository`
  (`async void` + empty catch) — failures invisible in logs.
- **KeepAlive `IsHealthy=true` hardcoded** — heartbeat log/metric is not a true health indicator.
- **String-interpolated error logs** in `BetGeniusRepository` reduce structured-log value.
- **No correlation enricher** in Serilog — correlation lives only in traces; a log line in
  isolation can't be tied to a trace without the trace/span id (consider adding a trace-id enricher).
