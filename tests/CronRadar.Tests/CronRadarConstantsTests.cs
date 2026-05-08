using Xunit;

namespace CronRadar.Tests;

public class CronRadarConstantsTests
{
    [Fact]
    public void MaxMonitorKeyLength_Is200()
    {
        Assert.Equal(200, CronRadarConstants.MaxMonitorKeyLength);
    }

    [Fact]
    public void DefaultTimeoutSeconds_Is5()
    {
        Assert.Equal(5, CronRadarConstants.DefaultTimeoutSeconds);
    }

    [Fact]
    public void DefaultGracePeriodSeconds_Is60()
    {
        Assert.Equal(60, CronRadarConstants.DefaultGracePeriodSeconds);
    }

    [Fact]
    public void ApiKeyEnvironmentVariable_IsCorrect()
    {
        Assert.Equal("CRONRADAR_API_KEY", CronRadarConstants.ApiKeyEnvironmentVariable);
    }

    [Fact]
    public void DebugEnvironmentVariable_IsCorrect()
    {
        Assert.Equal("CRONRADAR_DEBUG", CronRadarConstants.DebugEnvironmentVariable);
    }
}
