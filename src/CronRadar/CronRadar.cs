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
    /// Two main functions: Ping() for execution confirmation, SyncMonitor() for registration.
    /// </summary>
    public static class CronRadar
    {
        private static readonly HttpClient _http = new HttpClient
        {
            BaseAddress = new Uri(
                Environment.GetEnvironmentVariable("CRONRADAR_BASE_URL")
                ?? "https://cronradar.com"
            ),
            Timeout = TimeSpan.FromSeconds(5)
        };

        #region Ping Methods

        /// <summary>
        /// Ping CronRadar to indicate your job has run successfully.
        /// Optionally provide schedule for self-healing (auto-registration on 404).
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="schedule">Optional cron schedule for auto-registration if monitor doesn't exist</param>
        /// <param name="gracePeriod">Optional grace period in seconds for auto-registration</param>
        public static void Ping(string monitorKey, string? schedule = null, int? gracePeriod = null)
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
                    SyncMonitor(monitorKey, schedule, source, null, gracePeriod ?? 60);

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
        /// Asynchronously ping CronRadar to indicate your job has run successfully.
        /// Optionally provide schedule for self-healing (auto-registration on 404).
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        /// <param name="schedule">Optional cron schedule for auto-registration if monitor doesn't exist</param>
        /// <param name="gracePeriod">Optional grace period in seconds for auto-registration</param>
        public static async Task PingAsync(string monitorKey, string? schedule = null, int? gracePeriod = null)
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
                    await SyncMonitorAsync(monitorKey, schedule, source, null, gracePeriod ?? 60);

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

        #region SyncMonitor Methods

        /// <summary>
        /// Register a monitor with CronRadar, setting up expectations for when it should run.
        /// Used by extensions to sync discovered jobs.
        /// </summary>
        /// <param name="monitorKey">The unique identifier for this monitor</param>
        /// <param name="schedule">Cron expression defining when the job runs</param>
        /// <param name="source">Source framework (e.g., "hangfire", "celery"). Auto-detected if null.</param>
        /// <param name="name">Human-readable name. Generated from key if null.</param>
        /// <param name="gracePeriod">Grace period in seconds before alerting (default: 60)</param>
        public static void SyncMonitor(string monitorKey, string schedule, string? source = null, string? name = null, int gracePeriod = 60)
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
                            gracePeriod = gracePeriod
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
        /// Asynchronously register a monitor with CronRadar.
        /// </summary>
        /// <param name="monitorKey">The unique identifier for this monitor</param>
        /// <param name="schedule">Cron expression defining when the job runs</param>
        /// <param name="source">Source framework (e.g., "hangfire", "celery"). Auto-detected if null.</param>
        /// <param name="name">Human-readable name. Generated from key if null.</param>
        /// <param name="gracePeriod">Grace period in seconds before alerting (default: 60)</param>
        public static async Task SyncMonitorAsync(string monitorKey, string schedule, string? source = null, string? name = null, int gracePeriod = 60)
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
                            gracePeriod = gracePeriod
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
                var methodStr = Environment.GetEnvironmentVariable("CRONRADAR_METHOD") ?? "GET";
                var method = methodStr.ToUpperInvariant() == "POST" ? HttpMethod.Post : HttpMethod.Get;

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(method, $"/ping/{Uri.EscapeDataString(monitorKey)}");
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
                var methodStr = Environment.GetEnvironmentVariable("CRONRADAR_METHOD") ?? "GET";
                var method = methodStr.ToUpperInvariant() == "POST" ? HttpMethod.Post : HttpMethod.Get;

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(method, $"/ping/{Uri.EscapeDataString(monitorKey)}");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");

                return await _http.SendAsync(request);
            }
            catch
            {
                return null;
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