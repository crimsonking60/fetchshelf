using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Fetchshelf.Web;

/// <summary>
/// Builds Fetchshelf's Serilog configuration, Fetchshelf-spec.md §11: console output goes to stderr only;
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

        var configuration = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);

        if (!string.IsNullOrEmpty(logDirEnv))
        {
            configuration = configuration.WriteTo.File(
                Path.Combine(logDirEnv, "fetchshelf.log"),
                rollingInterval: RollingInterval.Day);
        }

        return configuration;
    }
}
