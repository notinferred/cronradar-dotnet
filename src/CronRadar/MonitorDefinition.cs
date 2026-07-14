namespace CronRadar
{
    /// <summary>
    /// A single discovered monitor passed to <see cref="CronRadar.SyncMonitors"/> when an
    /// extension reconciles its framework's schedule.
    /// </summary>
    public class MonitorDefinition
    {
        /// <summary>Unique monitor key (normalized job identifier).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Cron expression describing when the job runs.</summary>
        public string Schedule { get; set; } = string.Empty;

        /// <summary>Human-readable name. Generated from the key if null.</summary>
        public string? Name { get; set; }

        /// <summary>Grace period in seconds. Uses the SDK default if null.</summary>
        public int? GracePeriod { get; set; }
    }
}
