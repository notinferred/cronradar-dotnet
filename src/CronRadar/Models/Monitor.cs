using System;

namespace CronRadar
{
    /// <summary>
    /// Represents a monitor configuration for CronRadar.
    /// </summary>
    public class Monitor
    {
        /// <summary>
        /// Gets or sets the unique key for the monitor.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the cron schedule expression.
        /// </summary>
        public string Schedule { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the human-readable name for the monitor.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the grace period in seconds before alerting.
        /// Default is 60 seconds.
        /// </summary>
        public int? GracePeriod { get; set; }

        /// <summary>
        /// Gets or sets the timezone for the schedule.
        /// Default is UTC.
        /// </summary>
        public string? Timezone { get; set; }

        /// <summary>
        /// Initializes a new instance of the Monitor class.
        /// </summary>
        public Monitor()
        {
        }

        /// <summary>
        /// Initializes a new instance of the Monitor class with required parameters.
        /// </summary>
        /// <param name="key">The unique key for the monitor.</param>
        /// <param name="schedule">The cron schedule expression.</param>
        public Monitor(string key, string schedule)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        }

        /// <summary>
        /// Initializes a new instance of the Monitor class with all parameters.
        /// </summary>
        /// <param name="key">The unique key for the monitor.</param>
        /// <param name="schedule">The cron schedule expression.</param>
        /// <param name="name">The human-readable name.</param>
        /// <param name="gracePeriod">The grace period in seconds.</param>
        /// <param name="timezone">The timezone for the schedule.</param>
        public Monitor(string key, string schedule, string? name = null, int? gracePeriod = null, string? timezone = null)
            : this(key, schedule)
        {
            Name = name;
            GracePeriod = gracePeriod;
            Timezone = timezone;
        }
    }
}