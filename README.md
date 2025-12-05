# CronRadar .NET SDK

[![NuGet](https://img.shields.io/nuget/v/CronRadar.svg)](https://www.nuget.org/packages/CronRadar/)

**Self-healing monitors. Never breaks.**

## Installation

```bash
dotnet add package CronRadar
```

## Quick Start

```csharp
// After your job completes
CronRadar.Ping("daily-backup");

// With self-healing (auto-registers if missing)
CronRadar.Ping("daily-backup", schedule: "0 2 * * *");
```

## Configuration

Set your API key (get one at [cronradar.com](https://cronradar.com)):

```bash
export CRONRADAR_API_KEY=ck_app_xxxxx_yyyyy
```

## Advanced Usage

### Async Operations

```csharp
await CronRadar.PingAsync("daily-backup");
```

### Proactive Monitoring

```csharp
// Register monitor upfront
CronRadar.SyncMonitor("daily-backup", "0 2 * * *");
```

## Framework Extensions

- **Hangfire**: `dotnet add package CronRadar.Hangfire`

## Support

📚 Docs: [cronradar.com/docs](https://cronradar.com/docs)

© 2025 CronRadar. All rights reserved.