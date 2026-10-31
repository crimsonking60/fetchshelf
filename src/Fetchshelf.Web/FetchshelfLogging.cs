using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Fetchshelf.Web;

/// <summary>
/// Builds Fetchshelf's Serilog configuration: console output goes to stderr only;
/// a daily-rotating file sink is added when a log directory is configured; the minimum level
/// defaults to Debug and can be overridden by an environment-sourced level name.
/// </summary>
public static class FetchshelfLogging
{
    public static LoggerConfiguration Build(string? logDirEnv, string? logLevelEnv)
    {
        var levelSwitch = new LoggingLevelSwitch(LogEventLevel.Debug);
        if (!string.IsNullOrWhiteSpace(logLevelEnv) && Enum.TryParse<LogEventLevel>(logLevelEnv, ignoreCase: true, out var level))
        {
            levelSwitch.MinimumLevel = level;
        }

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
            .WriteTo.Async(sink => sink.Console(standardErrorFromLevel: LogEventLevel.Verbose), bufferSize: 10_000, blockWhenFull: false);

        if (!string.IsNullOrEmpty(logDirEnv))
        {
            configuration = configuration.WriteTo.File(
                Path.Combine(logDirEnv, "fetchshelf.log"),
                rollingInterval: RollingInterval.Day);
        }

        return configuration;
    }

    private static LogEventLevel AtLeast(LogEventLevel level, LogEventLevel floor) => level > floor ? level : floor;
}
