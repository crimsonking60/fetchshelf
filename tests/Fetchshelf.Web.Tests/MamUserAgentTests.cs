using System.Net;

namespace Fetchshelf.Web.Tests;

public class MamUserAgentTests
{
    [Fact]
    public async Task Search_SendsTheRunningVersionAsUserAgent()
    {
        string? userAgent = null;
        using var fixture = new FetchshelfWebAppFixture(mamHandler: request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"perpage":0,"start":0,"total":0,"found":0,"data":[]}"""),
            };
        });

        await fixture.CreateClient().GetAsync("/?handler=Results&Q=kings");

        Assert.Equal($"Fetchshelf/{AppVersion.Current}", userAgent);
    }
}
