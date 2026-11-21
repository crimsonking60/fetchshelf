using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Fetchshelf.Web;

/// <summary>
/// Builds Fetchshelf's Serilog configuration: console output goes to stderr only;
/// a daily-rotating file sink is added when a log directory is configured; the minimum level
/// defaults to Information and can be overridden by an environment-sourced level name.
/// </summary>
public static class FetchshelfLogging
{
    /// <summary>A day's log file starts a new one, <c>fetchshelf20261121_001.log</c>, at this size.</summary>
    public const long FileSizeLimitBytes = 100L * 1024 * 1024;

    /// <summary>The newest log files kept; older ones are deleted.</summary>
    public const int RetainedFileCount = 31;

    public static LoggerConfiguration Build(string? logDirEnv, string? logLevelEnv)
    {
        var levelSwitch = new LoggingLevelSwitch(ParseLevel(logLevelEnv) ?? LogEventLevel.Information);

        var frameworkLevel = AtLeast(levelSwitch.MinimumLevel, LogEventLevel.Warning);

        // Console writes block when the terminal stops draining output (paused, Ctrl-S, stalled SSH client).
        // Writing through a bounded buffer that drops on overflow keeps that from stalling every request.
        var configuration = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", frameworkLevel)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Migrations", AtLeast(levelSwitch.MinimumLevel, LogEventLevel.Information))
            .MinimumLevel.Override("Microsoft.AspNetCore", frameworkLevel)
            // HttpClient logs each request's URL path, and MAM download paths contain the personal download hash.
            .MinimumLevel.Override("System.Net.Http", frameworkLevel)
            // The container health check calls /health every 30 seconds; its debug lines would bury everything else.
            .MinimumLevel.Override("Microsoft.Extensions.Diagnostics.HealthChecks", frameworkLevel)
            // The web interface's keys are stored unencrypted in the database folder on purpose: they only protect
            // form submissions. Data Protection warns about that each time it creates a key; other warnings stay.
            .Filter.ByExcluding(IsUnencryptedKeyWarning)
            .WriteTo.Async(sink => sink.Console(standardErrorFromLevel: LogEventLevel.Verbose), bufferSize: 10_000, blockWhenFull: false);

        if (!string.IsNullOrEmpty(logDirEnv))
        {
            configuration = configuration.WriteTo.File(
                Path.Combine(logDirEnv, "fetchshelf.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: FileSizeLimitBytes,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: RetainedFileCount);
        }

        return configuration;
    }

    /// <summary>
    /// The warning to log when <c>FETCHSHELF_LOG</c> is set to something that is not a level, which is then ignored; null
    /// when it is unset or a level.
    /// </summary>
    public static string? LevelWarning(string? logLevelEnv) =>
        string.IsNullOrWhiteSpace(logLevelEnv) || ParseLevel(logLevelEnv) is not null
            ? null
            : $"FETCHSHELF_LOG is '{logLevelEnv}', which is not a log level, so Information is used. "
              + $"Expected one of {string.Join(", ", Enum.GetNames<LogEventLevel>())}.";

    // Enum.TryParse also takes "2" or "Debug, Error"; only the names count.
    private static LogEventLevel? ParseLevel(string? name) =>
        Enum.GetNames<LogEventLevel>().FirstOrDefault(known => string.Equals(known, name?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } match
            ? Enum.Parse<LogEventLevel>(match)
            : null;

    private static bool IsUnencryptedKeyWarning(LogEvent logEvent) =>
        logEvent.Properties.TryGetValue("SourceContext", out var source)
        && source is ScalarValue { Value: "Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager" }
        && logEvent.MessageTemplate.Text.StartsWith("No XML encryptor configured", StringComparison.Ordinal);

    private static LogEventLevel AtLeast(LogEventLevel level, LogEventLevel floor) => level > floor ? level : floor;
}
