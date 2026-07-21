# CronRadar .NET SDK

[![NuGet](https://img.shields.io/nuget/v/CronRadar.svg)](https://www.nuget.org/packages/CronRadar/)

The base SDK for monitoring scheduled jobs in .NET. Wrap a delegate, send a heartbeat, or track full lifecycle — CronRadar tracks duration, alerts on missed schedules, and recovers from accidental monitor deletion.

For framework auto-discovery, install the matching extension on top of this base:

- [`CronRadar.Hangfire`](https://www.nuget.org/packages/CronRadar.Hangfire) — every recurring Hangfire job auto-monitored
- [`CronRadar.Quartz`](https://www.nuget.org/packages/CronRadar.Quartz) — every Quartz.NET trigger auto-monitored

## Install

```bash
dotnet add package CronRadar
```

Targets `netstandard2.0`, `net6.0`, `net7.0`, `net8.0`, `net9.0`.

## Setup

The SDK reads its API key from the `CRONRADAR_API_KEY` environment variable.

```bash
export CRONRADAR_API_KEY=ck_app_xxxxxxxxxxxxxxxxxxxx
```

Or pass it explicitly per-call (useful in app config):

```csharp
CronRadar.Monitor("daily-backup", apiKey: "ck_app_xxxxxxxxxxxxxxxxxxxx");
```

Get the API key from the CronRadar dashboard at [app.cronradar.com](https://app.cronradar.com) under your application's settings. API keys have the form `ck_app_<random>`.

Create a separate CronRadar application and API key for production, staging, and development. Store the key in the deployment platform's secret manager; never commit it, print it, or reuse a production key outside production.

The SDK does not require explicit initialization. The first call reads the env var; if it's missing, the SDK logs a single warning and silently no-ops every subsequent call (per the "never break a user's job" guarantee).

## Quickstart

```csharp
using CronRadar;

// First-time call: pass schedule so the monitor auto-registers.
await CronRadar.MonitorAsync("daily-backup", schedule: "0 2 * * *");

// Subsequent calls: just the key.
await CronRadar.MonitorAsync("daily-backup");
```

That's the minimal integration. CronRadar:

- Creates the `daily-backup` monitor on the first ping (because `schedule` was provided)
- Records each subsequent ping
- Alerts you (email + any other configured channels) if the monitor doesn't ping within `0 2 * * *` plus its grace period
- Re-creates the monitor if you delete it from the dashboard and the schedule param is still being passed

## Manual lifecycle

For finer-grained tracking — duration measurement, explicit failure messages, hung-job detection — use the lifecycle endpoints. Async methods are recommended for non-blocking execution; sync overloads exist for legacy code.

### Wrapper (recommended)

```csharp
using CronRadar;

await CronRadar.WrapAsync("daily-backup", async () =>
{
    await RunBackupAsync();
}, schedule: "0 2 * * *");
```

The wrapper:

- Calls `StartJobAsync` before invoking your delegate
- Calls `CompleteJobAsync` if it returns
- Calls `FailJobAsync` with the exception message if it throws
- Re-throws the original exception so your error handlers run normally
- Auto-registers the monitor on first run if `schedule` is provided

### Manual call style

```csharp
using CronRadar;

await CronRadar.StartJobAsync("daily-backup", schedule: "0 2 * * *");
try
{
    await RunBackupAsync();
    await CronRadar.CompleteJobAsync("daily-backup");
}
catch (Exception ex)
{
    await CronRadar.FailJobAsync("daily-backup", ex.Message);
    throw;   // always re-throw — monitoring observes, never alters behavior
}
```

## Reference

All public methods exist as both sync (`Monitor`) and async (`MonitorAsync`) overloads. Async is recommended.

### `MonitorAsync(monitorKey, schedule?, apiKey?)`

Records a successful execution. Returns `Task`.

```csharp
await CronRadar.MonitorAsync("daily-backup");
await CronRadar.MonitorAsync("daily-backup", schedule: "0 2 * * *");
```

| Parameter | Type | Required | Notes |
|---|---|---|---|
| `monitorKey` | `string` | yes | Monitor key. Lowercase, kebab/snake/dot-case. Max 200 chars. |
| `schedule` | `string?` | optional | Standard 5-field cron expression. Required on the *first* ping for self-healing registration. |
| `apiKey` | `string?` | optional | Override the env-var-supplied API key for this call. |

Throws: never. Network errors and 4xx/5xx responses are caught and logged when `DebugLoggingEnabled` is true.

Grace period is set globally via the static property `CronRadar.DefaultGracePeriodSeconds` (default 60), or per-monitor via `SyncMonitor`. It's not a per-call argument.

### `StartJobAsync(key, schedule?)`

Records that a job has begun executing.

```csharp
await CronRadar.StartJobAsync("daily-backup");
await CronRadar.StartJobAsync("daily-backup", schedule: "0 2 * * *");
```

Used in conjunction with `CompleteJobAsync` or `FailJobAsync` to measure duration and detect hung jobs. Pass `schedule` on the first call to auto-register the monitor.

### `CompleteJobAsync(key)`

Records successful completion.

```csharp
await CronRadar.CompleteJobAsync("daily-backup");
```

Pair with a prior `StartJobAsync(key)` to record duration in the dashboard.

### `FailJobAsync(key, message?)`

Records explicit failure.

```csharp
await CronRadar.FailJobAsync("daily-backup", "Database connection refused");
```

| Parameter | Type | Required | Notes |
|---|---|---|---|
| `key` | `string` | yes | Monitor key. |
| `message` | `string?` | optional | Human-readable failure detail, shown in the dashboard. |

Triggers an immediate alert (no grace period). The message appears in the alert payload.

### `WrapAsync(monitorKey, fn, schedule?, apiKey?)`

Wraps an async delegate with full lifecycle tracking. Returns `Task`.

```csharp
await CronRadar.WrapAsync("daily-backup", async () => { /* work */ }, schedule: "0 2 * * *");
```

| Parameter | Type | Required | Notes |
|---|---|---|---|
| `monitorKey` | `string` | yes | Monitor key. |
| `fn` | `Func<Task>` | yes | The async delegate to instrument. |
| `schedule` | `string?` | optional | Cron expression for self-healing registration. |
| `apiKey` | `string?` | optional | Override the env-var-supplied API key for this call. |

If `fn` throws, the wrapper re-throws after recording the failure. Monitoring never swallows your exceptions. A sync `Wrap(string monitorKey, Action action, ...)` overload is also available for non-async code.

### `SyncMonitorAsync(key, schedule, source?, name?)`

Pre-registers a monitor without sending a ping. Used by the Hangfire and Quartz extensions during application startup.

```csharp
await CronRadar.SyncMonitorAsync(
    key: "daily-backup",
    schedule: "0 2 * * *",
    source: "hangfire",
    name: "Daily Backup");
```

| Parameter | Type | Required | Notes |
|---|---|---|---|
| `key` | `string` | yes | Monitor key. |
| `schedule` | `string` | yes | Cron expression. |
| `source` | `string?` | optional | Identifier for the framework or origin. |
| `name` | `string?` | optional | Human-readable display name. |

Used internally by extensions; rarely needed in application code.

## Configuration

| Environment variable | Required | Default | Purpose |
|---|---|---|---|
| `CRONRADAR_API_KEY` | yes | — | API key from the CronRadar dashboard. Format `ck_app_xxxxx`. |
| `CRONRADAR_DEBUG` | no | `false` | Set to `true` to log every request and error to `Console.Error`. Equivalent to `CronRadar.DebugLoggingEnabled = true` in code. |

Static properties on `CronRadar` for in-process tuning (must be set before the first call):

| Property | Default | Purpose |
|---|---|---|
| `CronRadar.TimeoutSeconds` | `5` | HTTP request timeout. |
| `CronRadar.DefaultGracePeriodSeconds` | `60` | Default grace period for newly registered monitors. |
| `CronRadar.DebugLoggingEnabled` | follows `CRONRADAR_DEBUG` | Toggle debug logging at runtime. |

The base URL is a compile-time internal constant pointing at `https://cron.life`.

## Error handling

The SDK upholds two hard guarantees:

1. **Never throws to user code.** Network errors, timeouts, 4xx/5xx responses, malformed payloads — all caught and logged. A failing CronRadar API must not break a user's cron job.
2. **Re-throws user-job exceptions.** When using `WrapAsync`, the original exception from your delegate always propagates. Monitoring observes; it does not change behavior.

What this looks like at runtime:

| Situation | SDK behavior | Your code |
|---|---|---|
| Network unreachable | Logs once; returns | Continues normally |
| 5-second timeout | Logs once; returns | Continues normally |
| 401 Unauthorized | Logs once; returns | Continues normally |
| Wrapped delegate throws | Records `FailJobAsync`; re-throws | Receives the original exception |
| `CRONRADAR_API_KEY` missing | Logs once per process; subsequent calls no-op | Continues normally |

If you need to know whether a ping succeeded — for example, in tests — every method returns `Task` or `Task<T>` either way; ping success is silent. Enable `CRONRADAR_DEBUG=true` to surface every request.

## Troubleshooting

### `401 Unauthorized` in logs

Your `CRONRADAR_API_KEY` is wrong or missing. Verify:

```bash
test -n "$CRONRADAR_API_KEY" && echo "CronRadar key is set" || echo "CronRadar key is missing"
```

The key must match the one shown on **app.cronradar.com → your application → Settings → API Keys**. Keys are not retrievable after creation — if you lost it, create a new one and rotate.

### `404 Monitor Not Found` on first ping

You called `MonitorAsync(key)` without a `schedule` for a brand-new key. The first ping must include `schedule` so CronRadar can register the monitor:

```csharp
await CronRadar.MonitorAsync("daily-backup", schedule: "0 2 * * *");
```

Subsequent pings can omit `schedule`. If you delete the monitor from the dashboard and the next ping doesn't include `schedule`, you'll see the same 404.

### My cron expression is rejected

CronRadar uses the standard 5-field POSIX cron format. **Quartz.NET-style 6 or 7-field expressions are not accepted by the base SDK.** If you're using Quartz, use the `CronRadar.Quartz` extension which translates Quartz expressions correctly.

| Wrong (passed to base SDK) | Right |
|---|---|
| `0 0 0 2 * *` (Quartz 6-field) | `0 2 * * *` |
| `0 0 2 ? * MON-FRI` (Quartz `?`) | `0 2 * * 1-5` |

### My async ping doesn't complete before process exit

If you're awaiting the SDK calls, this should not happen. If you're using `Task.Run(...)` fire-and-forget patterns, ensure the task completes before exit:

```csharp
await CronRadar.MonitorAsync("daily-backup");
return 0; // safe — await above ensures the ping completed
```

### `TaskCanceledException` from the underlying `HttpClient`

The default 5-second timeout has been hit. CronRadar catches and swallows this — it should not surface in your code. If you do see it, you're likely intercepting at a lower level (custom `HttpMessageHandler`, etc.). The SDK uses `HttpClient` instances directly; check that your DI container isn't injecting a handler that throws on cancellation.

## Verification

Run the wrapped job once and confirm the intended CronRadar application shows the monitor plus a terminal `success` or `failed` result. A package restore or monitor record without a terminal run is not activation.

Check authentication and outbound HTTPS without printing the key:

```bash
curl --fail-with-body --silent --show-error --user "$CRONRADAR_API_KEY:" "https://cron.life/api/sync/status"
```

After a real run is visible, use the guided reliability drill to verify the configured notification path without changing the production job.

## Migration

To replace a completion-only `Monitor` call with lifecycle tracking, keep the same monitor key and schedule, wrap the job with `WrapAsync`, verify one successful and one controlled failed run, then remove the old heartbeat. The wrapper records failure and rethrows the original user exception, preserving scheduler behavior and monitor history.

For Hangfire or Quartz.NET, migrate to the matching framework integration only after its discovered key matches the existing manual key and a terminal run is visible; then remove the duplicate manual wrapper.

## Links

- **Documentation:** [docs.cronradar.com](https://docs.cronradar.com)
- **Agent-friendly index:** [docs.cronradar.com/llms.txt](https://docs.cronradar.com/llms.txt)
- **OpenAPI spec:** [docs.cronradar.com/openapi.json](https://docs.cronradar.com/openapi.json)
- **NuGet:** [nuget.org/packages/CronRadar](https://www.nuget.org/packages/CronRadar)
- **GitHub:** [github.com/cronradar/cronradar-dotnet](https://github.com/cronradar/cronradar-dotnet)
- **Hangfire extension:** [nuget.org/packages/CronRadar.Hangfire](https://www.nuget.org/packages/CronRadar.Hangfire)
- **Quartz.NET extension:** [nuget.org/packages/CronRadar.Quartz](https://www.nuget.org/packages/CronRadar.Quartz)
- **Support:** support@cronradar.com

## License

© 2026 [CronRadar](https://cronradar.com) · Proprietary — see [LICENSE](./LICENSE).
