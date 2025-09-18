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
                if (string.IsNullOrEmpty(jobId))
                {
                    _logger?.LogWarning("CronRadar: Could not determine job ID for ping");
                    return;
                }

                // Report job result to CronRadar
                if (context.Exception == null && !context.Canceled)
                {
                    var success = _client.Ping(jobId);
                    if (success)
                    {
                        _logger?.LogInformation("CronRadar: Successfully pinged monitor '{JobId}'", jobId);
                    }
                    else
                    {
                        _logger?.LogWarning("CronRadar: Failed to ping monitor '{JobId}' - check if monitor exists with this exact key", jobId);
                    }
                }
                else if (context.Exception != null)
                {
                    var success = _client.Fail(jobId, context.Exception.Message);
                    _logger?.LogInformation("CronRadar: Reported failure for monitor '{JobId}': {Success}", jobId, success ? "success" : "failed");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CronRadar: Error in OnPerformed");
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

                // Try multiple methods to get the recurring job ID

                // Method 1: Check if RecurringJobId is in Items (some Hangfire versions)
                if (context.Items.TryGetValue("RecurringJobId", out var itemValue) &&
                    itemValue is string itemId && !string.IsNullOrEmpty(itemId))
                {
                    return itemId;
                }

                // Method 2: Try to get from storage using the background job ID
                if (context.BackgroundJob?.Id != null)
                {
                    try
                    {
                        using (var connection = context.Storage.GetConnection())
                        {
                            // Check if this job was triggered by a recurring job
                            var jobData = connection.GetJobData(context.BackgroundJob.Id);
                            if (jobData?.Job?.Args?.Count > 0)
                            {
                                // Look for RecurringJobId in job parameters
                                var parameters = connection.GetJobParameter(context.BackgroundJob.Id, "RecurringJobId");
                                if (!string.IsNullOrEmpty(parameters))
                                {
                                    return parameters;
                                }
                            }

                            // Try to find parent recurring job by checking all recurring jobs
                            var recurringJobs = connection.GetAllItemsFromSet("recurring-jobs");
                            foreach (var recurringJobId in recurringJobs ?? new HashSet<string>())
                            {
                                // Check if this background job was created from this recurring job
                                var recurringJobData = connection.GetAllEntriesFromHash($"recurring-job:{recurringJobId}");
                                if (recurringJobData != null && recurringJobData.ContainsKey("LastJobId"))
                                {
                                    if (recurringJobData["LastJobId"] == context.BackgroundJob.Id)
                                    {
                                        return recurringJobId;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug("Failed to get recurring job ID from storage: {Error}", ex.Message);
                    }
                }

                // Method 3: Check job parameter (legacy)
                var paramJobId = context.GetJobParameter<string>("RecurringJobId");
                if (!string.IsNullOrEmpty(paramJobId))
                {
                    return paramJobId;
                }

                // Fallback for non-recurring jobs
                return context.BackgroundJob?.Id ?? "";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error getting job ID for CronRadar ping");
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

                    var result = _client.Sync(monitors);
                    if (result)
                    {
                        Console.WriteLine("CronRadar: ✅ Sync successful - monitors registered with CronRadar");
                    }
                    else
                    {
                        Console.WriteLine("CronRadar: ⚠️ Sync may have failed - check API key and connection");
                    }
                    return result;
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