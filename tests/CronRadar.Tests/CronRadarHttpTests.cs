using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;
using Xunit;
using CronRadarClient = CronRadar.CronRadar;

namespace CronRadar.Tests;

/// <summary>
/// Tests for HTTP communication in the CronRadar client.
/// Uses reflection to inject mock HttpClient for testing actual HTTP behavior.
/// </summary>
public class CronRadarHttpTests : IDisposable
{
    private readonly string _originalApiKey;
    private readonly bool _originalDebug;
    private Mock<HttpMessageHandler>? _mockHandler;
    private HttpClient? _mockClient;

    public CronRadarHttpTests()
    {
        _originalApiKey = Environment.GetEnvironmentVariable("CRONRADAR_API_KEY") ?? "";
        _originalDebug = CronRadarClient.DebugLoggingEnabled;

        // Set up a test API key
        Environment.SetEnvironmentVariable("CRONRADAR_API_KEY", "test-api-key-123");
    }

    public void Dispose()
    {
        // Restore original environment
        Environment.SetEnvironmentVariable("CRONRADAR_API_KEY", _originalApiKey);
        CronRadarClient.DebugLoggingEnabled = _originalDebug;

        // Reset the static HttpClient to allow other tests to work
        ResetHttpClient();
    }

    private void ResetHttpClient()
    {
        var field = typeof(CronRadarClient).GetField("_httpClient", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, null);
    }

    private void InjectMockHttpClient(Mock<HttpMessageHandler> mockHandler, string baseUrl = "https://cron.life")
    {
        _mockHandler = mockHandler;
        _mockClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };

        var field = typeof(CronRadarClient).GetField("_httpClient", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, _mockClient);
    }

    #region Monitor Tests

    [Fact]
    public void Monitor_SendsGetRequest_ToCorrectUrl()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;
        HttpMethod? capturedMethod = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
                capturedMethod = req.Method;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.Monitor("test-job");

        // Assert
        Assert.Equal(HttpMethod.Get, capturedMethod);
        Assert.Equal("/api/v1/ping/test-job/test-api-key-123", capturedUrl);
    }

    [Fact]
    public void Monitor_Handles200OkResponse_WithoutError()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act & Assert - should not throw
        var exception = Record.Exception(() => CronRadarClient.Monitor("test-job"));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_HandlesNetworkTimeout_Gracefully()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("Request timed out"));

        InjectMockHttpClient(mockHandler);

        // Act & Assert - should not throw (silent error handling)
        var exception = Record.Exception(() => CronRadarClient.Monitor("test-job"));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_HandlesConnectionRefused_Gracefully()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        InjectMockHttpClient(mockHandler);

        // Act & Assert - should not throw (silent error handling)
        var exception = Record.Exception(() => CronRadarClient.Monitor("test-job"));
        Assert.Null(exception);
    }

    [Fact]
    public void Monitor_With404AndSchedule_TriggersSyncMonitor()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var callCount = 0;
        var capturedUrls = new System.Collections.Generic.List<string>();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrls.Add(req.RequestUri?.PathAndQuery ?? "");
                callCount++;
            })
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                // First call returns 404, subsequent calls return 200
                if (req.RequestUri?.PathAndQuery?.Contains("/api/v1/sync") == true)
                    return new HttpResponseMessage(HttpStatusCode.OK);
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.Monitor("test-job", "* * * * *");

        // Assert - should have called /api/v1/sync after 404
        Assert.Contains(capturedUrls, u => u.Contains("/api/v1/sync"));
    }

    [Fact]
    public void Monitor_With404WithoutSchedule_DoesNotTriggerSync()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var capturedUrls = new System.Collections.Generic.List<string>();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrls.Add(req.RequestUri?.PathAndQuery ?? "");
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.Monitor("test-job"); // No schedule provided

        // Assert - should NOT have called /api/v1/sync
        Assert.DoesNotContain(capturedUrls, u => u.Contains("/api/v1/sync"));
    }

    [Fact]
    public void Monitor_UrlEncodesSpecialCharacters_InMonitorKey()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.Monitor("test job/special&chars");

        // Assert - special characters should be URL encoded
        Assert.NotNull(capturedUrl);
        Assert.Contains("test%20job%2Fspecial%26chars", capturedUrl);
    }

    #endregion

    #region StartJob Tests

    [Fact]
    public void StartJob_SendsPostRequest_ToStartEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;
        HttpMethod? capturedMethod = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
                capturedMethod = req.Method;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.StartJob("test-job");

        // Assert
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/api/v1/ping/test-job/test-api-key-123/start", capturedUrl);
    }

    [Fact]
    public void StartJob_IncludesScheduleQueryParam_WhenProvided()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.StartJob("test-job", "*/5 * * * *");

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("schedule=", capturedUrl);
        Assert.Contains("%2A%2F5", capturedUrl); // */5 URL encoded
    }

    [Fact]
    public void StartJob_With404AndSchedule_TriggersAutoRegistration()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var callCount = 0;
        var capturedUrls = new System.Collections.Generic.List<string>();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrls.Add(req.RequestUri?.PathAndQuery ?? "");
                callCount++;
            })
            .ReturnsAsync((HttpRequestMessage req, CancellationToken _) =>
            {
                if (req.RequestUri?.PathAndQuery?.Contains("/api/v1/sync") == true)
                    return new HttpResponseMessage(HttpStatusCode.OK);
                if (callCount == 1)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.StartJob("test-job", "* * * * *");

        // Assert
        Assert.Contains(capturedUrls, u => u.Contains("/api/v1/sync"));
    }

    [Fact]
    public void StartJob_With404WithoutSchedule_DoesNotTriggerSync()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var capturedUrls = new System.Collections.Generic.List<string>();

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrls.Add(req.RequestUri?.PathAndQuery ?? "");
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.StartJob("test-job"); // No schedule

        // Assert
        Assert.DoesNotContain(capturedUrls, u => u.Contains("/api/v1/sync"));
    }

    #endregion

    #region CompleteJob Tests

    [Fact]
    public void Lifecycle_IncludesExecutionCorrelationAndAttempt()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) => capturedUrl = request.RequestUri?.PathAndQuery)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        InjectMockHttpClient(mockHandler);

        CronRadarClient.StartJob("test-job", apiKey: "test-api-key-123", executionId: "run 42", attempt: 2);

        Assert.NotNull(capturedUrl);
        Assert.Contains("run_id=run%2042", capturedUrl);
        Assert.Contains("attempt=2", capturedUrl);
    }

    [Fact]
    public void CompleteJob_SendsPostRequest_ToCompleteEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;
        HttpMethod? capturedMethod = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
                capturedMethod = req.Method;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.CompleteJob("test-job");

        // Assert
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/api/v1/ping/test-job/test-api-key-123/complete", capturedUrl);
    }

    [Fact]
    public async Task CompleteJobAsync_SendsOnlyBoundedOutcomeValuesAsJson()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedBody = null;
        mockHandler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (request, _) => capturedBody = request.Content == null ? null : await request.Content.ReadAsStringAsync())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));
        InjectMockHttpClient(mockHandler);

        await CronRadarClient.CompleteJobAsync("test-job", outcomes: new System.Collections.Generic.Dictionary<string, object>
        {
            ["records_processed"] = 42,
            ["successful"] = true
        });

        var json = JsonSerializer.Deserialize<JsonElement>(capturedBody!);
        Assert.Equal(42, json.GetProperty("outcomes").GetProperty("records_processed").GetInt32());
        Assert.True(json.GetProperty("outcomes").GetProperty("successful").GetBoolean());
    }

    [Fact]
    public void CompleteJob_InvalidOutcomeNeverThrowsOrSends()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("must not send"));
        InjectMockHttpClient(mockHandler);
        var error = Record.Exception(() => CronRadarClient.CompleteJob("test-job", outcomes: new System.Collections.Generic.Dictionary<string, object> { ["payload"] = "sensitive" }));
        Assert.Null(error);
    }

    #endregion

    #region FailJob Tests

    [Fact]
    public void FailJob_SendsPostRequest_ToFailEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;
        HttpMethod? capturedMethod = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
                capturedMethod = req.Method;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.FailJob("test-job");

        // Assert
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/api/v1/ping/test-job/test-api-key-123/fail", capturedUrl);
    }

    [Fact]
    public void FailJob_IncludesMessageQueryParam_WhenProvided()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.FailJob("test-job", "Job failed due to timeout");

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.Contains("message=", capturedUrl);
        Assert.Contains("Job%20failed%20due%20to%20timeout", capturedUrl);
    }

    [Fact]
    public void FailJob_UrlEncodesSpecialCharacters_InMessage()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.FailJob("test-job", "Error: 404 & Not Found!");

        // Assert
        Assert.NotNull(capturedUrl);
        // Special characters should be URL encoded
        Assert.Contains("%26", capturedUrl); // &
        Assert.Contains("%21", capturedUrl); // !
    }

    #endregion

    #region SyncMonitor Tests

    [Fact]
    public void SyncMonitor_SendsPostRequest_ToApiSyncEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;
        HttpMethod? capturedMethod = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
                capturedMethod = req.Method;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.SyncMonitor("test-job", "* * * * *");

        // Assert
        Assert.Equal(HttpMethod.Post, capturedMethod);
        Assert.Equal("/api/v1/sync", capturedUrl);
    }

    [Fact]
    public void SyncMonitor_IncludesBasicAuthHeader()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedAuthHeader = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedAuthHeader = req.Headers.Authorization?.ToString();
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.SyncMonitor("test-job", "* * * * *");

        // Assert
        Assert.NotNull(capturedAuthHeader);
        Assert.StartsWith("Basic ", capturedAuthHeader);

        // Decode and verify it's the API key with empty password
        var base64 = capturedAuthHeader.Replace("Basic ", "");
        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        Assert.Equal("test-api-key-123:", decoded);
    }

    [Fact]
    public async Task SyncMonitor_SendsCorrectJsonPayload()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedBody = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                if (req.Content != null)
                {
                    capturedBody = await req.Content.ReadAsStringAsync();
                }
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.SyncMonitor("test-job", "*/5 * * * *", "manual", "Test Job Name");

        // Assert
        Assert.NotNull(capturedBody);
        var payload = JsonSerializer.Deserialize<JsonElement>(capturedBody);

        Assert.Equal("manual", payload.GetProperty("source").GetString());
        var monitors = payload.GetProperty("monitors");
        Assert.Equal(1, monitors.GetArrayLength());

        var monitor = monitors[0];
        Assert.Equal("test-job", monitor.GetProperty("key").GetString());
        Assert.Equal("Test Job Name", monitor.GetProperty("name").GetString());
        Assert.Equal("*/5 * * * *", monitor.GetProperty("schedule").GetString());
        Assert.Equal(60, monitor.GetProperty("gracePeriod").GetInt32());
    }

    [Fact]
    public void SyncMonitor_GeneratesReadableName_WhenNotProvided()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedBody = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>(async (req, _) =>
            {
                if (req.Content != null)
                {
                    capturedBody = await req.Content.ReadAsStringAsync();
                }
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        CronRadarClient.SyncMonitor("check-overdue-pings", "* * * * *"); // No name provided

        // Assert
        Assert.NotNull(capturedBody);
        var payload = JsonSerializer.Deserialize<JsonElement>(capturedBody);
        var monitor = payload.GetProperty("monitors")[0];

        // check-overdue-pings should become "Check Overdue Pings"
        Assert.Equal("Check Overdue Pings", monitor.GetProperty("name").GetString());
    }

    #endregion

    #region Async Method Tests

    [Fact]
    public async Task MonitorAsync_SendsGetRequest_ToCorrectUrl()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        await CronRadarClient.MonitorAsync("test-job");

        // Assert
        Assert.Equal("/api/v1/ping/test-job/test-api-key-123", capturedUrl);
    }

    [Fact]
    public async Task StartJobAsync_SendsPostRequest_ToStartEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        await CronRadarClient.StartJobAsync("test-job");

        // Assert
        Assert.Equal("/api/v1/ping/test-job/test-api-key-123/start", capturedUrl);
    }

    [Fact]
    public async Task CompleteJobAsync_SendsPostRequest_ToCompleteEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        await CronRadarClient.CompleteJobAsync("test-job");

        // Assert
        Assert.Equal("/api/v1/ping/test-job/test-api-key-123/complete", capturedUrl);
    }

    [Fact]
    public async Task FailJobAsync_SendsPostRequest_ToFailEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        await CronRadarClient.FailJobAsync("test-job", "Test error");

        // Assert
        Assert.NotNull(capturedUrl);
        Assert.StartsWith("/api/v1/ping/test-job/test-api-key-123/fail", capturedUrl);
        Assert.Contains("message=Test%20error", capturedUrl);
    }

    [Fact]
    public async Task SyncMonitorAsync_SendsPostRequest_ToApiSyncEndpoint()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        string? capturedUrl = null;

        mockHandler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                capturedUrl = req.RequestUri?.PathAndQuery;
            })
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        InjectMockHttpClient(mockHandler);

        // Act
        await CronRadarClient.SyncMonitorAsync("test-job", "* * * * *");

        // Assert
        Assert.Equal("/api/v1/sync", capturedUrl);
    }

    #endregion
}
