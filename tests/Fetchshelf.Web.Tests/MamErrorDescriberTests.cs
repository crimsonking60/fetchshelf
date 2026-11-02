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
    public void Describe_HttpErrorStatus_SaysMamAnsweredWithAnError() =>
        Assert.Equal("MAM answered with an error. It may be down - try again later.",
            MamErrorDescriber.Describe(new HttpRequestException("x", null, HttpStatusCode.ServiceUnavailable), Timeout));

    [Fact]
    public void Describe_ConnectionFailure_SaysMamCouldNotBeReached() =>
        Assert.Equal("MAM could not be reached. Check this machine's internet connection.",
            MamErrorDescriber.Describe(new HttpRequestException("Connection failed", new IOException("Name or service not known")), Timeout));

    [Fact]
    public void Describe_RateLimited_GivesAdviceWithoutTheStatusCode()
    {
        var message = MamErrorDescriber.Describe(new MamRateLimitedException("MAM rate limit hit (HTTP 429)."), Timeout);

        Assert.Equal("MAM is limiting requests from this account. Wait a few minutes and try again.", message);
        Assert.DoesNotContain("429", message);
    }

    [Fact]
    public void Describe_Forbidden_UsesItsOwnMessage() =>
        Assert.Equal("MAM refused the request.", MamErrorDescriber.Describe(new MamForbiddenException("MAM refused the request."), Timeout));

    [Fact]
    public void Describe_UnreadableResponse_SaysSo() =>
        Assert.Equal("MAM sent a response Fetchshelf could not read.", MamErrorDescriber.Describe(new JsonException("bad"), Timeout));

    [Fact]
    public void Describe_AnythingElse_PointsToTheLog() =>
        Assert.Equal("Something went wrong while talking to MAM. The log has the details.",
            MamErrorDescriber.Describe(new InvalidOperationException("boom"), Timeout));
}
