using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace CronRadar
{
    /// <summary>
    /// Dead-simple cron job monitoring with auto-registration support.
    /// Primary function: Monitor() for execution confirmation. Advanced: Sync() for pre-registration.
    /// </summary>
    public static class CronRadar
    {
        private static HttpClient? _httpClient;
        private static readonly object _httpLock = new object();
        private static string? _cachedSource = null;
        private static readonly object _sourceLock = new object();

        /// <summary>
        /// Enable debug logging to Console.Error. Set via CRONRADAR_DEBUG environment variable or this property.
        /// </summary>
        public static bool DebugLoggingEnabled { get; set; } =
            Environment.GetEnvironmentVariable(CronRadarConstants.DebugEnvironmentVariable)?.ToLowerInvariant() == "true";

        /// <summary>
        /// HTTP request timeout in seconds. Default: 5 seconds
        /// IMPORTANT: Changes to this property after first HttpClient creation will not take effect.
        /// Set this before any API calls.
        /// </summary>
        public static int TimeoutSeconds { get; set; } = CronRadarConstants.DefaultTimeoutSeconds;

        /// <summary>
        /// Default grace period in seconds for monitors. Default: 60 seconds
        /// </summary>
        public static int DefaultGracePeriodSeconds { get; set; } = CronRadarConstants.DefaultGracePeriodSeconds;

        private static HttpClient GetHttpClient()
        {
            if (_httpClient == null)
            {
                lock (_httpLock)
                {
                    if (_httpClient == null)
                    {
                        var configuredBaseUrl = Environment.GetEnvironmentVariable(CronRadarConstants.BaseUrlEnvironmentVariable);
                        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
                            ? CronRadarConstants.BaseUrl
                            : configuredBaseUrl.Trim();

                        _httpClient = new HttpClient
                        {
                            BaseAddress = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/"),
                            Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
                        };
                    }
                }
            }
            return _httpClient;
        }

        private static void LogDebug(string message)
        {
            if (DebugLoggingEnabled)
            {
                Console.Error.WriteLine($"[CronRadar Debug] {message}");
            }
        }

        private static void LogError(string message, Exception? ex = null)
        {
            if (DebugLoggingEnabled)
            {
                Console.Error.WriteLine($"[CronRadar Error] {message}");
                if (ex != null)
                {
                    Console.Error.WriteLine($"[CronRadar Error] Exception: {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine($"[CronRadar Error] StackTrace: {ex.StackTrace}");
                }
            }
        }

        private static bool ValidateMonitorKey(string monitorKey)
        {
            if (string.IsNullOrWhiteSpace(monitorKey))
            {
                LogError("Monitor key cannot be null or empty");
                return false;
            }
            if (monitorKey.Length > CronRadarConstants.MaxMonitorKeyLength)
            {
                LogError($"Monitor key '{monitorKey}' exceeds maximum length of {CronRadarConstants.MaxMonitorKeyLength} characters");
                return false;
            }
            return true;
        }

        private static bool ValidateSchedule(string? schedule)
        {
            if (string.IsNullOrWhiteSpace(schedule))
            {
                return true; // Schedule is optional for many operations
            }

            // Basic cron validation: should have 5 or 6 space-separated fields
            var parts = schedule.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5 || parts.Length > 6)
            {
                LogError($"Invalid cron schedule '{schedule}': Expected 5 or 6 fields, got {parts.Length}");
                return false;
            }

            return true;
        }

        #region Monitor Methods

        /// <summary>
        /// Monitor a job execution by recording it in CronRadar.
        /// Optionally provide schedule for self-healing (auto-registration on 404).
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="schedule">Optional cron schedule for auto-registration if monitor doesn't exist</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static void Monitor(string monitorKey, string? schedule = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;
                if (!ValidateSchedule(schedule)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: {CronRadarConstants.ApiKeyEnvironmentVariable} environment variable not set. Monitor '{monitorKey}' will not be tracked.");
                    return;
                }

                // Try to ping
                var response = SendPingInternal(monitorKey, apiKey);

                // Self-healing: if monitor doesn't exist and schedule provided, create it
                if (response?.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(schedule))
                {
                    Console.WriteLine($"[CronRadar] Monitor '{monitorKey}' not found. Auto-registering with schedule '{schedule}'...");

                    var source = DetectSource();
                    SyncMonitor(monitorKey, schedule, source);

                    // Retry ping
                    SendPingInternal(monitorKey, apiKey);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to monitor '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously monitor a job execution by recording it in CronRadar.
        /// Optionally provide schedule for self-healing (auto-registration on 404).
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="schedule">Optional cron schedule for auto-registration if monitor doesn't exist</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static async Task MonitorAsync(string monitorKey, string? schedule = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;
                if (!ValidateSchedule(schedule)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: {CronRadarConstants.ApiKeyEnvironmentVariable} environment variable not set. Monitor '{monitorKey}' will not be tracked.");
                    return;
                }

                // Try to ping
                var response = await SendPingInternalAsync(monitorKey, apiKey).ConfigureAwait(false);

                // Self-healing: if monitor doesn't exist and schedule provided, create it
                if (response?.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(schedule))
                {
                    Console.WriteLine($"[CronRadar] Monitor '{monitorKey}' not found. Auto-registering with schedule '{schedule}'...");

                    var source = DetectSource();
                    await SyncMonitorAsync(monitorKey, schedule, source).ConfigureAwait(false);

                    // Retry ping
                    await SendPingInternalAsync(monitorKey, apiKey).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to monitor '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        #endregion

        #region Lifecycle Methods

        /// <summary>
        /// Signal that a job has started executing.
        /// Used for lifecycle tracking to detect hung jobs and measure execution duration.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="schedule">Optional cron schedule for auto-registration</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static void StartJob(string monitorKey, string? schedule = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                SendLifecyclePing(monitorKey, "start", apiKey, schedule);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send start signal for '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously signal that a job has started executing.
        /// </summary>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static async Task StartJobAsync(string monitorKey, string? schedule = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                await SendLifecyclePingAsync(monitorKey, "start", apiKey, schedule).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send start signal for '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Signal that a job has completed successfully.
        /// Used for lifecycle tracking to record completion time and calculate duration.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static void CompleteJob(string monitorKey, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                SendLifecyclePing(monitorKey, "complete", apiKey);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send complete signal for '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously signal that a job has completed successfully.
        /// </summary>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static async Task CompleteJobAsync(string monitorKey, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                await SendLifecyclePingAsync(monitorKey, "complete", apiKey).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send complete signal for '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Signal that a job has failed.
        /// Triggers immediate alerts without waiting for grace period.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="message">Optional failure message for debugging</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static void FailJob(string monitorKey, string? message = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                SendLifecyclePing(monitorKey, "fail", apiKey, message: message);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send fail signal for '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously signal that a job has failed.
        /// </summary>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static async Task FailJobAsync(string monitorKey, string? message = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                await SendLifecyclePingAsync(monitorKey, "fail", apiKey, message: message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send fail signal for '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Wrap an action with automatic lifecycle monitoring.
        /// Handles start/complete/fail signals automatically.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="action">The action to wrap</param>
        /// <param name="schedule">Optional cron schedule for auto-registration</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static void Wrap(string monitorKey, Action action, string? schedule = null, string? apiKey = null)
        {
            StartJob(monitorKey, schedule, apiKey);
            try
            {
                action();
                CompleteJob(monitorKey, apiKey);
            }
            catch (Exception e)
            {
                FailJob(monitorKey, e.Message, apiKey);
                throw; // Re-throw to preserve original behavior
            }
        }

        /// <summary>
        /// Wrap an async function with automatic lifecycle monitoring.
        /// Handles start/complete/fail signals automatically.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="func">The async function to wrap</param>
        /// <param name="schedule">Optional cron schedule for auto-registration</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static async Task WrapAsync(string monitorKey, Func<Task> func, string? schedule = null, string? apiKey = null)
        {
            await StartJobAsync(monitorKey, schedule, apiKey).ConfigureAwait(false);
            try
            {
                await func().ConfigureAwait(false);
                await CompleteJobAsync(monitorKey, apiKey).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await FailJobAsync(monitorKey, e.Message, apiKey).ConfigureAwait(false);
                throw; // Re-throw to preserve original behavior
            }
        }

        /// <summary>
        /// Wrap an async function that returns a value with automatic lifecycle monitoring.
        /// </summary>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static async Task<T> WrapAsync<T>(string monitorKey, Func<Task<T>> func, string? schedule = null, string? apiKey = null)
        {
            await StartJobAsync(monitorKey, schedule, apiKey).ConfigureAwait(false);
            try
            {
                var result = await func().ConfigureAwait(false);
                await CompleteJobAsync(monitorKey, apiKey).ConfigureAwait(false);
                return result;
            }
            catch (Exception e)
            {
                await FailJobAsync(monitorKey, e.Message, apiKey).ConfigureAwait(false);
                throw; // Re-throw to preserve original behavior
            }
        }

        #endregion

        #region Sync Methods

        /// <summary>
        /// Pre-register a monitor with CronRadar, setting up expectations for when it should run.
        /// Used internally by extensions to sync discovered jobs. Advanced usage only.
        /// </summary>
        /// <param name="monitorKey">The unique identifier for this monitor</param>
        /// <param name="schedule">Cron expression defining when the job runs</param>
        /// <param name="source">Source framework (e.g., "hangfire", "celery"). Auto-detected if null.</param>
        /// <param name="name">Human-readable name. Generated from key if null.</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static void SyncMonitor(string monitorKey, string schedule, string? source = null, string? name = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;
                if (!ValidateSchedule(schedule))
                {
                    LogError("Schedule validation failed");
                    return;
                }
                if (string.IsNullOrWhiteSpace(schedule))
                {
                    LogError("Schedule cannot be null or empty");
                    return;
                }

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: {CronRadarConstants.ApiKeyEnvironmentVariable} environment variable not set. Monitor '{monitorKey}' will not be synced.");
                    return;
                }

                source ??= DetectSource();
                name ??= GenerateReadableName(monitorKey);

                var syncRequest = new
                {
                    source = source,
                    monitors = new[]
                    {
                        new
                        {
                            key = monitorKey,
                            name = name,
                            schedule = schedule,
                            gracePeriod = DefaultGracePeriodSeconds
                        }
                    }
                };

                var json = JsonSerializer.Serialize(syncRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/sync");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");
                request.Content = content;

                var response = GetHttpClient().SendAsync(request).GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[CronRadar] Monitor '{monitorKey}' synced successfully.");
                }
                else
                {
                    Console.WriteLine($"[CronRadar] Failed to sync monitor '{monitorKey}': {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error syncing monitor '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously pre-register a monitor with CronRadar.
        /// Used internally by extensions to sync discovered jobs. Advanced usage only.
        /// </summary>
        /// <param name="monitorKey">The unique identifier for this monitor</param>
        /// <param name="schedule">Cron expression defining when the job runs</param>
        /// <param name="source">Source framework (e.g., "hangfire", "celery"). Auto-detected if null.</param>
        /// <param name="name">Human-readable name. Generated from key if null.</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static async Task SyncMonitorAsync(string monitorKey, string schedule, string? source = null, string? name = null, string? apiKey = null)
        {
            try
            {
                if (!ValidateMonitorKey(monitorKey)) return;
                if (!ValidateSchedule(schedule))
                {
                    LogError("Schedule validation failed");
                    return;
                }
                if (string.IsNullOrWhiteSpace(schedule))
                {
                    LogError("Schedule cannot be null or empty");
                    return;
                }

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: {CronRadarConstants.ApiKeyEnvironmentVariable} environment variable not set. Monitor '{monitorKey}' will not be synced.");
                    return;
                }

                source ??= DetectSource();
                name ??= GenerateReadableName(monitorKey);

                var syncRequest = new
                {
                    source = source,
                    monitors = new[]
                    {
                        new
                        {
                            key = monitorKey,
                            name = name,
                            schedule = schedule,
                            gracePeriod = DefaultGracePeriodSeconds
                        }
                    }
                };

                var json = JsonSerializer.Serialize(syncRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/sync");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");
                request.Content = content;

                var response = await GetHttpClient().SendAsync(request).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[CronRadar] Monitor '{monitorKey}' synced successfully.");
                }
                else
                {
                    Console.WriteLine($"[CronRadar] Failed to sync monitor '{monitorKey}': {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                LogError($"Error syncing monitor '{monitorKey}'", ex);
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Reconcile the complete set of monitors for a source in a single request.
        /// Extensions call this on startup and periodically to declare every job they
        /// discover (the authoritative set), so dead-man coverage exists before a job
        /// ever runs. Invalid entries are skipped, not fatal to the batch.
        /// </summary>
        /// <param name="monitors">The complete current set of discovered monitors for this source.</param>
        /// <param name="source">Source framework (e.g., "hangfire", "quartz"). Auto-detected if null.</param>
        /// <param name="version">Extension version, reported to the server. Defaults to the base SDK version.</param>
        /// <param name="apiKey">Optional API key. Falls back to CRONRADAR_API_KEY environment variable if not provided.</param>
        public static void SyncMonitors(IEnumerable<MonitorDefinition> monitors, string? source = null, string? version = null, string? apiKey = null)
            => SyncMonitorsAsync(monitors, source, version, apiKey).GetAwaiter().GetResult();

        /// <summary>
        /// Asynchronously reconcile the complete set of monitors for a source (see <see cref="SyncMonitors"/>).
        /// </summary>
        public static async Task SyncMonitorsAsync(IEnumerable<MonitorDefinition> monitors, string? source = null, string? version = null, string? apiKey = null)
        {
            try
            {
                if (monitors == null) return;

                var declared = monitors.ToList();
                var valid = declared
                    .Where(m => m != null
                                && ValidateMonitorKey(m.Key)
                                && !string.IsNullOrWhiteSpace(m.Schedule)
                                && ValidateSchedule(m.Schedule))
                    .ToList();

                // A reconcile request is authoritative. Never turn a partially
                // valid discovery result into accidental removals on the server.
                if (valid.Count != declared.Count)
                {
                    Console.Error.WriteLine($"[CronRadar] Reconciliation skipped: {declared.Count - valid.Count} discovered monitor(s) had an invalid key or five-field cron schedule. Fix the reported Hangfire recurring job configuration; no existing monitors were removed.");
                    return;
                }

                apiKey ??= Environment.GetEnvironmentVariable(CronRadarConstants.ApiKeyEnvironmentVariable) ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: {CronRadarConstants.ApiKeyEnvironmentVariable} environment variable not set. {valid.Count} monitor(s) will not be synced.");
                    return;
                }

                source ??= DetectSource();

                var syncRequest = new
                {
                    source = source,
                    version = version ?? CronRadarConstants.SdkVersion,
                    mode = "reconcile",
                    monitors = valid.Select(m => new
                    {
                        key = m.Key,
                        name = m.Name ?? GenerateReadableName(m.Key),
                        schedule = m.Schedule,
                        gracePeriod = m.GracePeriod ?? DefaultGracePeriodSeconds
                    }).ToArray()
                };

                var json = JsonSerializer.Serialize(syncRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/sync");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");
                request.Content = content;

                var response = await GetHttpClient().SendAsync(request).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[CronRadar] Reconciled {valid.Count} monitor(s) for source '{source}'.");
                }
                else
                {
                    var detail = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var guidance = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                        ? "Confirm CRONRADAR_API_KEY is active and belongs to this application and deployment environment."
                        : (int)response.StatusCode == 429
                            ? "Keep the default reconciliation interval and retry after the server-provided delay."
                            : "Hangfire continues normally; the next reconciliation will retry.";
                    Console.Error.WriteLine($"[CronRadar] Reconciliation was rejected ({(int)response.StatusCode} {response.StatusCode}). {guidance} {Bound(detail, 300)}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CronRadar] Reconciliation could not reach CronRadar within the configured {TimeoutSeconds}-second timeout. The application continues normally and the next reconciliation will retry. {ex.GetType().Name}: {ex.Message}");
                LogError("Error reconciling monitors", ex);
                // Never throw - protect user's application
            }
        }

        #endregion

        #region Helper Methods

        private static string Bound(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
        }

        private static HttpResponseMessage? SendPingInternal(string monitorKey, string apiKey)
        {
            try
            {
                // URL-based auth: /ping/{monitorKey}/{apiKey}
                var url = $"/ping/{Uri.EscapeDataString(monitorKey)}/{Uri.EscapeDataString(apiKey)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                return GetHttpClient().SendAsync(request).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"Failed to send ping for '{monitorKey}'", ex);
                return null;
            }
        }

        private static async Task<HttpResponseMessage?> SendPingInternalAsync(string monitorKey, string apiKey)
        {
            try
            {
                // URL-based auth: /ping/{monitorKey}/{apiKey}
                var url = $"/ping/{Uri.EscapeDataString(monitorKey)}/{Uri.EscapeDataString(apiKey)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                return await GetHttpClient().SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send ping for '{monitorKey}'", ex);
                return null;
            }
        }

        private static void SendLifecyclePing(string monitorKey, string endpoint, string apiKey, string? schedule = null, string? message = null)
        {
            try
            {
                var response = SendLifecyclePingInternal(monitorKey, endpoint, apiKey, schedule, message);

                // Self-healing: if monitor doesn't exist and schedule provided, create it
                if (response?.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(schedule))
                {
                    Console.WriteLine($"[CronRadar] Monitor '{monitorKey}' not found. Auto-registering...");
                    var source = DetectSource();
                    SyncMonitor(monitorKey, schedule, source, apiKey: apiKey);

                    // Retry the lifecycle ping
                    SendLifecyclePingInternal(monitorKey, endpoint, apiKey, schedule, message);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to send {endpoint} ping for '{monitorKey}'", ex);
            }
        }

        private static HttpResponseMessage? SendLifecyclePingInternal(string monitorKey, string endpoint, string apiKey, string? schedule = null, string? message = null)
        {
            try
            {
                // URL-based auth: /ping/{monitorKey}/{apiKey}/{endpoint}
                var url = $"/ping/{Uri.EscapeDataString(monitorKey)}/{Uri.EscapeDataString(apiKey)}/{endpoint}";

                // Add query parameters
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(schedule))
                    queryParams.Add($"schedule={Uri.EscapeDataString(schedule)}");
                if (!string.IsNullOrEmpty(message))
                    queryParams.Add($"message={Uri.EscapeDataString(message)}");

                if (queryParams.Count > 0)
                    url += "?" + string.Join("&", queryParams);

                var request = new HttpRequestMessage(HttpMethod.Post, url);

                return GetHttpClient().SendAsync(request).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogError($"Failed to send {endpoint} ping for '{monitorKey}'", ex);
                return null;
            }
        }

        private static async Task SendLifecyclePingAsync(string monitorKey, string endpoint, string apiKey, string? schedule = null, string? message = null)
        {
            try
            {
                var response = await SendLifecyclePingInternalAsync(monitorKey, endpoint, apiKey, schedule, message).ConfigureAwait(false);

                // Self-healing: if monitor doesn't exist and schedule provided, create it
                if (response?.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(schedule))
                {
                    Console.WriteLine($"[CronRadar] Monitor '{monitorKey}' not found. Auto-registering...");
                    var source = DetectSource();
                    await SyncMonitorAsync(monitorKey, schedule, source, apiKey: apiKey).ConfigureAwait(false);

                    // Retry the lifecycle ping
                    await SendLifecyclePingInternalAsync(monitorKey, endpoint, apiKey, schedule, message).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to send {endpoint} ping for '{monitorKey}'", ex);
            }
        }

        private static async Task<HttpResponseMessage?> SendLifecyclePingInternalAsync(string monitorKey, string endpoint, string apiKey, string? schedule = null, string? message = null)
        {
            try
            {
                // URL-based auth: /ping/{monitorKey}/{apiKey}/{endpoint}
                var url = $"/ping/{Uri.EscapeDataString(monitorKey)}/{Uri.EscapeDataString(apiKey)}/{endpoint}";

                // Add query parameters
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(schedule))
                    queryParams.Add($"schedule={Uri.EscapeDataString(schedule)}");
                if (!string.IsNullOrEmpty(message))
                    queryParams.Add($"message={Uri.EscapeDataString(message)}");

                if (queryParams.Count > 0)
                    url += "?" + string.Join("&", queryParams);

                var request = new HttpRequestMessage(HttpMethod.Post, url);

                return await GetHttpClient().SendAsync(request).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogError($"Failed to send {endpoint} ping for '{monitorKey}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Detect the calling source/framework based on assembly names and stack trace.
        /// Cached after first detection to avoid reflection overhead.
        /// </summary>
        private static string DetectSource()
        {
            // Return cached value if available
            if (_cachedSource != null)
            {
                return _cachedSource;
            }

            lock (_sourceLock)
            {
                // Double-check after acquiring lock
                if (_cachedSource != null)
                {
                    return _cachedSource;
                }

                try
                {
                // Check calling assembly first
                var callingAssembly = Assembly.GetCallingAssembly();
                var assemblyName = callingAssembly?.GetName()?.Name?.ToLowerInvariant() ?? "";

                // Check for known extension assemblies
                if (assemblyName.Contains("cronradar.hangfire")) { _cachedSource = "hangfire"; return _cachedSource; }
                if (assemblyName.Contains("cronradar.laravel")) { _cachedSource = "laravel"; return _cachedSource; }
                if (assemblyName.Contains("cronradar.celery")) { _cachedSource = "celery"; return _cachedSource; }
                if (assemblyName.Contains("cronradar.sidekiq")) { _cachedSource = "sidekiq"; return _cachedSource; }
                if (assemblyName.Contains("cronradar.bull")) { _cachedSource = "bull"; return _cachedSource; }
                if (assemblyName.Contains("cronradar.quartz")) { _cachedSource = "quartz"; return _cachedSource; }

                // Check stack trace for framework signatures
                var stackTrace = new StackTrace();
                foreach (var frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
                {
                    var typeName = frame?.GetMethod()?.DeclaringType?.FullName?.ToLowerInvariant() ?? "";

                    if (typeName.Contains("hangfire")) { _cachedSource = "hangfire-direct"; return _cachedSource; }
                    if (typeName.Contains("quartz")) { _cachedSource = "quartz-direct"; return _cachedSource; }
                    if (typeName.Contains("coravel")) { _cachedSource = "coravel-direct"; return _cachedSource; }
                    if (typeName.Contains("ncron")) { _cachedSource = "ncron-direct"; return _cachedSource; }
                }

                // Check loaded assemblies for framework presence
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in loadedAssemblies)
                {
                    var name = assembly.GetName()?.Name?.ToLowerInvariant() ?? "";
                    if (name.StartsWith("hangfire")) { _cachedSource = "hangfire-env"; return _cachedSource; }
                    if (name.StartsWith("quartz")) { _cachedSource = "quartz-env"; return _cachedSource; }
                }
                }
                catch (Exception ex)
                {
                    LogError("Failed to detect source framework", ex);
                    // If detection fails, fall back to manual
                }

                // Cache and return default
                _cachedSource = "manual";
                return _cachedSource;
            }
        }

        /// <summary>
        /// Generate a human-readable name from a monitor key.
        /// Converts kebab-case to Title Case.
        /// </summary>
        private static string GenerateReadableName(string monitorKey)
        {
            if (string.IsNullOrEmpty(monitorKey))
                return monitorKey;

            // Handle kebab-case: "check-overdue-pings" -> "Check Overdue Pings"
            if (monitorKey.Contains('-'))
            {
                return string.Join(" ", monitorKey.Split('-')
                    .Select(word => word.Length > 0
                        ? char.ToUpper(word[0]) + word.Substring(1).ToLower()
                        : word));
            }

            // Handle snake_case: "check_overdue_pings" -> "Check Overdue Pings"
            if (monitorKey.Contains('_'))
            {
                return string.Join(" ", monitorKey.Split('_')
                    .Select(word => word.Length > 0
                        ? char.ToUpper(word[0]) + word.Substring(1).ToLower()
                        : word));
            }

            // Handle PascalCase: "CheckOverduePings" -> "Check Overdue Pings"
            if (char.IsUpper(monitorKey[0]))
            {
                var result = new System.Text.StringBuilder();
                for (int i = 0; i < monitorKey.Length; i++)
                {
                    if (i > 0 && char.IsUpper(monitorKey[i]) &&
                        (i + 1 < monitorKey.Length && char.IsLower(monitorKey[i + 1])))
                    {
                        result.Append(' ');
                    }
                    result.Append(monitorKey[i]);
                }
                return result.ToString();
            }

            // Default: just capitalize first letter
            return char.ToUpper(monitorKey[0]) + monitorKey.Substring(1);
        }

        #endregion
    }
}
