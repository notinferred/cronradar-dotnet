#if NETSTANDARD2_0_OR_GREATER || NET6_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Hangfire;
using Hangfire.Server;
using Hangfire.Common;
using Hangfire.States;

namespace CronRadar.Extensions
{
    /// <summary>
    /// Simple Hangfire job filter that monitors job execution with CronRadar.
    /// </summary>
    public class CronRadarJobFilter : JobFilterAttribute, IServerFilter
    {
        private readonly ICronRadarClient _client;
        private readonly ILogger? _logger;
        private readonly ConcurrentDictionary<string, bool> _syncedJobs = new();

        public CronRadarJobFilter(ICronRadarClient? client = null, ILogger? logger = null)
        {
            _client = client ?? new CronRadarClient();
            _logger = logger;
        }

        public void OnPerforming(PerformingContext context)
        {
            // Not needed - we sync on job registration
        }

        public void OnPerformed(PerformedContext context)
        {
            try
            {
                var jobId = GetJobId(context);
                if (string.IsNullOrEmpty(jobId)) return;

                // Report job result to CronRadar
                if (context.Exception == null && !context.Canceled)
                {
                    _client.Ping(jobId);
                    _logger?.LogDebug("CronRadar: Pinged {JobId}", jobId);
                }
                else if (context.Exception != null)
                {
                    _client.Fail(jobId, context.Exception.Message);
                    _logger?.LogDebug("CronRadar: Reported failure for {JobId}", jobId);
                }
            }
            catch
            {
                // Never throw - protect the application
            }
        }

        private string GetJobId(PerformContext context)
        {
            try
            {
                // For recurring jobs, use the recurring job ID
                if (context.BackgroundJob?.Job != null)
                {
                    var jobType = context.BackgroundJob.Job.Type;
                    var methodName = context.BackgroundJob.Job.Method.Name;
                    return $"{jobType.Name}.{methodName}";
                }

                return context.BackgroundJob?.Id ?? "";
            }
            catch
            {
                return "";
            }
        }
    }

    /// <summary>
    /// Wrapper for IRecurringJobManager that syncs monitors when jobs are registered
    /// </summary>
    public class MonitoredRecurringJobManager : IRecurringJobManager
    {
        private readonly IRecurringJobManager _inner;
        private readonly ICronRadarClient _client;
        private readonly ILogger? _logger;

        public MonitoredRecurringJobManager(IRecurringJobManager inner, ICronRadarClient? client = null, ILogger? logger = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _client = client ?? new CronRadarClient();
            _logger = logger;
        }

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            // Register with Hangfire
            _inner.AddOrUpdate(recurringJobId, job, cronExpression, options);

            // Sync with CronRadar
            SyncMonitor(recurringJobId, cronExpression, job?.Type?.Name ?? recurringJobId);
        }

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, TimeZoneInfo timeZone, string queue)
        {
            // Register with Hangfire
            _inner.AddOrUpdate(recurringJobId, job, cronExpression, timeZone, queue);

            // Sync with CronRadar
            SyncMonitor(recurringJobId, cronExpression, job?.Type?.Name ?? recurringJobId);
        }

        public void Trigger(string recurringJobId)
        {
            _inner.Trigger(recurringJobId);
        }

        public void RemoveIfExists(string recurringJobId)
        {
            _inner.RemoveIfExists(recurringJobId);
        }

        private void SyncMonitor(string jobId, string cronExpression, string jobName)
        {
            try
            {
                var monitor = new Monitor(jobId, cronExpression, jobName);
                _client.Sync(new[] { monitor });
                _logger?.LogDebug("CronRadar: Synced monitor {JobId} with cron {Cron}", jobId, cronExpression);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("CronRadar: Failed to sync monitor {JobId} - {Error}", jobId, ex.Message);
            }
        }
    }

    /// <summary>
    /// Extension methods for simple CronRadar integration with Hangfire
    /// </summary>
    public static class CronRadarHangfireExtensions
    {
        private static ICronRadarClient? _client;

        /// <summary>
        /// Enables CronRadar monitoring for all Hangfire jobs.
        /// This is the ONLY method you need to call.
        /// </summary>
        public static IGlobalConfiguration UseCronRadar(this IGlobalConfiguration configuration, ICronRadarClient? client = null)
        {
            try
            {
                if (configuration == null) return configuration;

                // Initialize client (auto-discovers API key from env/config)
                _client = client ?? new CronRadarClient();

                // Add job filter for runtime monitoring
                GlobalJobFilters.Filters.Add(new CronRadarJobFilter(_client));

                return configuration;
            }
            catch
            {
                // Never break the application
                return configuration;
            }
        }

        /// <summary>
        /// Extension for IRecurringJobManager to make job registration sync monitors automatically
        /// </summary>
        public static void AddOrUpdate<T>(
            this IRecurringJobManager manager,
            string recurringJobId,
            System.Linq.Expressions.Expression<Func<T, System.Threading.Tasks.Task>> methodCall,
            string cronExpression)
        {
            // Add to Hangfire
            manager.AddOrUpdate(recurringJobId, methodCall, cronExpression);

            // Sync with CronRadar if initialized
            if (_client != null)
            {
                try
                {
                    var monitor = new Monitor(recurringJobId, cronExpression, typeof(T).Name);
                    _client.Sync(new[] { monitor });
                }
                catch
                {
                    // Never break
                }
            }
        }

        /// <summary>
        /// Extension for IRecurringJobManager to make job registration sync monitors automatically
        /// </summary>
        public static void AddOrUpdate<T>(
            this IRecurringJobManager manager,
            string recurringJobId,
            System.Linq.Expressions.Expression<Action<T>> methodCall,
            string cronExpression)
        {
            // Add to Hangfire
            manager.AddOrUpdate(recurringJobId, methodCall, cronExpression);

            // Sync with CronRadar if initialized
            if (_client != null)
            {
                try
                {
                    var monitor = new Monitor(recurringJobId, cronExpression, typeof(T).Name);
                    _client.Sync(new[] { monitor });
                }
                catch
                {
                    // Never break
                }
            }
        }
    }
}
#endif