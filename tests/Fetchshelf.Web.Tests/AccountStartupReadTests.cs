using System.Net;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// With a mam_id, Fetchshelf reads the account from MAM once when it starts, so the Jobs and Account pages have its
/// numbers from the start. The test fixture leaves this out unless asked, so other tests' MAM sees only their own calls.
/// </summary>
public class AccountStartupReadTests
{
    private static Func<HttpRequestMessage, HttpResponseMessage> AccountMam(Action onRead) => request =>
    {
        if (request.RequestUri!.AbsolutePath.Contains("jsonLoad.php"))
        {
            onRead();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(MamAccountFixture.Json) };
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
    };

    private static async Task WaitForAsync(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task WithAMamId_TheAccountIsReadOnceAtStartup_AndTheJobsPageHasItsNumbers()
    {
        var reads = 0;
        using var fixture = new FetchshelfWebAppFixture("mam_id = \"test-mam-id\"\n", mamHandler: AccountMam(() => Interlocked.Increment(ref reads)), readAccountAtStartup: true);
        var client = fixture.CreateClient();
        var mamClient = fixture.CreateScope().ServiceProvider.GetRequiredService<MamClient>();

        await WaitForAsync(() => mamClient.LastAccountState is not null);
        var html = await client.GetStringAsync("/Jobs");

        Assert.NotNull(mamClient.LastAccountState);
        Assert.Equal(1, reads);
        Assert.DoesNotContain("No account numbers read from MAM yet.", html);
        Assert.Contains("Unsatisfied 14 of 20", html);
    }

    [Fact]
    public async Task WithoutAMamId_NothingIsRead()
    {
        var reads = 0;
        using var fixture = new FetchshelfWebAppFixture(mamHandler: AccountMam(() => Interlocked.Increment(ref reads)), readAccountAtStartup: true);
        var client = fixture.CreateClient();

        await client.GetStringAsync("/Jobs");
        await Task.Delay(300);

        Assert.Equal(0, reads);
        Assert.Null(fixture.CreateScope().ServiceProvider.GetRequiredService<MamClient>().LastAccountState);
    }

    [Fact]
    public async Task AFailedRead_DoesNotStopFetchshelf()
    {
        using var fixture = new FetchshelfWebAppFixture(
            "mam_id = \"test-mam-id\"\n",
            mamHandler: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
            readAccountAtStartup: true);
        var client = fixture.CreateClient();
        await Task.Delay(300);

        var response = await client.GetAsync("/Jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No account numbers read from MAM yet.", await response.Content.ReadAsStringAsync());
    }
}
