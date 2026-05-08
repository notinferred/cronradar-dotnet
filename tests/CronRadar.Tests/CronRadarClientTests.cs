using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Xunit;
using CronRadarClient = CronRadar.CronRadar;

namespace CronRadar.Tests;

/// <summary>
/// Tests for the main CronRadar client behavior.
/// Note: The CronRadar class uses a static HttpClient, so some tests verify
/// expected behavior patterns rather than making actual HTTP calls.
/// </summary>
public class CronRadarClientTests
{
    [Fact]
    public void DefaultTimeout_Is5Seconds()
    {
        // The default timeout should be 5 seconds per specification
        Assert.Equal(5, CronRadarClient.TimeoutSeconds);
    }

    [Fact]
    public void DefaultGracePeriod_Is60Seconds()
    {
        // The default grace period should be 60 seconds per specification
        Assert.Equal(60, CronRadarClient.DefaultGracePeriodSeconds);
    }

    [Fact]
    public void Monitor_WithNullKey_DoesNotThrow()
    {
        // Silent error handling: never throw, protect user's job execution
        var exception = Record.Exception(() => CronRadarClient.Monitor(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_WithEmptyKey_DoesNotThrow()
    {
        // Silent error handling: never throw, protect user's job execution
        var exception = Record.Exception(() => CronRadarClient.Monitor(""));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_WithWhitespaceKey_DoesNotThrow()
    {
        // Silent error handling: never throw, protect user's job execution
        var exception = Record.Exception(() => CronRadarClient.Monitor("   "));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_WithTooLongKey_DoesNotThrow()
    {
        // Key validation: keys over 200 chars should be rejected silently
        var longKey = new string('a', 201);
        var exception = Record.Exception(() => CronRadarClient.Monitor(longKey));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_WithExactlyMaxLengthKey_DoesNotThrow()
    {
        // Key validation: 200 char key should be valid
        var maxKey = new string('a', 200);
        var exception = Record.Exception(() => CronRadarClient.Monitor(maxKey));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_WithNoApiKey_DoesNotThrow()
    {
        // When no API key is configured, should log warning but not throw
        var originalApiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CRONRADAR_API_KEY", null);
            var exception = Record.Exception(() => CronRadarClient.Monitor("test-monitor"));
            Assert.Null(exception);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CRONRADAR_API_KEY", originalApiKey);
        }
    }

    [Fact]
    public void StartJob_WithNullKey_DoesNotThrow()
    {
        var exception = Record.Exception(() => CronRadarClient.StartJob(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void CompleteJob_WithNullKey_DoesNotThrow()
    {
        var exception = Record.Exception(() => CronRadarClient.CompleteJob(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void FailJob_WithNullKey_DoesNotThrow()
    {
        var exception = Record.Exception(() => CronRadarClient.FailJob(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void FailJob_WithMessage_DoesNotThrow()
    {
        var exception = Record.Exception(() => CronRadarClient.FailJob("test-job", "Error message"));
        Assert.Null(exception);
    }

    [Fact]
    public void Wrap_WithNullAction_ThrowsNullReferenceException()
    {
        // Wrap should throw because the action itself is null, not because of CronRadar
        Assert.ThrowsAny<Exception>(() => CronRadarClient.Wrap("test-job", null!));
    }

    [Fact]
    public void Wrap_WithThrowingAction_RethrowsException()
    {
        // Wrap should re-throw exceptions from the wrapped action
        var ex = Assert.Throws<InvalidOperationException>(() =>
            CronRadarClient.Wrap("test-job", () => throw new InvalidOperationException("Test error")));

        Assert.Equal("Test error", ex.Message);
    }

    [Fact]
    public void Wrap_WithSuccessfulAction_Completes()
    {
        var executed = false;
        var exception = Record.Exception(() =>
            CronRadarClient.Wrap("test-job", () => executed = true));

        Assert.Null(exception);
        Assert.True(executed);
    }

    [Fact]
    public async Task WrapAsync_WithThrowingFunc_RethrowsException()
    {
        // WrapAsync should re-throw exceptions from the wrapped function
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CronRadarClient.WrapAsync("test-job", () => throw new InvalidOperationException("Test error")));
    }

    [Fact]
    public async Task WrapAsync_WithSuccessfulFunc_Completes()
    {
        var executed = false;
        var exception = await Record.ExceptionAsync(() =>
            CronRadarClient.WrapAsync("test-job", () => { executed = true; return Task.CompletedTask; }));

        Assert.Null(exception);
        Assert.True(executed);
    }

    [Fact]
    public async Task WrapAsyncWithResult_ReturnsValue()
    {
        var result = await CronRadarClient.WrapAsync("test-job", () => Task.FromResult(42));
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task WrapAsyncWithResult_WithThrowingFunc_RethrowsException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CronRadarClient.WrapAsync("test-job", () =>
            {
                throw new InvalidOperationException("Test error");
#pragma warning disable CS0162
                return Task.FromResult(42);
#pragma warning restore CS0162
            }));
    }

    [Fact]
    public void SyncMonitor_WithNullKey_DoesNotThrow()
    {
        var exception = Record.Exception(() => CronRadarClient.SyncMonitor(null!, "* * * * *"));
        Assert.Null(exception);
    }

    [Fact]
    public void SyncMonitor_WithNullSchedule_DoesNotThrow()
    {
        var exception = Record.Exception(() => CronRadarClient.SyncMonitor("test-job", null!));
        Assert.Null(exception);
    }

    [Fact]
    public void SyncMonitor_WithEmptySchedule_DoesNotThrow()
    {
        var exception = Record.Exception(() => CronRadarClient.SyncMonitor("test-job", ""));
        Assert.Null(exception);
    }

    [Fact]
    public void SyncMonitor_WithInvalidSchedule_DoesNotThrow()
    {
        // Invalid cron (only 2 fields instead of 5-6)
        var exception = Record.Exception(() => CronRadarClient.SyncMonitor("test-job", "invalid"));
        Assert.Null(exception);
    }

    [Theory]
    [InlineData("* * * * *")]           // Every minute - 5 fields
    [InlineData("0 * * * *")]           // Every hour
    [InlineData("0 0 * * *")]           // Daily at midnight
    [InlineData("0 0 * * 0")]           // Weekly on Sunday
    [InlineData("0 0 1 * *")]           // Monthly on the 1st
    [InlineData("0 0 * * * *")]         // 6 fields - with seconds
    public void SyncMonitor_WithValidSchedule_DoesNotThrow(string schedule)
    {
        var exception = Record.Exception(() => CronRadarClient.SyncMonitor("test-job", schedule));
        Assert.Null(exception);
    }

    [Fact]
    public async Task MonitorAsync_WithNullKey_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => CronRadarClient.MonitorAsync(null!));
        Assert.Null(exception);
    }

    [Fact]
    public async Task StartJobAsync_WithNullKey_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => CronRadarClient.StartJobAsync(null!));
        Assert.Null(exception);
    }

    [Fact]
    public async Task CompleteJobAsync_WithNullKey_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => CronRadarClient.CompleteJobAsync(null!));
        Assert.Null(exception);
    }

    [Fact]
    public async Task FailJobAsync_WithNullKey_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => CronRadarClient.FailJobAsync(null!));
        Assert.Null(exception);
    }

    [Fact]
    public async Task SyncMonitorAsync_WithNullKey_DoesNotThrow()
    {
        var exception = await Record.ExceptionAsync(() => CronRadarClient.SyncMonitorAsync(null!, "* * * * *"));
        Assert.Null(exception);
    }
}
