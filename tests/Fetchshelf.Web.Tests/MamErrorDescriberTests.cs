using System.Net;
using System.Text.Json;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Web.Pages;

namespace Fetchshelf.Web.Tests;

public class MamErrorDescriberTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public void Describe_HttpClientTimeout_SaysHowLongItWaited()
    {
        var timeout = new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.", new TimeoutException());

        var message = MamErrorDescriber.Describe(timeout, Timeout);

        Assert.Contains("did not answer within 30 seconds", message);
        Assert.DoesNotContain("HttpClient", message);
    }

    [Fact]
    public void Describe_HttpErrorStatus_NamesTheStatus() =>
        Assert.Equal("MAM answered with HTTP 503 (ServiceUnavailable).",
            MamErrorDescriber.Describe(new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable), Timeout));

    [Fact]
    public void Describe_ConnectionFailure_SaysMamCouldNotBeReached() =>
        Assert.Equal("MAM could not be reached (Name or service not known).",
            MamErrorDescriber.Describe(new HttpRequestException("Connection failed", new IOException("Name or service not known")), Timeout));

    [Fact]
    public void Describe_MamSpecificException_UsesItsOwnMessage() =>
        Assert.Equal("MAM rate limit hit (HTTP 429).", MamErrorDescriber.Describe(new MamRateLimitedException("MAM rate limit hit (HTTP 429)."), Timeout));

    [Fact]
    public void Describe_UnreadableResponse_SaysSo() =>
        Assert.Equal("MAM sent a response Fetchshelf could not read.", MamErrorDescriber.Describe(new JsonException("bad"), Timeout));

    [Fact]
    public void Describe_AnythingElse_IsReportedAsUnexpected() =>
        Assert.Equal("Unexpected error: boom", MamErrorDescriber.Describe(new InvalidOperationException("boom"), Timeout));
}
