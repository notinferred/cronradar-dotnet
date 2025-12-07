using System;
using System.Text;
using System.Text.Json;

namespace CronRadar
{
    /// <summary>
    /// Normalizes monitor keys to ensure consistency across sync and ping operations.
    /// Handles various formats including JSON-serialized strings, framework-specific keys, and custom formats.
    /// </summary>
    public static class CronRadarKeyNormalizer
    {
        /// <summary>
        /// Normalizes a single key into a valid CronRadar monitor key.
        /// Handles JSON-serialized strings, removes quotes, converts separators to hyphens,
        /// and ensures only alphanumeric characters and hyphens remain.
        /// Examples:
        /// - "check-overdue-pings" -> "check-overdue-pings"
        /// - "\"check-overdue-pings\"" -> "check-overdue-pings"
        /// - "Check_Overdue_Pings" -> "check-overdue-pings"
        /// - "Reports.DailyReport" -> "reports-dailyreport"
        /// </summary>
        /// <param name="key">The key to normalize</param>
        /// <returns>Normalized monitor key in lowercase with only alphanumeric and hyphens</returns>
        public static string Normalize(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return key ?? string.Empty;

            // Handle JSON-serialized strings (from Hangfire serialization)
            // Example: "\"check-overdue-pings\"" becomes "check-overdue-pings"
            if (key.StartsWith("\"") && key.EndsWith("\"") && key.Length > 2)
            {
                try
                {
                    var deserialized = JsonSerializer.Deserialize<string>(key);
                    if (!string.IsNullOrEmpty(deserialized))
                    {
                        key = deserialized;
                    }
                }
                catch
                {
                    // If deserialization fails, continue with manual cleanup
                }
            }

            // Remove all types of quotes first
            key = key.Trim('"', '\'', '`', ' ', '\t', '\r', '\n')
                     .Replace("\\\"", "")
                     .Replace("\"", "")
                     .Replace("'", "")
                     .Replace("`", "");

            // Now normalize to alphanumeric + hyphens
            var result = new StringBuilder();
            var lastWasSeparator = false;

            foreach (var c in key)
            {
                if (char.IsLetterOrDigit(c))
                {
                    result.Append(char.ToLowerInvariant(c));
                    lastWasSeparator = false;
                }
                else if (c == '.' || c == '/' || c == '_' || c == ' ' || c == '-')
                {
                    // Convert all separators to hyphens, but avoid consecutive hyphens
                    if (!lastWasSeparator && result.Length > 0)
                    {
                        result.Append('-');
                        lastWasSeparator = true;
                    }
                }
                // Skip other characters
            }

            // Remove trailing hyphen if present
            if (result.Length > 0 && result[result.Length - 1] == '-')
            {
                result.Length--;
            }

            return result.ToString();
        }

        /// <summary>
        /// Normalizes a Quartz.NET-style job key (Group.Name) into a CronRadar monitor key.
        /// Examples:
        /// - ("Reports", "DailyReport") -> "reports-dailyreport"
        /// - ("DEFAULT", "BackupJob") -> "default-backupjob"
        /// - ("My Reports", "Daily Report") -> "my-reports-daily-report"
        /// </summary>
        /// <param name="group">The job group (e.g., "Reports")</param>
        /// <param name="name">The job name (e.g., "DailyReport")</param>
        /// <returns>Normalized monitor key suitable for CronRadar</returns>
        public static string Normalize(string group, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            // Combine group and name with a dot separator
            var combined = string.IsNullOrWhiteSpace(group)
                ? name
                : $"{group}.{name}";

            return Normalize(combined);
        }
    }
}
