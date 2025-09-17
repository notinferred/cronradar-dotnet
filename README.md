# CronRadar .NET SDK

[![NuGet](https://img.shields.io/nuget/v/CronRadar.svg)](https://www.nuget.org/packages/CronRadar/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-512BD4)](https://dotnet.microsoft.com/)

Official .NET SDK for [CronRadar](https://cronradar.com) - Dead-simple cron job monitoring. Get alerts when your scheduled tasks don't run on time.

## Features

✅ **Simple Integration** - Just a few lines of code to monitor any job
✅ **Fail-Safe Design** - Never breaks your application, even if CronRadar is down
✅ **Dependency Injection** - First-class support for ASP.NET Core DI
✅ **Hangfire Support** - Seamless integration with Hangfire jobs
✅ **Async/Sync** - Both synchronous and asynchronous methods
✅ **Auto-Sync** - Automatically sync monitors on application startup
✅ **Minimal Dependencies** - Uses only built-in .NET libraries + optional DI support

## Installation

Install via NuGet Package Manager:

```bash
dotnet add package CronRadar
```

Or via Package Manager Console:

```powershell
Install-Package CronRadar
```

## Quick Start

### 1. Set your API Key

Set your API key as an environment variable:

```bash
export CRONRADAR_API_KEY=ck_app_xxxxx_yyyyy
```

Or in Windows:
```powershell
$env:CRONRADAR_API_KEY="ck_app_xxxxx_yyyyy"
```

### 2. Basic Usage

```csharp
using CronRadar;

// Initialize client (reads API key from environment)
var cronRadar = new CronRadarClient();

// Define your monitors
var monitors = new[]
{
    new Monitor("daily-backup", "0 2 * * *") { Name = "Database Backup" },
    new Monitor("hourly-sync", "0 * * * *") { Name = "Data Sync" },
    new Monitor("weekly-report", "0 9 * * MON") { Name = "Weekly Report" }
};

// Sync monitors on startup
await cronRadar.SyncAsync(monitors);

// In your job, send a ping when it runs
public async Task RunDailyBackup()
{
    // Your backup logic here
    await PerformBackup();

    // Ping CronRadar
    await cronRadar.PingAsync("daily-backup");
}
```

### 3. Monitor Pattern (Recommended)

Wrap your jobs to automatically ping on success or report failures:

```csharp
// Synchronous job
cronRadar.Monitor("daily-backup", () =>
{
    // Your job logic here
    PerformBackup();
    // CronRadar will be pinged automatically on success
});

// Async job with return value
var result = await cronRadar.MonitorAsync("data-process", async () =>
{
    var data = await FetchData();
    await ProcessData(data);
    return data.Count; // CronRadar pinged on success
});

// If the job throws, CronRadar will be notified of the failure
```

## ASP.NET Core Integration

### Configure with Dependency Injection

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add CronRadar with configuration
builder.Services.AddCronRadar(options =>
{
    options.ApiKey = "ck_app_xxxxx_yyyyy"; // Or use environment variable
    options.BaseUrl = "https://cronradar.com"; // Optional
    options.Timeout = TimeSpan.FromSeconds(5); // Optional
    options.FailSilently = true; // Default: true

    // Auto-sync monitors on startup
    options.AutoSync = true;
    options.Monitors = new[]
    {
        new Monitor("daily-backup", "0 2 * * *"),
        new Monitor("hourly-sync", "0 * * * *")
    };
});

var app = builder.Build();
```

### Use in Services

```csharp
public class BackupService
{
    private readonly ICronRadarClient _cronRadar;

    public BackupService(ICronRadarClient cronRadar)
    {
        _cronRadar = cronRadar;
    }

    public async Task RunBackup()
    {
        await _cronRadar.MonitorAsync("daily-backup", async () =>
        {
            // Your backup logic
            await PerformDatabaseBackup();
        });
    }
}
```

## Hangfire Integration

### Basic Setup

```csharp
using CronRadar.Extensions;

// In your Hangfire configuration
var cronRadar = new CronRadarClient();

// Sync monitor and create Hangfire job
RecurringJob.AddOrUpdate(
    "daily-backup",
    cronRadar.CreateMonitoredJob("daily-backup", () => RunBackup()),
    "0 2 * * *"
);
```

### Advanced Hangfire Integration

```csharp
// Extension method for IRecurringJobManager (requires Hangfire.Core)
recurringJobManager.AddOrUpdateWithMonitoring(
    cronRadar,
    "daily-backup",
    () => BackupService.Run(),
    "0 2 * * *",
    "Database Backup" // Optional display name
);

// For async jobs
recurringJobManager.AddOrUpdateWithMonitoring(
    cronRadar,
    "data-sync",
    async () => await DataService.SyncAsync(),
    "0 * * * *"
);
```

## Background Service Example

```csharp
public class ScheduledBackupService : BackgroundService
{
    private readonly ICronRadarClient _cronRadar;
    private readonly ILogger<ScheduledBackupService> _logger;

    public ScheduledBackupService(ICronRadarClient cronRadar, ILogger<ScheduledBackupService> logger)
    {
        _cronRadar = cronRadar;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sync monitors on startup
        await _cronRadar.SyncAsync(new[]
        {
            new Monitor("hourly-backup", "0 * * * *")
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _cronRadar.MonitorAsync("hourly-backup", async () =>
                {
                    _logger.LogInformation("Starting backup...");
                    await PerformBackup();
                    _logger.LogInformation("Backup completed");
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Backup failed");
            }

            // Wait for next hour
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task PerformBackup()
    {
        // Your backup logic here
    }
}
```

## Configuration Options

```csharp
var options = new CronRadarOptions
{
    // API key for authentication (default: reads from CRONRADAR_API_KEY env var)
    ApiKey = "ck_app_xxxxx_yyyyy",

    // Base URL for CronRadar API (default: https://cronradar.com)
    BaseUrl = "https://cronradar.com",

    // HTTP timeout (default: 5 seconds)
    Timeout = TimeSpan.FromSeconds(5),

    // Fail silently on errors to prevent breaking your app (default: true)
    FailSilently = true,

    // Enable auto-sync on startup (for DI integration)
    AutoSync = true,

    // Monitors to sync on startup
    Monitors = new[]
    {
        new Monitor("job-1", "0 * * * *")
    }
};

var cronRadar = new CronRadarClient(apiKey: null, options);
```

## Monitor Model

```csharp
var monitor = new Monitor
{
    Key = "daily-backup",           // Required: Unique identifier
    Schedule = "0 2 * * *",         // Required: Cron expression
    Name = "Database Backup",       // Optional: Display name
    GracePeriod = 300,             // Optional: Seconds before alerting (default: 60)
    Timezone = "America/New_York"   // Optional: Timezone (default: UTC)
};
```

## Error Handling

The SDK is designed to never break your application:

```csharp
// By default, errors are caught and logged (fail silently)
await cronRadar.PingAsync("monitor-key"); // Returns false on error

// You can disable fail-silently if needed
var options = new CronRadarOptions { FailSilently = false };
var cronRadar = new CronRadarClient(options: options);

// Now errors will be thrown
try
{
    await cronRadar.PingAsync("monitor-key");
}
catch (Exception ex)
{
    // Handle error
}
```

## API Methods

### Core Methods

```csharp
// Sync monitors with CronRadar
Task<bool> SyncAsync(IEnumerable<Monitor> monitors)
bool Sync(IEnumerable<Monitor> monitors)

// Send a success ping
Task<bool> PingAsync(string monitorKey)
bool Ping(string monitorKey)

// Report a failure
Task<bool> FailAsync(string monitorKey, string? error = null)
bool Fail(string monitorKey, string? error = null)

// Monitor job execution
Task<T> MonitorAsync<T>(string monitorKey, Func<Task<T>> action)
T Monitor<T>(string monitorKey, Func<T> action)
```

## Framework Support

- **.NET 6.0+** - Full support with all features
- **.NET 7.0** - Full support with all features
- **.NET 8.0** - Full support with all features
- **.NET Standard 2.0** - Core functionality (supports .NET Framework 4.6.1+)
- **.NET Standard 2.1** - Core functionality with performance improvements

## Best Practices

1. **Sync on Startup** - Always sync your monitors when your application starts
2. **Use Monitor Pattern** - Wrap jobs with `Monitor()` or `MonitorAsync()` for automatic error handling
3. **Set Reasonable Timeouts** - Default 5 seconds is good for most cases
4. **Use Environment Variables** - Store API keys in environment variables, not in code
5. **Handle Failures Gracefully** - The SDK won't break your app, but log failures for debugging

## Common Patterns

### Console Application

```csharp
class Program
{
    static async Task Main(string[] args)
    {
        var cronRadar = new CronRadarClient();

        // Define and sync monitors
        await cronRadar.SyncAsync(new[]
        {
            new Monitor("daily-job", "0 0 * * *")
        });

        // Run job with monitoring
        await cronRadar.MonitorAsync("daily-job", async () =>
        {
            Console.WriteLine("Running daily job...");
            await ProcessDailyData();
            Console.WriteLine("Daily job completed!");
        });
    }
}
```

### Azure Functions

```csharp
public class ScheduledFunction
{
    private readonly ICronRadarClient _cronRadar;

    public ScheduledFunction(ICronRadarClient cronRadar)
    {
        _cronRadar = cronRadar;
    }

    [FunctionName("DailyBackup")]
    public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer)
    {
        await _cronRadar.MonitorAsync("daily-backup", async () =>
        {
            // Your function logic here
        });
    }
}
```

### Windows Service

```csharp
public class WorkerService : BackgroundService
{
    private readonly ICronRadarClient _cronRadar;

    public WorkerService(ICronRadarClient cronRadar)
    {
        _cronRadar = cronRadar;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Sync on startup
        await _cronRadar.SyncAsync(new[]
        {
            new Monitor("worker-heartbeat", "*/5 * * * *")
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            await _cronRadar.PingAsync("worker-heartbeat");
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
```

## Troubleshooting

### API Key Not Found

```
CronRadar API key is not configured. Set CRONRADAR_API_KEY environment variable or provide it in the constructor.
```

**Solution:** Set the `CRONRADAR_API_KEY` environment variable or pass it directly:
```csharp
var cronRadar = new CronRadarClient("ck_app_xxxxx_yyyyy");
```

### Network Timeouts

If you're experiencing timeouts, increase the timeout setting:
```csharp
var options = new CronRadarOptions
{
    Timeout = TimeSpan.FromSeconds(10)
};
```

### Silent Failures

Enable logging to see what's happening:
```csharp
builder.Services.AddCronRadar(options =>
{
    options.Logger = loggerFactory.CreateLogger<CronRadarClient>();
});
```

## Support

- 📧 Email: support@cronradar.com
- 📚 Documentation: https://cronradar.com/docs
- 🐛 Issues: [GitHub Issues](https://github.com/cronradar/cronradar-dotnet/issues)
- 💬 Discord: [Join our community](https://discord.gg/cronradar)

## License

This SDK is released under the MIT License. See [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

---

Made with ❤️ by [CronRadar](https://cronradar.com)