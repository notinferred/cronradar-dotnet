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
        /// Base SDK version, reported to the server on sync/reconcile.
        /// </summary>
        public const string SdkVersion = "0.1.0";

        /// <summary>
        /// CronRadar API base URL.
        /// </summary>
        internal const string BaseUrl = "https://cron.life";

        /// <summary>Optional environment variable for local testing or a private ingestion endpoint.</summary>
        public const string BaseUrlEnvironmentVariable = "CRONRADAR_BASE_URL";

        /// <summary>
        /// Environment variable name for API key.
        /// </summary>
        public const string ApiKeyEnvironmentVariable = "CRONRADAR_API_KEY";

        /// <summary>
        /// Environment variable name for debug logging.
        /// </summary>
        public const string DebugEnvironmentVariable = "CRONRADAR_DEBUG";
    }
}
