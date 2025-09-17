#if NETSTANDARD2_0_OR_GREATER || NET6_0_OR_GREATER
using System;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace CronRadar.Extensions
{
    /// <summary>
    /// Extension methods for Hangfire integration with CronRadar.
    /// Note: Requires Hangfire.Core package to be installed separately.
    /// </summary>
    public static class HangfireExtensions
    {
        /// <summary>
        /// Configures Hangfire to use CronRadar for job monitoring.
        /// Call this after configuring Hangfire services.
        /// </summary>
        /// <param name="configuration">Hangfire global configuration.</param>
        /// <param name="cronRadarClient">CronRadar client instance.</param>
        /// <returns>The configuration for chaining.</returns>
        public static object UseCronRadar(this object configuration, ICronRadarClient cronRadarClient)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            if (cronRadarClient == null) throw new ArgumentNullException(nameof(cronRadarClient));

            // Note: This is a placeholder for Hangfire integration
            // Actual implementation would require Hangfire.Core reference
            // Users should install Hangfire separately and use the extension methods below

            return configuration;
        }

        /// <summary>
        /// Adds or updates a recurring job with CronRadar monitoring.
        /// </summary>
        /// <param name="recurringJobManager">Hangfire recurring job manager (dynamic to avoid hard dependency).</param>
        /// <param name="cronRadar">CronRadar client instance.</param>
        /// <param name="monitorKey">The monitor key for CronRadar.</param>
        /// <param name="methodCall">The job method to execute.</param>
        /// <param name="cronExpression">The cron expression for scheduling.</param>
        /// <param name="name">Optional display name for the monitor.</param>
        public static void AddOrUpdateWithMonitoring(
            this object recurringJobManager,
            ICronRadarClient cronRadar,
            string monitorKey,
            Expression<Action> methodCall,
            string cronExpression,
            string? name = null)
        {
            if (recurringJobManager == null) throw new ArgumentNullException(nameof(recurringJobManager));
            if (cronRadar == null) throw new ArgumentNullException(nameof(cronRadar));
            if (string.IsNullOrEmpty(monitorKey)) throw new ArgumentNullException(nameof(monitorKey));

            // Sync the monitor with CronRadar
            var monitor = new Monitor(monitorKey, cronExpression, name);
            cronRadar.Sync(new[] { monitor });

            // Create a wrapper that pings CronRadar
            Expression<Action> wrappedCall = () => ExecuteWithMonitoring(cronRadar, monitorKey, methodCall.Compile());

            // Use reflection to call Hangfire's AddOrUpdate method
            // This avoids a hard dependency on Hangfire
            var method = recurringJobManager.GetType().GetMethod("AddOrUpdate",
                new[] { typeof(string), typeof(Expression<Action>), typeof(string) });

            if (method != null)
            {
                method.Invoke(recurringJobManager, new object[] { monitorKey, wrappedCall, cronExpression });
            }
        }

        /// <summary>
        /// Adds or updates a recurring async job with CronRadar monitoring.
        /// </summary>
        /// <param name="recurringJobManager">Hangfire recurring job manager (dynamic to avoid hard dependency).</param>
        /// <param name="cronRadar">CronRadar client instance.</param>
        /// <param name="monitorKey">The monitor key for CronRadar.</param>
        /// <param name="methodCall">The async job method to execute.</param>
        /// <param name="cronExpression">The cron expression for scheduling.</param>
        /// <param name="name">Optional display name for the monitor.</param>
        public static void AddOrUpdateWithMonitoring(
            this object recurringJobManager,
            ICronRadarClient cronRadar,
            string monitorKey,
            Expression<Func<Task>> methodCall,
            string cronExpression,
            string? name = null)
        {
            if (recurringJobManager == null) throw new ArgumentNullException(nameof(recurringJobManager));
            if (cronRadar == null) throw new ArgumentNullException(nameof(cronRadar));
            if (string.IsNullOrEmpty(monitorKey)) throw new ArgumentNullException(nameof(monitorKey));

            // Sync the monitor with CronRadar
            var monitor = new Monitor(monitorKey, cronExpression, name);
            cronRadar.Sync(new[] { monitor });

            // Create a wrapper that pings CronRadar
            Expression<Func<Task>> wrappedCall = () => ExecuteWithMonitoringAsync(cronRadar, monitorKey, methodCall.Compile());

            // Use reflection to call Hangfire's AddOrUpdate method
            var method = recurringJobManager.GetType().GetMethod("AddOrUpdate",
                new[] { typeof(string), typeof(Expression<Func<Task>>), typeof(string) });

            if (method != null)
            {
                method.Invoke(recurringJobManager, new object[] { monitorKey, wrappedCall, cronExpression });
            }
        }

        private static void ExecuteWithMonitoring(ICronRadarClient cronRadar, string monitorKey, Action action)
        {
            try
            {
                action();
                cronRadar.Ping(monitorKey);
            }
            catch (Exception ex)
            {
                cronRadar.Fail(monitorKey, ex.Message);
                throw;
            }
        }

        private static async Task ExecuteWithMonitoringAsync(ICronRadarClient cronRadar, string monitorKey, Func<Task> action)
        {
            try
            {
                await action();
                await cronRadar.PingAsync(monitorKey);
            }
            catch (Exception ex)
            {
                await cronRadar.FailAsync(monitorKey, ex.Message);
                throw;
            }
        }
    }

    /// <summary>
    /// Simplified Hangfire integration for direct use without Hangfire dependency.
    /// </summary>
    public static class CronRadarJobExtensions
    {
        /// <summary>
        /// Creates a monitored job action that reports to CronRadar.
        /// </summary>
        /// <param name="cronRadar">CronRadar client instance.</param>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The job action to execute.</param>
        /// <returns>An action that executes the job with monitoring.</returns>
        public static Action CreateMonitoredJob(this ICronRadarClient cronRadar, string monitorKey, Action action)
        {
            return () => cronRadar.Monitor(monitorKey, action);
        }

        /// <summary>
        /// Creates a monitored async job that reports to CronRadar.
        /// </summary>
        /// <param name="cronRadar">CronRadar client instance.</param>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The async job action to execute.</param>
        /// <returns>A task that executes the job with monitoring.</returns>
        public static Func<Task> CreateMonitoredJob(this ICronRadarClient cronRadar, string monitorKey, Func<Task> action)
        {
            return () => cronRadar.MonitorAsync(monitorKey, action);
        }

        /// <summary>
        /// Creates a monitored job with return value that reports to CronRadar.
        /// </summary>
        /// <typeparam name="T">Return type of the job.</typeparam>
        /// <param name="cronRadar">CronRadar client instance.</param>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The job function to execute.</param>
        /// <returns>A function that executes the job with monitoring.</returns>
        public static Func<T> CreateMonitoredJob<T>(this ICronRadarClient cronRadar, string monitorKey, Func<T> action)
        {
            return () => cronRadar.Monitor(monitorKey, action);
        }

        /// <summary>
        /// Creates a monitored async job with return value that reports to CronRadar.
        /// </summary>
        /// <typeparam name="T">Return type of the job.</typeparam>
        /// <param name="cronRadar">CronRadar client instance.</param>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The async job function to execute.</param>
        /// <returns>A task that executes the job with monitoring.</returns>
        public static Func<Task<T>> CreateMonitoredJob<T>(this ICronRadarClient cronRadar, string monitorKey, Func<Task<T>> action)
        {
            return () => cronRadar.MonitorAsync(monitorKey, action);
        }
    }
}
#endif