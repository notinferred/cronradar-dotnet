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

## Framework Extensions

- **Hangfire**: `dotnet add package CronRadar.Hangfire`

## License

Proprietary - © 2025 CronRadar. All rights reserved.
