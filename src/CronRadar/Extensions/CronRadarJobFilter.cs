#if NETSTANDARD2_0_OR_GREATER || NET6_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Hangfire;
using Hangfire.Server;
using Hangfire.Common;

namespace CronRadar.Extensions
{
    /// <summary>
    /// Hangfire job filter that automatically monitors all jobs with CronRadar.
    /// NEVER throws exceptions - fails silently to protect your application.
    /// </summary>
    public class CronRadarJobFilter : JobFilterAttribute, IServerFilter
    {
        private readonly ICronRadarClient _cronRadar;
        private readonly ILogger? _logger;
        private readonly ConcurrentDictionary<string, bool> _syncedMonitors = new();

        public CronRadarJobFilter(ICronRadarClient? cronRadar = null, ILogger? logger = null)
        {
            try
            {
                _cronRadar = cronRadar ?? new CronRadarClient();
                _logger = logger;
            }
            catch
            {
                // Even construction should fail silently
                _cronRadar = new NullCronRadarClient(); // Null object pattern
            }
        }

        public CronRadarJobFilter() : this(null, null)
        {
        }

        /// <summary>
        /// Called before job execution. Syncs monitor if needed.
        /// NEVER throws exceptions.
        /// </summary>
        public void OnPerforming(PerformingContext context)
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
            catch (Exception ex)
            {
                // Log but NEVER throw - protect the application
                _logger?.LogDebug("CronRadar: Failed to sync monitor - {Message}", ex.Message);
            }
        }

        /// <summary>
        /// Called after job execution. Sends ping or failure to CronRadar.
        /// NEVER throws exceptions.
        /// </summary>
        public void OnPerformed(PerformedContext context)
        {
            try
            {
                var jobId = GetJobId(context);
                if (string.IsNullOrEmpty(jobId)) return;

                if (context.Exception == null && !context.Canceled)
                {
                    // Job succeeded - send ping
                    _cronRadar.Ping(jobId);
                    _logger?.LogDebug("CronRadar: Pinged monitor {JobId}", jobId);
                }
                else if (context.Exception != null)
                {
                    // Job failed - report failure
                    _cronRadar.Fail(jobId, context.Exception.Message);
                    _logger?.LogDebug("CronRadar: Reported failure for {JobId}: {Error}", jobId, context.Exception.Message);
                }
            }
            catch (Exception ex)
            {
                // Log but NEVER throw - protect the application
                _logger?.LogDebug("CronRadar: Failed to report job status - {Message}", ex.Message);
            }
        }

        private string GetJobId(PerformContext context)
        {
            try
            {
                // For recurring jobs, try to get from storage
                if (context.BackgroundJob?.Job != null)
                {
                    // Check if this is a recurring job by looking at the job type
                    var jobType = context.BackgroundJob.Job.Type;
                    var methodName = context.BackgroundJob.Job.Method.Name;

                    // Create a stable ID based on job signature
                    var jobSignature = $"{jobType.Name}.{methodName}";

                    // For now, use the signature as the ID
                    // In production, you might want to query the storage for the actual recurring job ID
                    return jobSignature;
                }

                // Fall back to BackgroundJob.Id
                return context.BackgroundJob?.Id ?? "";
            }
            catch
            {
                try
                {
                    // Simple fallback
                    return context.BackgroundJob?.Id ?? "";
                }
                catch
                {
                    return "";
                }
            }
        }

        private void SyncMonitorFromContext(PerformContext context, string jobId)
        {
            try
            {
                // Default cron expression - will be overridden by actual schedule if available
                var cronExpression = "0 * * * *"; // Every hour by default

                // Extract job type/method name for display
                var jobName = ExtractJobName(context) ?? jobId;

                // Sync the monitor
                var monitor = new Monitor(jobId, cronExpression, jobName);
                _cronRadar.Sync(new[] { monitor });

                _logger?.LogDebug("CronRadar: Synced monitor {JobId} with name {JobName}", jobId, jobName);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("CronRadar: Failed to sync monitor - {Message}", ex.Message);
            }
        }

        private string? ExtractJobName(PerformContext context)
        {
            try
            {
                var job = context.BackgroundJob?.Job;
                if (job != null)
                {
                    return $"{job.Type.Name}.{job.Method.Name}";
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
    /// Null object pattern for fail-safe operation
    /// </summary>
    internal class NullCronRadarClient : ICronRadarClient
    {
        // Sync methods
        public bool Sync(IEnumerable<Monitor> monitors) => true;
        public Task<bool> SyncAsync(IEnumerable<Monitor> monitors, CancellationToken cancellationToken = default) => Task.FromResult(true);

        // Ping methods
        public bool Ping(string monitorKey) => true;
        public Task<bool> PingAsync(string monitorKey, CancellationToken cancellationToken = default) => Task.FromResult(true);

        // Fail methods
        public bool Fail(string monitorKey, string? error = null) => true;
        public Task<bool> FailAsync(string monitorKey, string? error = null, CancellationToken cancellationToken = default) => Task.FromResult(true);

        // Monitor methods
        public T Monitor<T>(string monitorKey, Func<T> action) => action();
        public void Monitor(string monitorKey, Action action) => action();
        public async Task<T> MonitorAsync<T>(string monitorKey, Func<Task<T>> action, CancellationToken cancellationToken = default) => await action();
        public async Task MonitorAsync(string monitorKey, Func<Task> action, CancellationToken cancellationToken = default) => await action();

        public void Dispose() { }
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
        public static IGlobalConfiguration UseCronRadar(this IGlobalConfiguration configuration, ICronRadarClient? client = null)
        {
            try
            {
                if (configuration == null) return configuration;

                _client = client ?? new CronRadarClient();
                var filter = new CronRadarJobFilter(_client);

                // Add the filter to global filters
                GlobalJobFilters.Filters.Add(filter);

                // Sync existing recurring jobs after Hangfire initializes
                Task.Run(async () =>
                {
                    await Task.Delay(10000); // Wait for Hangfire to fully initialize
                    SyncRecurringJobs();
                });

                return configuration;
            }
            catch
            {
                // Never break the application
                return configuration;
            }
        }

        /// <summary>
        /// Syncs all registered recurring jobs with CronRadar
        /// </summary>
        public static void SyncRecurringJobs()
        {
            try
            {
                if (_client == null) return;

                var monitors = new List<Monitor>();

                // Try to get recurring jobs from Hangfire storage using reflection
                try
                {
                    // Get JobStorage.Current
                    var jobStorageType = Type.GetType("Hangfire.JobStorage, Hangfire.Core");
                    if (jobStorageType != null)
                    {
                        var currentProperty = jobStorageType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                        var storage = currentProperty?.GetValue(null);

                        if (storage != null)
                        {
                            // Get connection
                            var getConnectionMethod = storage.GetType().GetMethod("GetConnection");
                            if (getConnectionMethod != null)
                            {
                                using (var connection = getConnectionMethod.Invoke(storage, null) as IDisposable)
                                {
                                    if (connection != null)
                                    {
                                        // Try to get recurring jobs
                                        var getRecurringJobsMethod = connection.GetType().GetMethod("GetRecurringJobs");
                                        if (getRecurringJobsMethod != null)
                                        {
                                            var recurringJobs = getRecurringJobsMethod.Invoke(connection, null);
                                            if (recurringJobs != null)
                                            {
                                                // Process each job
                                                foreach (var job in (IEnumerable<dynamic>)recurringJobs)
                                                {
                                                    try
                                                    {
                                                        string id = job.Id;
                                                        string cron = job.Cron ?? "* * * * *";
                                                        string name = job.Job?.Type?.Name ?? id;

                                                        monitors.Add(new Monitor(id, cron, name));
                                                    }
                                                    catch
                                                    {
                                                        // Skip jobs we can't process
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // If reflection fails, we'll rely on jobs syncing when they run
                }

                // Sync discovered monitors
                if (monitors.Count > 0)
                {
                    _client.Sync(monitors);
                }
            }
            catch
            {
                // Silently fail - never break the application
            }
        }

        /// <summary>
        /// Gets the CronRadar client instance used by Hangfire integration.
        /// </summary>
        public static ICronRadarClient? Client => _client;
    }
}
#endif