using System;

namespace CronRadar
{
    /// <summary>
    /// Shared constants for CronRadar SDK.
    /// </summary>
    public static class CronRadarConstants
    {
        /// <summary>
        /// Maximum length for a monitor key (200 characters).
        /// </summary>
        public const int MaxMonitorKeyLength = 200;

        /// <summary>
        /// Default HTTP request timeout in seconds (5 seconds).
        /// </summary>
        public const int DefaultTimeoutSeconds = 5;

        /// <summary>
        /// Default grace period for monitors in seconds (60 seconds / 1 minute).
        /// </summary>
        public const int DefaultGracePeriodSeconds = 60;

        /// <summary>
        /// Default CronRadar API base URL.
        /// Can be overridden via CRONRADAR_BASE_URL environment variable.
        /// </summary>
        public static string DefaultBaseUrl =>
            Environment.GetEnvironmentVariable("CRONRADAR_BASE_URL") ?? "https://cron.life";

        /// <summary>
        /// Environment variable name for API key.
        /// </summary>
        public const string ApiKeyEnvironmentVariable = "CRONRADAR_API_KEY";

        /// <summary>
        /// Environment variable name for debug logging.
        /// </summary>
        public const string DebugEnvironmentVariable = "CRONRADAR_DEBUG";

        /// <summary>
        /// Environment variable name for custom base URL.
        /// </summary>
        public const string BaseUrlEnvironmentVariable = "CRONRADAR_BASE_URL";
    }
}
