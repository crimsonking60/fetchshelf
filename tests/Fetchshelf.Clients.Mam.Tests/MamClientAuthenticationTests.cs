using System.Net;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientAuthenticationTests
{
    private const string EmptyResultJson = """{"perpage":0,"start":0,"total":0,"found":0,"data":[]}""";

    private static MamClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler, string mamId = "configured-mam-id") =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = mamId });

    [Fact]
    public async Task SearchAsync_ConfiguredMamId_SendsConfiguredCookie()
    {
        var cookiesSent = new List<string?>();
        var client = CreateClient(request =>
        {
            cookiesSent.Add(request.Headers.TryGetValues("Cookie", out var values) ? values.First() : null);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) };
        });

        await client.SearchAsync(new MamSearchQuery());

        Assert.Equal(["mam_id=configured-mam-id"], cookiesSent);
    }

    [Fact]
    public async Task SearchAsync_ResponseSetsRotatedCookie_KeepsSendingConfiguredCookie()
    {
        var cookiesSent = new List<string?>();
        var client = CreateClient(request =>
        {
            cookiesSent.Add(request.Headers.TryGetValues("Cookie", out var values) ? values.First() : null);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) };
            response.Headers.Add("Set-Cookie", "mam_id=rotated-cookie; path=/; HttpOnly");
            return response;
        });

        await client.SearchAsync(new MamSearchQuery());
        await client.SearchAsync(new MamSearchQuery());

        Assert.Equal(["mam_id=configured-mam-id", "mam_id=configured-mam-id"], cookiesSent);
    }

    [Fact]
    public async Task SearchAsync_EmptyMamId_SendsNoCookieHeader()
    {
        var cookiesSent = new List<string?>();
        var client = CreateClient(request =>
        {
            cookiesSent.Add(request.Headers.TryGetValues("Cookie", out var values) ? values.First() : null);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) };
        }, mamId: "");

        await client.SearchAsync(new MamSearchQuery());

        Assert.Equal([null], cookiesSent);
    }

    [Fact]
    public async Task SearchAsync_RequestBody_IsSentWithContentLengthAndPlainJsonContentType()
    {
        long? contentLength = null;
        bool? chunked = null;
        string? contentType = null;
        var client = CreateClient(request =>
        {
            contentLength = request.Content!.Headers.ContentLength;
            chunked = request.Headers.TransferEncodingChunked;
            contentType = request.Content.Headers.ContentType?.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptyResultJson) };
        });

        await client.SearchAsync(new MamSearchQuery());

        Assert.True(contentLength > 0, "Expected a known Content-Length.");
        Assert.NotEqual(true, chunked);
        Assert.Equal("application/json", contentType);
    }
}
