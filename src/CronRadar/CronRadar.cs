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
        private static readonly HttpClient _http = new HttpClient
        {
            BaseAddress = new Uri("https://cron.life"),
            Timeout = TimeSpan.FromSeconds(5)
        };

        #region Monitor Methods

        /// <summary>
        /// Monitor a job execution by recording it in CronRadar.
        /// Optionally provide schedule for self-healing (auto-registration on 404).
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="schedule">Optional cron schedule for auto-registration if monitor doesn't exist</param>
        public static void Monitor(string monitorKey, string? schedule = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: CRONRADAR_API_KEY environment variable not set. Monitor '{monitorKey}' will not be tracked.");
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
            catch
            {
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously monitor a job execution by recording it in CronRadar.
        /// Optionally provide schedule for self-healing (auto-registration on 404).
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="schedule">Optional cron schedule for auto-registration if monitor doesn't exist</param>
        public static async Task MonitorAsync(string monitorKey, string? schedule = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: CRONRADAR_API_KEY environment variable not set. Monitor '{monitorKey}' will not be tracked.");
                    return;
                }

                // Try to ping
                var response = await SendPingInternalAsync(monitorKey, apiKey);

                // Self-healing: if monitor doesn't exist and schedule provided, create it
                if (response?.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(schedule))
                {
                    Console.WriteLine($"[CronRadar] Monitor '{monitorKey}' not found. Auto-registering with schedule '{schedule}'...");

                    var source = DetectSource();
                    await SyncMonitorAsync(monitorKey, schedule, source);

                    // Retry ping
                    await SendPingInternalAsync(monitorKey, apiKey);
                }
            }
            catch
            {
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
        public static void StartJob(string monitorKey, string? schedule = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                SendLifecyclePing(monitorKey, "start", apiKey, schedule);
            }
            catch
            {
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously signal that a job has started executing.
        /// </summary>
        public static async Task StartJobAsync(string monitorKey, string? schedule = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                await SendLifecyclePingAsync(monitorKey, "start", apiKey, schedule);
            }
            catch
            {
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Signal that a job has completed successfully.
        /// Used for lifecycle tracking to record completion time and calculate duration.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        public static void CompleteJob(string monitorKey)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                SendLifecyclePing(monitorKey, "complete", apiKey);
            }
            catch
            {
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously signal that a job has completed successfully.
        /// </summary>
        public static async Task CompleteJobAsync(string monitorKey)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                await SendLifecyclePingAsync(monitorKey, "complete", apiKey);
            }
            catch
            {
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Signal that a job has failed.
        /// Triggers immediate alerts without waiting for grace period.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="message">Optional failure message for debugging</param>
        public static void FailJob(string monitorKey, string? message = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                SendLifecyclePing(monitorKey, "fail", apiKey, message: message);
            }
            catch
            {
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously signal that a job has failed.
        /// </summary>
        public static async Task FailJobAsync(string monitorKey, string? message = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey)) return;

                await SendLifecyclePingAsync(monitorKey, "fail", apiKey, message: message);
            }
            catch
            {
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
        public static void Wrap(string monitorKey, Action action, string? schedule = null)
        {
            StartJob(monitorKey, schedule);
            try
            {
                action();
                CompleteJob(monitorKey);
            }
            catch (Exception e)
            {
                FailJob(monitorKey, e.Message);
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
        public static async Task WrapAsync(string monitorKey, Func<Task> func, string? schedule = null)
        {
            await StartJobAsync(monitorKey, schedule);
            try
            {
                await func();
                await CompleteJobAsync(monitorKey);
            }
            catch (Exception e)
            {
                await FailJobAsync(monitorKey, e.Message);
                throw; // Re-throw to preserve original behavior
            }
        }

        /// <summary>
        /// Wrap an async function that returns a value with automatic lifecycle monitoring.
        /// </summary>
        public static async Task<T> WrapAsync<T>(string monitorKey, Func<Task<T>> func, string? schedule = null)
        {
            await StartJobAsync(monitorKey, schedule);
            try
            {
                var result = await func();
                await CompleteJobAsync(monitorKey);
                return result;
            }
            catch (Exception e)
            {
                await FailJobAsync(monitorKey, e.Message);
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
        public static void SyncMonitor(string monitorKey, string schedule, string? source = null, string? name = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: CRONRADAR_API_KEY environment variable not set. Monitor '{monitorKey}' will not be synced.");
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
                            gracePeriod = 60
                        }
                    }
                };

                var json = JsonSerializer.Serialize(syncRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/sync");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");
                request.Content = content;

                var response = _http.SendAsync(request).Result;

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
                Console.WriteLine($"[CronRadar] Error syncing monitor '{monitorKey}': {ex.Message}");
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
        public static async Task SyncMonitorAsync(string monitorKey, string schedule, string? source = null, string? name = null)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: CRONRADAR_API_KEY environment variable not set. Monitor '{monitorKey}' will not be synced.");
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
                            gracePeriod = 60
                        }
                    }
                };

                var json = JsonSerializer.Serialize(syncRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/sync");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");
                request.Content = content;

                var response = await _http.SendAsync(request);

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
                Console.WriteLine($"[CronRadar] Error syncing monitor '{monitorKey}': {ex.Message}");
                // Never throw - protect user's job execution
            }
        }

        #endregion

        #region Helper Methods

        private static HttpResponseMessage? SendPingInternal(string monitorKey, string apiKey)
        {
            try
            {
                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Get, $"/ping/{Uri.EscapeDataString(monitorKey)}");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");

                return _http.SendAsync(request).Result;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<HttpResponseMessage?> SendPingInternalAsync(string monitorKey, string apiKey)
        {
            try
            {
                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Get, $"/ping/{Uri.EscapeDataString(monitorKey)}");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");

                return await _http.SendAsync(request);
            }
            catch
            {
                return null;
            }
        }

        private static void SendLifecyclePing(string monitorKey, string endpoint, string apiKey, string? schedule = null, string? message = null)
        {
            try
            {
                // Build URL
                var url = $"/ping/{Uri.EscapeDataString(monitorKey)}/{endpoint}";

                // Add query parameters
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(schedule))
                    queryParams.Add($"schedule={Uri.EscapeDataString(schedule)}");
                if (!string.IsNullOrEmpty(message))
                    queryParams.Add($"message={Uri.EscapeDataString(message)}");

                if (queryParams.Count > 0)
                    url += "?" + string.Join("&", queryParams);

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");

                _http.SendAsync(request).Wait();
            }
            catch
            {
                // Silently fail to protect job execution
            }
        }

        private static async Task SendLifecyclePingAsync(string monitorKey, string endpoint, string apiKey, string? schedule = null, string? message = null)
        {
            try
            {
                // Build URL
                var url = $"/ping/{Uri.EscapeDataString(monitorKey)}/{endpoint}";

                // Add query parameters
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(schedule))
                    queryParams.Add($"schedule={Uri.EscapeDataString(schedule)}");
                if (!string.IsNullOrEmpty(message))
                    queryParams.Add($"message={Uri.EscapeDataString(message)}");

                if (queryParams.Count > 0)
                    url += "?" + string.Join("&", queryParams);

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");

                await _http.SendAsync(request);
            }
            catch
            {
                // Silently fail to protect job execution
            }
        }

        /// <summary>
        /// Detect the calling source/framework based on assembly names and stack trace.
        /// </summary>
        private static string DetectSource()
        {
            try
            {
                // Check calling assembly first
                var callingAssembly = Assembly.GetCallingAssembly();
                var assemblyName = callingAssembly?.GetName()?.Name?.ToLowerInvariant() ?? "";

                // Check for known extension assemblies
                if (assemblyName.Contains("cronradar.hangfire")) return "hangfire";
                if (assemblyName.Contains("cronradar.laravel")) return "laravel";
                if (assemblyName.Contains("cronradar.celery")) return "celery";
                if (assemblyName.Contains("cronradar.sidekiq")) return "sidekiq";
                if (assemblyName.Contains("cronradar.bull")) return "bull";
                if (assemblyName.Contains("cronradar.quartz")) return "quartz";

                // Check stack trace for framework signatures
                var stackTrace = new StackTrace();
                foreach (var frame in stackTrace.GetFrames() ?? Array.Empty<StackFrame>())
                {
                    var typeName = frame?.GetMethod()?.DeclaringType?.FullName?.ToLowerInvariant() ?? "";

                    if (typeName.Contains("hangfire")) return "hangfire-direct";
                    if (typeName.Contains("quartz")) return "quartz-direct";
                    if (typeName.Contains("coravel")) return "coravel-direct";
                    if (typeName.Contains("ncron")) return "ncron-direct";
                }

                // Check loaded assemblies for framework presence
                var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var assembly in loadedAssemblies)
                {
                    var name = assembly.GetName()?.Name?.ToLowerInvariant() ?? "";
                    if (name.StartsWith("hangfire")) return "hangfire-env";
                    if (name.StartsWith("quartz")) return "quartz-env";
                }
            }
            catch
            {
                // If detection fails, fall back to manual
            }

            return "manual";
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