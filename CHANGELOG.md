# Changelog

All notable changes to the CronRadar .NET SDK will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.11] - 2025-01-20

### Added
- Implemented IClientFilter and IElectStateFilter interfaces for enhanced job tracking
- Multiple fallback mechanisms for capturing RecurringJobId in various Hangfire configurations

### Improved
- Enhanced RecurringJobId capture through multiple filter interfaces
- More reliable job ID detection during job creation and state transitions
- Better support for different Hangfire versions and configurations

## [1.0.10] - 2025-01-19

### Added
- Automatic monitor registration on 404 responses
- Monitor configuration caching for resilience
- PingAsync overload with schedule/gracePeriod parameters

### Improved
- Self-healing monitors that recover from deletion
- Server API simplified to single GET endpoint with query params
- Full backward compatibility maintained

## [1.0.9] - 2025-01-15

### Added
- Support for .NET 9.0
- Enhanced error handling and retry logic

### Improved
- Performance optimizations for high-throughput scenarios
- Better logging for debugging

## Previous Versions

For version history before 1.0.9, please refer to the NuGet package release notes.

[1.0.11]: https://github.com/cronradar/cronradar-dotnet/compare/v1.0.10...v1.0.11
[1.0.10]: https://github.com/cronradar/cronradar-dotnet/compare/v1.0.9...v1.0.10
[1.0.9]: https://github.com/cronradar/cronradar-dotnet/releases/tag/v1.0.9