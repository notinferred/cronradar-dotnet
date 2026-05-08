using Xunit;

namespace CronRadar.Tests;

public class CronRadarKeyNormalizerTests
{
    [Theory]
    [InlineData("check-overdue-pings", "check-overdue-pings")]
    [InlineData("Check-Overdue-Pings", "check-overdue-pings")]
    [InlineData("CHECK-OVERDUE-PINGS", "check-overdue-pings")]
    public void Normalize_KebabCase_ReturnsLowercase(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("check_overdue_pings", "check-overdue-pings")]
    [InlineData("Check_Overdue_Pings", "check-overdue-pings")]
    [InlineData("CHECK_OVERDUE_PINGS", "check-overdue-pings")]
    public void Normalize_SnakeCase_ConvertsToKebabCase(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Reports.DailyReport", "reports-dailyreport")]
    [InlineData("Jobs.Cleanup.Database", "jobs-cleanup-database")]
    [InlineData("Namespace.SubNamespace.JobName", "namespace-subnamespace-jobname")]
    public void Normalize_DotSeparated_ConvertsToKebabCase(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("\"check-overdue-pings\"", "check-overdue-pings")]
    [InlineData("'check-overdue-pings'", "check-overdue-pings")]
    [InlineData("`check-overdue-pings`", "check-overdue-pings")]
    public void Normalize_QuotedStrings_RemovesQuotes(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Normalize_EmptyString_ReturnsEmptyString()
    {
        var result = CronRadarKeyNormalizer.Normalize("");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_WhitespaceString_ReturnsWhitespace()
    {
        // Implementation returns whitespace as-is because IsNullOrWhiteSpace returns input
        var result = CronRadarKeyNormalizer.Normalize("   ");
        Assert.Equal("   ", result);
    }

    [Fact]
    public void Normalize_NullString_ReturnsEmptyString()
    {
        var result = CronRadarKeyNormalizer.Normalize(null!);
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("job--name", "job-name")]
    [InlineData("job__name", "job-name")]
    [InlineData("job..name", "job-name")]
    [InlineData("job  name", "job-name")]
    public void Normalize_ConsecutiveSeparators_CollapsesToSingleHyphen(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("job-name-", "job-name")]
    [InlineData("job-name.", "job-name")]
    [InlineData("job-name_", "job-name")]
    public void Normalize_TrailingSeparator_RemovesTrailing(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("job@name#test", "jobnametest")]
    [InlineData("job!name?test", "jobnametest")]
    [InlineData("job$name%test", "jobnametest")]
    public void Normalize_SpecialCharacters_RemovesThem(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("job123", "job123")]
    [InlineData("job-123-name", "job-123-name")]
    [InlineData("123job", "123job")]
    public void Normalize_WithNumbers_PreservesNumbers(string input, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    // Tests for the two-parameter overload (group, name)
    [Theory]
    [InlineData("Reports", "DailyReport", "reports-dailyreport")]
    [InlineData("DEFAULT", "BackupJob", "default-backupjob")]
    [InlineData("My Reports", "Daily Report", "my-reports-daily-report")]
    public void Normalize_GroupAndName_CombinesCorrectly(string group, string name, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(group, name);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("", "BackupJob", "backupjob")]
    [InlineData(null, "BackupJob", "backupjob")]
    [InlineData("   ", "BackupJob", "backupjob")]
    public void Normalize_EmptyGroup_ReturnsNameOnly(string? group, string name, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(group!, name);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Reports", "", "")]
    [InlineData("Reports", null, "")]
    [InlineData("Reports", "   ", "")]
    public void Normalize_EmptyName_ReturnsEmptyString(string group, string? name, string expected)
    {
        var result = CronRadarKeyNormalizer.Normalize(group, name!);
        Assert.Equal(expected, result);
    }
}
