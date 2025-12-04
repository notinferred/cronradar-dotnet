using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CronRadar.Extensions
{
    /// <summary>
    /// Background service that syncs monitors on startup.
    /// </summary>
    internal class CronRadarHostedService : IHostedService
    {
        private readonly ICronRadarClient _client;
        private readonly CronRadarOptions _options;
        private readonly ILogger<CronRadarHostedService>? _logger;

        public CronRadarHostedService(
            ICronRadarClient client,
            IOptions<CronRadarOptions> options,
            ILogger<CronRadarHostedService>? logger = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (_options.AutoSync && _options.Monitors != null && _options.Monitors.Length > 0)
            {
                try
                {
                    _logger?.LogInformation("Starting CronRadar monitor sync with {Count} monitors", _options.Monitors.Length);
                    var success = await _client.SyncAsync(_options.Monitors, cancellationToken);

                    if (success)
                    {
                        _logger?.LogInformation("CronRadar monitor sync completed successfully");
                    }
                    else
                    {
                        _logger?.LogWarning("CronRadar monitor sync failed");
                    }
                }
                catch (Exception ex)
                {
                    // Don't throw - we don't want to prevent the app from starting
                    _logger?.LogError(ex, "Error during CronRadar monitor sync");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // Nothing to clean up
            return Task.CompletedTask;
        }
    }
}