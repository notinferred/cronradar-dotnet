# CronRadar .NET SDK

[![NuGet](https://img.shields.io/nuget/v/CronRadar.svg)](https://www.nuget.org/packages/CronRadar/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

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

Set your API key (get one at [cronradar.com](https://cronradar.com)):

```bash
export CRONRADAR_API_KEY=ck_app_xxxxx_yyyyy
```

## Examples

### Console Application
```csharp
class Program
{
    static void Main()
    {
        // Do your work
        BackupDatabase();

        // Ping CronRadar
        CronRadar.Ping("daily-backup");
    }
}
```

### ASP.NET Core
```csharp
public class BackupService
{
    public async Task RunBackup()
    {
        await PerformBackup();
        await CronRadar.PingAsync("backup");
    }
}
```

### Windows Service / Task Scheduler
```csharp
protected override void OnStart(string[] args)
{
    DoWork();
    CronRadar.Ping("windows-service");
}
```

## How It Works

1. Your job runs
2. You call `CronRadar.Ping("job-name")`
3. If the ping doesn't arrive on schedule, you get alerted

That's it. No complex setup. No agents. Just monitoring.

## Framework Extensions

Need framework-specific integration?

- **Hangfire**: `dotnet add package CronRadar.Hangfire`
- **Quartz.NET**: `dotnet add package CronRadar.Quartz` (coming soon)
- **Azure Functions**: `dotnet add package CronRadar.AzureFunctions` (coming soon)

## Philosophy

- **One Function**: Just `Ping()` - nothing else
- **Zero Dependencies**: Uses only .NET built-in libraries
- **Never Breaks**: Silently fails to protect your application
- **30 Lines of Code**: The entire SDK. Really.

## Support

- 📧 Email: support@cronradar.com
- 🐛 Issues: [GitHub](https://github.com/cronradar/cronradar-dotnet/issues)
- 📚 Docs: [cronradar.com/docs](https://cronradar.com/docs)

## License

MIT