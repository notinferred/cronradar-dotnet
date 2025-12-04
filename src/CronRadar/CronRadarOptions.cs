using System;
using Microsoft.Extensions.Logging;

namespace CronRadar
{
    /// <summary>
    /// Configuration options for CronRadar client.
    /// </summary>
    public class CronRadarOptions
    {
        /// <summary>
        /// Gets or sets the API key for authentication.
        /// If not set, will attempt to read from CRONRADAR_API_KEY environment variable.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Gets or sets the base URL for CronRadar API.
        /// Default is https://cronradar.com
        /// </summary>
        public string BaseUrl { get; set; } = "https://cronradar.com";

        /// <summary>
        /// Gets or sets the HTTP timeout.
        /// Default is 5 seconds.
        /// </summary>
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Gets or sets whether to fail silently on errors.
        /// When true, exceptions are caught and logged instead of thrown.
        /// Default is true to prevent breaking the host application.
        /// </summary>
        public bool FailSilently { get; set; } = true;

        /// <summary>
        /// Gets or sets the logger instance for diagnostic logging.
        /// </summary>
        public ILogger<CronRadarClient>? Logger { get; set; }

        /// <summary>
        /// Gets or sets whether to enable automatic syncing on startup.
        /// Used by dependency injection integration.
        /// </summary>
        public bool AutoSync { get; set; } = true;

        /// <summary>
        /// Gets or sets the monitors to sync on startup.
        /// Used when AutoSync is enabled.
        /// </summary>
        public Monitor[]? Monitors { get; set; }
    }
}