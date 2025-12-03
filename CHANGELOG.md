# Changelog

All notable changes to the CronRadar .NET SDK will be documented in this file.

## [1.0.2] - 2024-01-17

### 🚀 NEW: One-Line Hangfire Integration

This release introduces the simplest possible Hangfire integration - just add `.UseCronRadar()` to your configuration!

### Added
- **`.UseCronRadar()` extension method** - Monitor ALL Hangfire jobs with one line
- **`CronRadarJobFilter`** - Automatic job interception using Hangfire's filter pattern
- **Zero-configuration setup** - No changes needed to existing job code
- **Automatic job discovery** - Finds and monitors all your Hangfire jobs
- **Smart syncing** - Only syncs monitors on first execution

### Improved
- Enhanced fail-safe operation - never breaks your jobs
- Better error handling with graceful degradation
- Improved documentation with real-world examples
- Reflection-based integration (no hard Hangfire dependency)

### Technical Details
- Implements `IServerFilter` pattern for job interception
- Uses reflection to maintain compatibility without hard dependencies
- Caches job metadata for optimal performance
- Thread-safe concurrent dictionary for monitor tracking

## [1.0.1] - 2024-01-15

### Fixed
- Improved HTTP client configuration
- Better timeout handling
- Enhanced error messages

## [1.0.0] - 2024-01-10

### Initial Release
- Core monitoring functionality
- Sync/Async API methods
- ASP.NET Core DI integration
- Basic Hangfire support
- Environment variable configuration
- Fail-safe design

---

## Migration Guide

### From 1.0.0/1.0.1 to 1.0.2

The new version is fully backward compatible. To use the new one-line integration:

**Before (1.0.0/1.0.1):**
```csharp
var cronRadar = new CronRadarClient();
RecurringJob.AddOrUpdate(
    "my-job",
    cronRadar.CreateMonitoredJob("my-job", () => MyMethod()),
    "0 * * * *"
);
```

**After (1.0.2):**
```csharp
// Just add this line to Hangfire config - ALL jobs monitored!
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(connectionString)
    .UseCronRadar());  // ← ONE LINE!
```

Your existing code continues to work, but the new approach is much simpler!