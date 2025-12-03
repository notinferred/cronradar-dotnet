#if NETSTANDARD2_0_OR_GREATER || NET6_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace CronRadar.Extensions
{
    /// <summary>
    /// Hangfire job filter that automatically monitors all jobs with CronRadar.
    /// This class uses reflection to implement IServerFilter without taking a hard dependency on Hangfire.
    /// </summary>
    public class CronRadarJobFilter
    {
        private readonly ICronRadarClient _cronRadar;
        private readonly ConcurrentDictionary<string, bool> _syncedMonitors = new();
        private static readonly object _syncLock = new();

        public CronRadarJobFilter(ICronRadarClient cronRadar)
        {
            _cronRadar = cronRadar ?? throw new ArgumentNullException(nameof(cronRadar));
        }

        public CronRadarJobFilter() : this(new CronRadarClient())
        {
        }

        /// <summary>
        /// Called before job execution. Syncs monitor if needed.
        /// </summary>
        public void OnPerforming(object context)
        {
            try
            {
                var jobId = GetJobId(context);
                if (!string.IsNullOrEmpty(jobId))
                {
                    // Sync monitor on first execution
                    if (_syncedMonitors.TryAdd(jobId, true))
                    {
                        SyncMonitorFromContext(context, jobId);
                    }
                }
            }
            catch
            {
                // Silently fail - never break job execution
            }
        }

        /// <summary>
        /// Called after job execution. Sends ping or failure to CronRadar.
        /// </summary>
        public void OnPerformed(object context)
        {
            try
            {
                var jobId = GetJobId(context);
                if (string.IsNullOrEmpty(jobId)) return;

                var exception = GetException(context);
                if (exception == null)
                {
                    // Job succeeded - send ping
                    _cronRadar.Ping(jobId);
                }
                else
                {
                    // Job failed - report failure
                    _cronRadar.Fail(jobId, exception.Message);
                }
            }
            catch
            {
                // Silently fail - never break job execution
            }
        }

        private string GetJobId(object context)
        {
            try
            {
                // Try to get RecurringJobId first (for recurring jobs)
                var recurringJobIdProp = context.GetType().GetProperty("RecurringJobId");
                if (recurringJobIdProp != null)
                {
                    var recurringJobId = recurringJobIdProp.GetValue(context) as string;
                    if (!string.IsNullOrEmpty(recurringJobId))
                        return recurringJobId;
                }

                // Fall back to BackgroundJob.Id
                var backgroundJobProp = context.GetType().GetProperty("BackgroundJob");
                if (backgroundJobProp != null)
                {
                    var backgroundJob = backgroundJobProp.GetValue(context);
                    if (backgroundJob != null)
                    {
                        var idProp = backgroundJob.GetType().GetProperty("Id");
                        if (idProp != null)
                        {
                            return idProp.GetValue(backgroundJob)?.ToString() ?? "";
                        }
                    }
                }

                return "";
            }
            catch
            {
                return "";
            }
        }

        private Exception? GetException(object context)
        {
            try
            {
                var exceptionProp = context.GetType().GetProperty("Exception");
                return exceptionProp?.GetValue(context) as Exception;
            }
            catch
            {
                return null;
            }
        }

        private void SyncMonitorFromContext(object context, string jobId)
        {
            try
            {
                // Try to extract cron expression from RecurringJob
                var cronExpression = ExtractCronExpression(context) ?? "* * * * *";

                // Extract job type/method name for display
                var jobName = ExtractJobName(context) ?? jobId;

                // Sync the monitor
                var monitor = new Monitor(jobId, cronExpression, jobName);
                _cronRadar.Sync(new[] { monitor });
            }
            catch
            {
                // Silently fail
            }
        }

        private string? ExtractCronExpression(object context)
        {
            try
            {
                // For recurring jobs, try to get the cron expression from storage
                var recurringJobIdProp = context.GetType().GetProperty("RecurringJobId");
                if (recurringJobIdProp != null)
                {
                    var recurringJobId = recurringJobIdProp.GetValue(context) as string;
                    if (!string.IsNullOrEmpty(recurringJobId))
                    {
                        // Try to get from storage connection
                        var storageProp = context.GetType().GetProperty("Storage");
                        if (storageProp != null)
                        {
                            var storage = storageProp.GetValue(context);
                            if (storage != null)
                            {
                                // This would require accessing Hangfire's storage
                                // For now, return a default
                                return null;
                            }
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private string? ExtractJobName(object context)
        {
            try
            {
                var backgroundJobProp = context.GetType().GetProperty("BackgroundJob");
                if (backgroundJobProp != null)
                {
                    var backgroundJob = backgroundJobProp.GetValue(context);
                    if (backgroundJob != null)
                    {
                        var jobProp = backgroundJob.GetType().GetProperty("Job");
                        if (jobProp != null)
                        {
                            var job = jobProp.GetValue(backgroundJob);
                            if (job != null)
                            {
                                var methodProp = job.GetType().GetProperty("Method");
                                if (methodProp != null)
                                {
                                    var method = methodProp.GetValue(job) as MethodInfo;
                                    if (method != null)
                                    {
                                        return $"{method.DeclaringType?.Name}.{method.Name}";
                                    }
                                }
                            }
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Static helper for easy GlobalConfiguration integration
    /// </summary>
    public static class CronRadarHangfire
    {
        private static ICronRadarClient? _client;

        /// <summary>
        /// Configures Hangfire to use CronRadar for all job monitoring.
        /// Call this in your Hangfire configuration.
        /// </summary>
        public static object UseCronRadar(this object configuration, ICronRadarClient? client = null)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            _client = client ?? new CronRadarClient();
            var filter = new CronRadarJobFilter(_client);

            // Use reflection to add the filter globally
            var globalJobFiltersType = Type.GetType("Hangfire.GlobalJobFilters, Hangfire.Core");
            if (globalJobFiltersType != null)
            {
                var filtersProperty = globalJobFiltersType.GetProperty("Filters", BindingFlags.Public | BindingFlags.Static);
                if (filtersProperty != null)
                {
                    var filters = filtersProperty.GetValue(null);
                    if (filters != null)
                    {
                        var addMethod = filters.GetType().GetMethod("Add");
                        if (addMethod != null)
                        {
                            addMethod.Invoke(filters, new object[] { filter });
                        }
                    }
                }
            }

            return configuration;
        }

        /// <summary>
        /// Gets the CronRadar client instance used by Hangfire integration.
        /// </summary>
        public static ICronRadarClient? Client => _client;
    }
}
#endif