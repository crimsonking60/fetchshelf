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
    public void Build_UnsetLevel_DefaultsToInformation()
    {
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            var logger = FetchshelfLogging.Build(null, null).CreateLogger();

            LogAndFlush(logger, l =>
            {
                l.Write(LogEventLevel.Debug, "debug-message");
                l.Write(LogEventLevel.Information, "information-message");
            });

            var output = writer.ToString();
            Assert.DoesNotContain("debug-message", output);
            Assert.Contains("information-message", output);
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

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore.Database.Command")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    [InlineData("System.Net.Http.HttpClient.mam.LogicalHandler")]
    [InlineData("System.Net.Http.HttpClient.mam.ClientHandler")]
    [InlineData("Microsoft.Extensions.Diagnostics.HealthChecks.DefaultHealthCheckService")]
    public void Build_FrameworkSource_KeepsWarningsButDropsInformation(string sourceContext)
    {
        var output = CaptureStderr(null, logger =>
        {
            var framework = logger.ForContext("SourceContext", sourceContext);
            framework.Information("framework-info");
            framework.Warning("framework-warning");
            logger.Information("app-info");
        });

        Assert.DoesNotContain("framework-info", output);
        Assert.Contains("framework-warning", output);
        Assert.Contains("app-info", output);
    }

    [Fact]
    public void Build_MigrationSource_KeepsInformation()
    {
        var output = CaptureStderr(null, logger =>
            logger.ForContext("SourceContext", "Microsoft.EntityFrameworkCore.Migrations").Information("applying-migration"));

        Assert.Contains("applying-migration", output);
    }

    [Fact]
    public void Build_LevelStricterThanFrameworkFloor_AlsoAppliesToFrameworkSources()
    {
        var output = CaptureStderr("Error", logger =>
            logger.ForContext("SourceContext", "Microsoft.AspNetCore.Hosting.Diagnostics").Warning("framework-warning"));

        Assert.DoesNotContain("framework-warning", output);
    }

    private static string CaptureStderr(string? logLevelEnv, Action<ILogger> act)
    {
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            LogAndFlush(FetchshelfLogging.Build(null, logLevelEnv).CreateLogger(), act);
            return writer.ToString();
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    [Fact]
    public async Task Build_StderrBlocked_LoggingDoesNotBlockTheCaller()
    {
        var originalError = Console.Error;
        using var release = new ManualResetEventSlim();
        try
        {
            Console.SetError(new BlockingWriter(release));
            var logger = FetchshelfLogging.Build(null, null).CreateLogger();

            var logging = Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    logger.Information("message {Index}", i);
                }
            });

            var finished = await Task.WhenAny(logging, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Same(logging, finished);

            release.Set();
            (logger as IDisposable)?.Dispose();
        }
        finally
        {
            release.Set();
            Console.SetError(originalError);
        }
    }

    private sealed class BlockingWriter(ManualResetEventSlim release) : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void Write(char value) => release.Wait();
    }

    [Fact]
    public void Build_UnparseableLevel_FallsBackToInformationRatherThanThrowing()
    {
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            var logger = FetchshelfLogging.Build(null, "not-a-real-level").CreateLogger();

            LogAndFlush(logger, l =>
            {
                l.Write(LogEventLevel.Debug, "debug-message");
                l.Write(LogEventLevel.Information, "information-message");
            });

            var output = writer.ToString();
            Assert.DoesNotContain("debug-message", output);
            Assert.Contains("information-message", output);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
