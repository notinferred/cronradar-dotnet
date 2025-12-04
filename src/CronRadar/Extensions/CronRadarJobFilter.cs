#if NETSTANDARD2_0_OR_GREATER || NET6_0_OR_GREATER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using Hangfire;
using Hangfire.Server;
using Hangfire.Common;
using Hangfire.States;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

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
                // CRITICAL: For recurring jobs, we MUST use the exact recurring job ID
                // that was used when the job was registered (e.g., "check-overdue-pings")
                // This MUST match what we sync to CronRadar!

                // Check if this is a recurring job by looking for RecurringJobId in context
                var recurringJobId = context.GetJobParameter<string>("RecurringJobId");
                if (!string.IsNullOrEmpty(recurringJobId))
                {
                    return recurringJobId;
                }

                // Fallback for non-recurring jobs
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
        private static System.Threading.Timer? _syncTimer;
        private static IGlobalConfiguration? _configuration;

        /// <summary>
        /// Enables CronRadar monitoring for all Hangfire jobs.
        /// This is the ONLY method you need to call.
        /// </summary>
        public static IGlobalConfiguration UseCronRadar(this IGlobalConfiguration configuration, ICronRadarClient? client = null)
        {
            try
            {
                if (configuration == null) return configuration;

                // Store configuration for later use
                _configuration = configuration;

                // Initialize client (auto-discovers API key from env/config)
                _client = client ?? new CronRadarClient();

                // Add job filter for runtime monitoring
                GlobalJobFilters.Filters.Add(new CronRadarJobFilter(_client));

                // Start multiple sync attempts with increasing delays
                Task.Run(async () =>
                {
                    // Try multiple times with increasing delays to ensure JobStorage is ready
                    var delays = new[] { 1000, 3000, 5000, 10000, 30000 }; // 1s, 3s, 5s, 10s, 30s

                    foreach (var delay in delays)
                    {
                        await Task.Delay(delay);

                        if (TrySyncAllRecurringJobs())
                        {
                            break;
                        }
                    }
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
        /// Tries to sync all recurring jobs with CronRadar
        /// </summary>
        private static bool TrySyncAllRecurringJobs()
        {
            if (_client == null || JobStorage.Current == null) return false;

            try
            {
                var monitors = new List<Monitor>();

                using (var connection = JobStorage.Current.GetConnection())
                {
                    // Get all recurring job IDs
                    var recurringJobIds = connection.GetAllItemsFromSet("recurring-jobs");

                    foreach (var jobId in recurringJobIds ?? new HashSet<string>())
                    {
                        if (!string.IsNullOrEmpty(jobId))
                        {
                            var jobData = connection.GetAllEntriesFromHash($"recurring-job:{jobId}");
                            if (jobData != null && jobData.ContainsKey("Cron"))
                            {
                                monitors.Add(new Monitor(
                                    jobId,
                                    jobData["Cron"] ?? "* * * * *",
                                    jobData.ContainsKey("Job") ? "Job" : jobId
                                ));
                            }
                        }
                    }
                }

                if (monitors.Count > 0)
                {
                    Console.WriteLine($"CronRadar: Attempting to sync {monitors.Count} monitors:");
                    foreach (var monitor in monitors)
                    {
                        Console.WriteLine($"  - Key: {monitor.Key}, Cron: {monitor.Schedule}, Name: {monitor.Name}");
                    }

                    _client.Sync(monitors);
                    Console.WriteLine("CronRadar: Sync call completed");
                    return true;
                }
                else
                {
                    Console.WriteLine("CronRadar: No monitors found to sync");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CronRadar: Failed to sync monitors - {ex.Message}");
                return false;
            }
        }

    }
}
#endif