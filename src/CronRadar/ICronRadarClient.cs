using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CronRadar
{
    /// <summary>
    /// Interface for CronRadar client operations.
    /// </summary>
    public interface ICronRadarClient : IDisposable
    {
        /// <summary>
        /// Synchronizes monitors with CronRadar.
        /// </summary>
        /// <param name="monitors">List of monitors to sync.</param>
        /// <returns>True if sync was successful, false otherwise.</returns>
        bool Sync(IEnumerable<Monitor> monitors);

        /// <summary>
        /// Asynchronously synchronizes monitors with CronRadar.
        /// </summary>
        /// <param name="monitors">List of monitors to sync.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if sync was successful, false otherwise.</returns>
        Task<bool> SyncAsync(IEnumerable<Monitor> monitors, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends a ping for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to ping.</param>
        /// <returns>True if ping was successful, false otherwise.</returns>
        bool Ping(string monitorKey);

        /// <summary>
        /// Asynchronously sends a ping for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to ping.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if ping was successful, false otherwise.</returns>
        Task<bool> PingAsync(string monitorKey, CancellationToken cancellationToken = default);

        /// <summary>
        /// Reports a failure for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to report failure for.</param>
        /// <param name="error">Optional error message.</param>
        /// <returns>True if failure report was successful, false otherwise.</returns>
        bool Fail(string monitorKey, string? error = null);

        /// <summary>
        /// Asynchronously reports a failure for a monitor.
        /// </summary>
        /// <param name="monitorKey">The monitor key to report failure for.</param>
        /// <param name="error">Optional error message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if failure report was successful, false otherwise.</returns>
        Task<bool> FailAsync(string monitorKey, string? error = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Monitors a job execution and pings on success.
        /// </summary>
        /// <typeparam name="T">Return type of the job.</typeparam>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The job action to execute.</param>
        /// <returns>The result of the action.</returns>
        T Monitor<T>(string monitorKey, Func<T> action);

        /// <summary>
        /// Monitors a job execution and pings on success.
        /// </summary>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The job action to execute.</param>
        void Monitor(string monitorKey, Action action);

        /// <summary>
        /// Asynchronously monitors a job execution and pings on success.
        /// </summary>
        /// <typeparam name="T">Return type of the job.</typeparam>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The async job action to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the action.</returns>
        Task<T> MonitorAsync<T>(string monitorKey, Func<Task<T>> action, CancellationToken cancellationToken = default);

        /// <summary>
        /// Asynchronously monitors a job execution and pings on success.
        /// </summary>
        /// <param name="monitorKey">The monitor key.</param>
        /// <param name="action">The async job action to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task MonitorAsync(string monitorKey, Func<Task> action, CancellationToken cancellationToken = default);
    }
}