# Configuration

Expands Phase 1 [ConfigurationOverview.md](./ConfigurationOverview.md) with the **runtime
resolution mechanics** (hierarchy, secrets, feature flags). All [Verified] / [Verified-argocd].

## 1. Configuration provider hierarchy (precedence, low → high)

Because services use `Host.CreateDefaultBuilder`, the standard .NET provider chain applies. Later
sources **override** earlier ones:

1. `appsettings.json` (baked into image; mostly dev stubs).
2. `appsettings.{Environment}.json` — `{Environment}` from `DOTNET_ENVIRONMENT`/`ASPNETCORE_ENVIRONMENT`.
   *Stripped from published images* (Dockerfiles remove `appsettings.*.json`), so effectively
   dev-only.
3. **User Secrets** — *Development only*. Present via `UserSecretsId` in
   OE.Job.Lookup, OE.Job.Settlement, OE.UOF.Client, OE.Websocket.Service csproj (the others have
   none). Used for local secret overrides off-disk.
4. **Environment variables** — the **primary production mechanism**. argocd injects `__`-delimited
   keys (`MessageBroker__Host`, `Custom__MongoDB-Sportsbook`, `Telemetry__Environment`, …).
5. Command-line args (rarely used).

> **Net effect in production:** values come from **env vars + External Secrets**, *not* the
> committed `appsettings.json`. To know what runs in an env, read
> `argocd/apps/oddsengine/<service>/<env>.yaml`. This is the #1 onboarding gotcha.

## 2. How configuration is read (the code pattern)

Imperative `IConfiguration` access throughout — **no Options pattern** (see
[DependencyInjection.md](./DependencyInjection.md) §5):
```csharp
cfg["MessageBroker:Host"]                       // string
cfg.GetValue<int>("Custom:DbContextTimeout", 180)   // typed + default
cfg.GetValue<bool>("MessageBroker:IsExclusive", false)
cfg.GetConnectionString("Translation")          // ConnectionStrings section
```
Note `:` (code) vs `__` (env var) for the same key, and `__N__` for array indices
(e.g. `Serilog__WriteTo__0__Args__formatter`).

## 3. Standard config sections

| Section | Purpose | Consumed by |
|---|---|---|
| `Logging:LogLevel` | MS logging levels (mostly superseded by Serilog) | all |
| `Serilog` | sink/level/enrichers (`ReadFrom.Configuration`) | 8/9 (not Lookup) |
| `Telemetry` | `Enabled`, `Environment`, `MetricsPort` | all (via package) |
| `MessageBroker` | connection + queue + routing (see [ConfigurationOverview.md](./ConfigurationOverview.md)) | broker users |
| `ExternalMessageBroker` | second broker, vhost `ext` | UOF.Client |
| `Custom:*` | service-specific: Mongo conn, client codes, BetGenius creds, retention, toggles | most |
| `ConnectionStrings:Translation` / `Custom:Trading` | SQL Server | Sportsbook / UOF.Client |
| `API:*` | Platform API base URL / keys | UOF.Client, Websocket |

## 4. Feature flags / toggles — [Verified]

Plain boolean/numeric config keys act as feature flags (no FeatureManagement library):

| Flag | Service | Effect |
|---|---|---|
| `Custom:Enabled` | LiveStream, Cleanup | master on/off for the job loop (Cleanup default **false**) |
| `Custom:EditSportsbook` | Settlement | gate before processing |
| `Custom:IsOEEnabled` | Websocket | selects OE vs legacy odds path |
| `Custom:UseMessagePack` | Websocket | SignalR MessagePack vs JSON |
| `Custom:AsianClient` | UOF.Client | enable Asian odds formats (Websocket `Custom:AsianClientCodes` removed in GM-811) |
| `MessageBroker:IsExclusive` / `IsPriority` | broker users | exclusive autodelete queue / `x-max-priority=10` |
| `Custom:EnableAzureSignalRService` | (platform-web only — not OEv2) | — |
| `Telemetry:Enabled` | all | telemetry on/off |
| `Custom:UtcWindowHours:Start/End`, `Custom:SportRetention` | Cleanup | window + retention tuning |
| `Custom:OrphanCleanupDryRun`, `Custom:OrphanSweepIntervalHours` | Cleanup | orphan sweep: count instead of delete (default **false**), and how often it may run (default **24**h) |

## 5. Secrets management — [Verified] / [Verified-argocd]

- **Production:** secrets are **External Secrets** synced from **AWS Secrets Manager**
  (`/<env>/apps/rabbitmq/{username,password}`, `/<env>/apps/mongodb-instance/connection-string/sportsbook`,
  …) into env vars (`MessageBroker__Password`, `Custom__MongoDB-Sportsbook`). This is the correct,
  intended mechanism.
- **Development:** User Secrets (where `UserSecretsId` exists) or local `appsettings.Development.json`
  (git-ignored).
- ⚠️ **Finding:** several committed `appsettings.json` still contain **live credentials** (RabbitMQ
  with real AWS hosts, BetGenius API key/password, SQL RDS password, platform API keys) because
  `.gitignore` only excludes `appsettings.Development.json`. They are overridden at runtime but
  sit in git history. **Rotate + remove.** Full list in [TechnicalDebt.md](./TechnicalDebt.md) (H1).
  *(This document deliberately does not reproduce secret values.)*

## 6. AWS Parameter Store
Not used directly by the app. AWS **Secrets Manager** (via External Secrets) is the store; there is
no `AddSystemsManager`/Parameter-Store config provider in code. [Verified — absence]

## 7. Environment matrix
Dev/Stg/Prd accounts, regions, broker/Mongo hosts, and secret paths are tabulated in
[ConfigurationOverview.md](./ConfigurationOverview.md) §"Environments" — not duplicated here.

## 8. Adding configuration (guidance, consistent with existing code)
- Add the key under `Custom:` (or a new section) in `appsettings.json` with a safe default.
- Read it imperatively with `GetValue<T>(key, default)`.
- Expose it for prod via the service's argocd `<env>.yaml` `env:` block (using `__`), and for
  secrets via an External Secret entry — never commit the real value.
