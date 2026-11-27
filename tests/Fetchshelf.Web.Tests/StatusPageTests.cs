using Fetchshelf.Web.Pages;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace Fetchshelf.Web.Tests;

/// <summary>The page shown for an error status that came without a page of its own, and the log line it writes.</summary>
public class StatusPageTests
{
    private static (StatusModel Model, CapturingLogger Logger) Shown(int code, string? referer, bool reExecuted = true, string? query = "?Show=any")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        if (referer is not null)
        {
            context.Request.Headers.Referer = referer;
        }

        if (reExecuted)
        {
            context.Features.Set<IStatusCodeReExecuteFeature>(new StatusCodeReExecuteFeature
            {
                OriginalPath = "/Lists/Detail/nope",
                OriginalQueryString = query,
                OriginalPathBase = string.Empty,
            });
        }

        var logger = new CapturingLogger();
        var model = new StatusModel(logger) { PageContext = new PageContext { HttpContext = context } };
        model.OnGet(code);
        return (model, logger);
    }

    [Fact]
    public void NotFound_SaysSo_AndLogsTheAddressAndWhereTheLinkWas()
    {
        var (model, logger) = Shown(StatusCodes.Status404NotFound, "http://localhost/Lists");

        Assert.Equal("Not found", model.Heading);
        Assert.Contains("(/Lists/Detail/nope)", model.Explanation);
        Assert.Equal([(LogLevel.Warning, "GET /Lists/Detail/nope?Show=any answered 404; the browser came from http://localhost/Lists")], logger.Entries);
    }

    [Fact]
    public void WithoutAReferer_TheLogSaysNoPage()
    {
        var (_, logger) = Shown(StatusCodes.Status400BadRequest, referer: null);

        Assert.Equal([(LogLevel.Warning, "GET /Lists/Detail/nope?Show=any answered 400; the browser came from no page")], logger.Entries);
    }

    [Fact]
    public void AnAddressWithoutAQuery_IsLoggedWithoutOne()
    {
        // The re-executed request's query is null then, which the log would write as "null".
        var (_, logger) = Shown(StatusCodes.Status404NotFound, referer: null, query: null);

        Assert.Equal([(LogLevel.Warning, "GET /Lists/Detail/nope answered 404; the browser came from no page")], logger.Entries);
    }

    [Fact]
    public void OpenedDirectly_LogsNothing()
    {
        var (_, logger) = Shown(StatusCodes.Status404NotFound, referer: null, reExecuted: false);

        Assert.Empty(logger.Entries);
    }

    internal sealed class CapturingLogger : ILogger<StatusModel>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
