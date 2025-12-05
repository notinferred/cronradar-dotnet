# CronRadar .NET SDK

[![NuGet](https://img.shields.io/nuget/v/CronRadar.svg)](https://www.nuget.org/packages/CronRadar/)

**One function. Zero dependencies. Never breaks.**

## Installation

```bash
dotnet add package CronRadar
```

## Usage

```csharp
// That's it. Your job is monitored.
CronRadar.Ping("daily-backup");
```

Async version:
```csharp
await CronRadar.PingAsync("daily-backup");
```

## Configuration

Set your API key (required - get one at [cronradar.com](https://cronradar.com)):

```bash
export CRONRADAR_API_KEY=ck_app_xxxxx_yyyyy
```

**Note:** If no API key is set, the SDK will log a warning but won't break your application.

## Example

```csharp
// After your job completes
BackupDatabase();
CronRadar.Ping("daily-backup");
```

## Framework Extensions

- **Hangfire**: `dotnet add package CronRadar.Hangfire`
- **Quartz.NET**: Coming soon
- **Azure Functions**: Coming soon

## Support

- 📧 Email: support@cronradar.com
- 🐛 Issues: [GitHub](https://github.com/cronradar/cronradar-dotnet/issues)
- 📚 Docs: [cronradar.com/docs](https://cronradar.com/docs)

Proprietary - © 2025 CronRadar. All rights reserved.