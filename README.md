# CronRadar .NET SDK

[![NuGet](https://img.shields.io/nuget/v/CronRadar.svg)](https://www.nuget.org/packages/CronRadar/)

**Self-healing monitors. Never breaks.**

## Installation

```bash
dotnet add package CronRadar
```

## Quick Start

```csharp
// Set API key via environment variable
// CRONRADAR_API_KEY=ck_app_xxxxx

// After your job completes
CronRadar.Monitor("daily-backup");

// With auto-registration (if monitor doesn't exist)
CronRadar.Monitor("daily-backup", schedule: "0 2 * * *");
```

## Lifecycle Tracking

Track start/complete/fail for better insights:

```csharp
CronRadar.StartJob("daily-backup", schedule: "0 2 * * *");
try
{
    RunBackup();
    CronRadar.CompleteJob("daily-backup");
}
catch (Exception e)
{
    CronRadar.FailJob("daily-backup", e.Message);
    throw;
}
```

Or use the wrapper:

```csharp
CronRadar.Wrap("daily-backup", () =>
{
    RunBackup();
}, schedule: "0 2 * * *");
```

## Async Methods (Recommended)

All methods have async versions for non-blocking execution:

```csharp
// Simple monitoring
await CronRadar.MonitorAsync("daily-backup");
await CronRadar.MonitorAsync("daily-backup", schedule: "0 2 * * *");

// Lifecycle tracking
await CronRadar.StartJobAsync("daily-backup", schedule: "0 2 * * *");
await CronRadar.CompleteJobAsync("daily-backup");
await CronRadar.FailJobAsync("daily-backup", errorMessage);

// Async wrapper
await CronRadar.WrapAsync("daily-backup", async () =>
{
    await RunBackupAsync();
}, schedule: "0 2 * * *");
```

## Configuration

**API Key** (required):
```bash
CRONRADAR_API_KEY=ck_app_xxxxx
```

Or pass directly:
```csharp
CronRadar.Monitor("job-name", apiKey: "ck_app_xxxxx");
```

**Debug logging** (optional):
```bash
CRONRADAR_DEBUG=true
```

## Links

- [Documentation](https://docs.cronradar.com)
- [NuGet](https://www.nuget.org/packages/CronRadar)

**Extensions:** [Hangfire](https://www.nuget.org/packages/CronRadar.Hangfire) | [Quartz.NET](https://www.nuget.org/packages/CronRadar.Quartz)

## License

© 2025 [CronRadar](https://cronradar.com) - See [LICENSE](./LICENSE)
