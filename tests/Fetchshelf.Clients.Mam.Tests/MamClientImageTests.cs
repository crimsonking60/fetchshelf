using System.Net;
using System.Net.Http.Headers;

namespace Fetchshelf.Clients.Mam.Tests;

public class MamClientImageTests
{
    private static MamClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new FakeHttpMessageHandler(handler)), new MamClientOptions { MamId = "cookie" });

    private static HttpResponseMessage Image(byte[] bytes, string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Theory]
    [InlineData("https://www.myanonamouse.net/covers/1.jpg", true)]
    [InlineData("https://cdn.myanonamouse.net/t/p/1.jpg", true)]
    [InlineData("https://img.myanonamouse.net/1.jpg", true)]
    [InlineData("https://myanonamouse.net/1.jpg", true)]
    [InlineData("http://cdn.myanonamouse.net/1.jpg", false)]
    [InlineData("https://evilmyanonamouse.net/1.jpg", false)]
    [InlineData("https://myanonamouse.net.example.com/1.jpg", false)]
    [InlineData("https://example.com/1.jpg", false)]
    [InlineData("/covers/1.jpg", false)]
    [InlineData(null, false)]
    public void IsMamAddress_OnlyMamsOwnHostsOverHttps(string? url, bool expected) =>
        Assert.Equal(expected, CreateClient(_ => throw new InvalidOperationException()).IsMamAddress(url));

    [Fact]
    public async Task DownloadImageAsync_SendsTheSessionAndReturnsTheImage()
    {
        HttpRequestMessage? sent = null;
        var client = CreateClient(request =>
        {
            sent = request;
            return Image([1, 2, 3]);
        });

        var image = await client.DownloadImageAsync("https://cdn.myanonamouse.net/t/p/1.jpg");

        Assert.Equal([1, 2, 3], image!.Bytes);
        Assert.Equal("image/jpeg", image.ContentType);
        Assert.Contains("mam_id=cookie", sent!.Headers.GetValues("Cookie").Single());
    }

    [Fact]
    public async Task DownloadImageAsync_AddressOutsideMam_IsNeverRequested()
    {
        var requested = false;
        var client = CreateClient(_ =>
        {
            requested = true;
            return Image([1]);
        });

        Assert.Null(await client.DownloadImageAsync("https://example.com/1.jpg"));
        Assert.False(requested);
    }

    [Fact]
    public async Task DownloadImageAsync_AnswerThatIsNotAnImage_IsNull()
    {
        var client = CreateClient(_ => Image("<html>login</html>"u8.ToArray(), "text/html"));

        Assert.Null(await client.DownloadImageAsync("https://www.myanonamouse.net/covers/1.jpg"));
    }

    [Fact]
    public async Task DownloadImageAsync_ImageTooLarge_IsNull()
    {
        var client = CreateClient(_ => Image(new byte[MamClient.MaxImageBytes + 1]));

        Assert.Null(await client.DownloadImageAsync("https://www.myanonamouse.net/covers/1.jpg"));
    }
}
