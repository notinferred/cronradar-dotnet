# CronRadar .NET SDK

[![NuGet](https://img.shields.io/nuget/v/CronRadar.svg)](https://www.nuget.org/packages/CronRadar/)

**Self-healing monitors. Never breaks.**

## Installation

```bash
dotnet add package CronRadar
```

## Usage

```csharp
// After your job completes successfully
CronRadar.Monitor("daily-backup");

// With self-healing (auto-register if monitor doesn't exist)
CronRadar.Monitor("daily-backup", schedule: "0 2 * * *");
```

## Lifecycle Tracking

**Option 1: Wrapper (Automatic)**
```csharp
await CronRadar.WrapAsync("daily-backup", async () =>
{
    await RunBackup();
}, schedule: "0 2 * * *");
```

**Option 2: Manual**
```csharp
await CronRadar.StartJobAsync("daily-backup");
try
{
    await RunBackup();
    await CronRadar.CompleteJobAsync("daily-backup");
}
catch (Exception e)
{
    await CronRadar.FailJobAsync("daily-backup", e.Message);
    throw;
}
```

## Configuration

Set environment variable:
- `CRONRADAR_API_KEY`: Your API key from cronradar.com

## Framework Extensions

- **Hangfire**: `dotnet add package CronRadar.Hangfire`

## Support

📚 Docs: [cronradar.com/docs](https://cronradar.com/docs)

## License

Proprietary - © 2025 CronRadar. All rights reserved.

See [LICENSE](./LICENSE) for details. This SDK may only be used with the CronRadar monitoring service.