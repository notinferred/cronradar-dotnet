using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CronRadar
{
    /// <summary>
    /// CronRadar client for monitoring scheduled jobs and cron tasks.
    /// </summary>
    public class CronRadarClient : ICronRadarClient, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<CronRadarClient>? _logger;
        private readonly string? _apiKey;
        private readonly bool _failSilently;
        private readonly JsonSerializerOptions _jsonOptions;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the CronRadar client.
        /// </summary>
        /// <param name="apiKey">API key for authentication. If null, reads from CRONRADAR_API_KEY environment variable.</param>
        /// <param name="options">Optional client configuration.</param>
        public CronRadarClient(string? apiKey = null, CronRadarOptions? options = null)
            : this(new HttpClient(), apiKey, options)
        {
        }

        /// <summary>
        /// Initializes a new instance of the CronRadar client with a custom HttpClient.
        /// </summary>
        /// <param name="httpClient">HttpClient instance to use for API calls.</param>
        /// <param name="apiKey">API key for authentication. If null, reads from CRONRADAR_API_KEY environment variable.</param>
        /// <param name="options">Optional client configuration.</param>
        public CronRadarClient(HttpClient httpClient, string? apiKey = null, CronRadarOptions? options = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

            // Try to get API key from multiple sources
            _apiKey = apiKey
                ?? options?.ApiKey
                ?? Environment.GetEnvironmentVariable("CRONRADAR_API_KEY")
                ?? Environment.GetEnvironmentVariable("CronRadar__ApiKey"); // Support .NET config pattern

            _logger = options?.Logger;
            _failSilently = options?.FailSilently ?? true;

            // Configure HttpClient with proper defaults
            var baseUrl = options?.BaseUrl
                ?? Environment.GetEnvironmentVariable("CRONRADAR_BASE_URL")
                ?? Environment.GetEnvironmentVariable("CronRadar__BaseUrl") // Support .NET config pattern
                ?? "https://cronradar.com";

            _httpClient.BaseAddress = new Uri(baseUrl);
            _httpClient.Timeout = options?.Timeout ?? TimeSpan.FromSeconds(5);

            // Set up Basic Auth if API key is available
            if (!string.IsNullOrEmpty(_apiKey))
            {
                var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_apiKey}:"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            }

            // Configure JSON options
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Synchronizes monitors with CronRadar.
        /// </summary>
        /// <param name="monitors">List of monitors to sync.</param>
        /// <returns>True if sync was successful, false otherwise.</returns>
        public bool Sync(IEnumerable<Monitor> monitors)
        {
            try
            {
                return SyncAsync(monitors).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Failed to sync monitors");
            }
        }

        /// <summary>
        /// Asynchronously synchronizes monitors with CronRadar.
        /// </summary>
        /// <param name="monitors">List of monitors to sync.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if sync was successful, false otherwise.</returns>
        public async Task<bool> SyncAsync(IEnumerable<Monitor> monitors, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!ValidateApiKey())
                    return false;

                var monitorsList = monitors?.ToList() ?? new List<Monitor>();
                if (!monitorsList.Any())
                {
                    _logger?.LogWarning("No monitors provided for sync");
                    return true; // Empty sync is considered successful
                }

                var payload = new
                {
                    monitors = monitorsList.Select(m => new
                    {
                        key = m.Key,
                        schedule = m.Schedule,
                        name = m.Name ?? m.Key,
                        grace_period = m.GracePeriod ?? 60,
                        timezone = m.Timezone ?? "UTC"
                    })
                };

                var json = JsonSerializer.Serialize(payload, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("/api/sync/monitors", content, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogInformation("Successfully synced {Count} monitors", monitorsList.Count);
                    Console.WriteLine($"CronRadar API: Successfully synced {monitorsList.Count} monitors");
                    return true;
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                _logger?.LogWarning("Monitor sync failed with status {StatusCode}: {Response}", response.StatusCode, responseBody);
                Console.WriteLine($"CronRadar API: Sync failed with status {response.StatusCode}: {responseBody}");
                return false;
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Failed to sync monitors");
            }
        }

        /// <summary>
        /// Sends a ping for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to ping.</param>
        /// <returns>True if ping was successful, false otherwise.</returns>
        public bool Ping(string monitorKey)
        {
            try
            {
                return PingAsync(monitorKey).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Failed to ping monitor '{monitorKey}'");
            }
        }

        /// <summary>
        /// Asynchronously sends a ping for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to ping.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if ping was successful, false otherwise.</returns>
        public async Task<bool> PingAsync(string monitorKey, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!ValidateApiKey())
                    return false;

                if (string.IsNullOrWhiteSpace(monitorKey))
                {
                    _logger?.LogWarning("Monitor key is required for ping");
                    return false;
                }

                var response = await _httpClient.GetAsync($"/ping/{Uri.EscapeDataString(monitorKey)}", cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Successfully pinged monitor '{MonitorKey}'", monitorKey);
                    return true;
                }

                _logger?.LogWarning("Ping failed for monitor '{MonitorKey}' with status {StatusCode}",
                    monitorKey, response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Failed to ping monitor '{monitorKey}'");
            }
        }

        /// <summary>
        /// Reports a failure for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to report failure for.</param>
        /// <param name="error">Optional error message.</param>
        /// <returns>True if failure report was successful, false otherwise.</returns>
        public bool Fail(string monitorKey, string? error = null)
        {
            try
            {
                return FailAsync(monitorKey, error).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Failed to report failure for monitor '{monitorKey}'");
            }
        }

        /// <summary>
        /// Asynchronously reports a failure for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to report failure for.</param>
        /// <param name="error">Optional error message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if failure report was successful, false otherwise.</returns>
        public async Task<bool> FailAsync(string monitorKey, string? error = null, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!ValidateApiKey())
                    return false;

                if (string.IsNullOrWhiteSpace(monitorKey))
                {
                    _logger?.LogWarning("Monitor key is required for failure report");
                    return false;
                }

                var url = $"/ping/{Uri.EscapeDataString(monitorKey)}/fail";
                if (!string.IsNullOrEmpty(error))
                {
                    url += $"?error={Uri.EscapeDataString(error)}";
                }

                var response = await _httpClient.GetAsync(url, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    _logger?.LogDebug("Successfully reported failure for monitor '{MonitorKey}'", monitorKey);
                    return true;
                }

                _logger?.LogWarning("Failed to report failure for monitor '{MonitorKey}' with status {StatusCode}",
                    monitorKey, response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Failed to report failure for monitor '{monitorKey}'");
            }
        }

        /// <summary>
        /// Monitors a job execution and pings on success.
        /// </summary>
        /// <typeparam name="T">Return type of the job.</typeparam>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The job action to execute.</param>
        /// <returns>The result of the action.</returns>
        public T Monitor<T>(string monitorKey, Func<T> action)
        {
            try
            {
                var result = action();
                Ping(monitorKey);
                return result;
            }
            catch (Exception ex)
            {
                Fail(monitorKey, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Monitors a job execution and pings on success.
        /// </summary>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The job action to execute.</param>
        public void Monitor(string monitorKey, Action action)
        {
            try
            {
                action();
                Ping(monitorKey);
            }
            catch (Exception ex)
            {
                Fail(monitorKey, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Asynchronously monitors a job execution and pings on success.
        /// </summary>
        /// <typeparam name="T">Return type of the job.</typeparam>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The async job action to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the action.</returns>
        public async Task<T> MonitorAsync<T>(string monitorKey, Func<Task<T>> action, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await action();
                await PingAsync(monitorKey, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                await FailAsync(monitorKey, ex.Message, cancellationToken);
                throw;
            }
        }

        /// <summary>
        /// Asynchronously monitors a job execution and pings on success.
        /// </summary>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The async job action to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task MonitorAsync(string monitorKey, Func<Task> action, CancellationToken cancellationToken = default)
        {
            try
            {
                await action();
                await PingAsync(monitorKey, cancellationToken);
            }
            catch (Exception ex)
            {
                await FailAsync(monitorKey, ex.Message, cancellationToken);
                throw;
            }
        }

        private bool ValidateApiKey()
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger?.LogError("CronRadar API key is not configured. Set CRONRADAR_API_KEY environment variable or provide it in the constructor.");
                return false;
            }
            return true;
        }

        private bool HandleException(Exception ex, string message)
        {
            _logger?.LogError(ex, message);

            if (!_failSilently)
                throw ex;

            return false;
        }

        /// <summary>
        /// Disposes the HTTP client resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the HTTP client resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}