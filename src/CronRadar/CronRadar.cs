using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CronRadar
{
    /// <summary>
    /// Dead-simple cron job monitoring. One function: Ping()
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

        /// <summary>
        /// Ping CronRadar to indicate your job has run successfully.
        /// Uses GET by default (better for cron), but can use POST via CRONRADAR_METHOD env var.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        public static void Ping(string monitorKey)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: CRONRADAR_API_KEY environment variable not set. Monitor '{monitorKey}' will not be tracked.");
                    return; // Continue without breaking
                }

                // Allow method override via env var, default to GET for simplicity
                var methodStr = Environment.GetEnvironmentVariable("CRONRADAR_METHOD") ?? "GET";
                var method = methodStr.ToUpperInvariant() == "POST" ? HttpMethod.Post : HttpMethod.Get;

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(method, $"/ping/{Uri.EscapeDataString(monitorKey)}");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");
                _http.SendAsync(request).Wait(5000);
            }
            catch
            {
                // Never throw - protect user's job execution
            }
        }

        /// <summary>
        /// Asynchronously ping CronRadar to indicate your job has run successfully.
        /// Uses GET by default (better for cron), but can use POST via CRONRADAR_METHOD env var.
        /// </summary>
        /// <param name="monitorKey">The monitor key identifying your job</param>
        public static async Task PingAsync(string monitorKey)
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine($"[CronRadar] Warning: CRONRADAR_API_KEY environment variable not set. Monitor '{monitorKey}' will not be tracked.");
                    return; // Continue without breaking
                }

                // Allow method override via env var, default to GET for simplicity
                var methodStr = Environment.GetEnvironmentVariable("CRONRADAR_METHOD") ?? "GET";
                var method = methodStr.ToUpperInvariant() == "POST" ? HttpMethod.Post : HttpMethod.Get;

                var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:"));
                var request = new HttpRequestMessage(method, $"/ping/{Uri.EscapeDataString(monitorKey)}");
                request.Headers.TryAddWithoutValidation("Authorization", $"Basic {auth}");
                await _http.SendAsync(request).ConfigureAwait(false);
            }
            catch
            {
                // Never throw - protect user's job execution
            }
        }
    }
}