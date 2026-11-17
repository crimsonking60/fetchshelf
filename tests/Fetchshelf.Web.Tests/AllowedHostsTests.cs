using System.Net;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Web.Hosting;

namespace Fetchshelf.Web.Tests;

/// <summary>`allowed_hosts`: other Host names are refused before any page runs.</summary>
public sealed class AllowedHostsTests
{
    private static async Task<HttpStatusCode> GetWithHostAsync(FetchshelfWebAppFixture fixture, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Jobs");
        request.Headers.Host = host;
        using var response = await fixture.CreateClient().SendAsync(request);
        return response.StatusCode;
    }

    [Theory]
    [InlineData("fetchshelf.lan", HttpStatusCode.OK)]
    [InlineData("fetchshelf.lan:3157", HttpStatusCode.OK)]
    [InlineData("books.example.org", HttpStatusCode.OK)]
    [InlineData("localhost", HttpStatusCode.OK)]
    [InlineData("127.0.0.1", HttpStatusCode.OK)]
    [InlineData("evil.example", HttpStatusCode.BadRequest)]
    [InlineData("example.org", HttpStatusCode.BadRequest)]
    public async Task Set_AnswersOnlyTheNamesListed_AndTheLocalOnes(string host, HttpStatusCode expected)
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\nallowed_hosts = [\"fetchshelf.lan\", \"*.example.org\"]\n");

        Assert.Equal(expected, await GetWithHostAsync(fixture, host));
    }

    [Fact]
    public async Task NotSet_AnswersAnyHost()
    {
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"\"\n");

        Assert.Equal(HttpStatusCode.OK, await GetWithHostAsync(fixture, "evil.example"));
    }

    [Theory]
    [InlineData("0.0.0.0", "fetchshelf.lan;localhost;127.0.0.1;[::1]")]
    [InlineData("192.168.1.5", "fetchshelf.lan;localhost;127.0.0.1;[::1];192.168.1.5")]
    [InlineData("fd00::5", "fetchshelf.lan;localhost;127.0.0.1;[::1];[fd00::5]")]
    public void For_AddsTheHostTheHealthProbeAsks(string webHost, string expected) =>
        Assert.Equal(expected, AllowedHosts.For(new FetchshelfSettings { WebHost = webHost, AllowedHosts = ["fetchshelf.lan"] }));

    [Fact]
    public void For_NothingSet_IsNull() => Assert.Null(AllowedHosts.For(new FetchshelfSettings()));
}
