using Serilog;
using Serilog.Events;

namespace Fetchshelf.Web.Tests;

/// <summary>Covers task 1.2: the Serilog configuration helper.</summary>
public sealed class FetchshelfLoggingTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-logging-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static void LogAndFlush(ILogger logger, Action<ILogger> act)
    {
        act(logger);
        (logger as IDisposable)?.Dispose();
    }

    [Fact]
    public void Build_WithNonEmptyLogDir_WritesLogFile()
    {
        var logger = FetchshelfLogging.Build(_tempDir, null).CreateLogger();

        LogAndFlush(logger, l => l.Information("hello"));

        var files = Directory.GetFiles(_tempDir, "fetchshelf*.log");
        Assert.NotEmpty(files);
        Assert.Contains("hello", File.ReadAllText(files[0]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Build_WithUnsetOrEmptyLogDir_DoesNotThrow(string? logDirEnv)
    {
        var logger = FetchshelfLogging.Build(logDirEnv, null).CreateLogger();

        LogAndFlush(logger, l => l.Information("hello"));
    }

    [Fact]
    public void Build_UnsetLevel_DefaultsToDebug()
    {
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            var logger = FetchshelfLogging.Build(null, null).CreateLogger();

            LogAndFlush(logger, l => l.Write(LogEventLevel.Debug, "debug-message"));

            Assert.Contains("debug-message", writer.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Build_LevelSetToWarning_SuppressesDebugButKeepsWarning()
    {
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            var logger = FetchshelfLogging.Build(null, "Warning").CreateLogger();

            LogAndFlush(logger, l =>
            {
                l.Write(LogEventLevel.Debug, "debug-message");
                l.Write(LogEventLevel.Warning, "warning-message");
            });

            var output = writer.ToString();
            Assert.DoesNotContain("debug-message", output);
            Assert.Contains("warning-message", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void Build_UnparseableLevel_FallsBackToDebugRatherThanThrowing()
    {
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            var logger = FetchshelfLogging.Build(null, "not-a-real-level").CreateLogger();

            LogAndFlush(logger, l => l.Write(LogEventLevel.Debug, "debug-message"));

            Assert.Contains("debug-message", writer.ToString());
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
