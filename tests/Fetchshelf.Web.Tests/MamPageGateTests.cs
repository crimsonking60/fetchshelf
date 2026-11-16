using System.Net;
using System.Text.RegularExpressions;
using Fetchshelf.Clients.Mam;
using Fetchshelf.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// The Search page and the search result page take the MAM gate: while a job holds it, they wait a moment, then say
/// which job is busy, without asking MAM anything or selecting anything.
/// </summary>
public sealed partial class MamPageGateTests
{
    private const string SearchJson = """
        {
            "perpage": 100, "start": 0, "total": 1, "found": 1,
            "data": [
                {
                    "id": 1, "title": "The Way of Kings", "author_info": "{\"1\":\"Brandon Sanderson\"}",
                    "narrator_info": "{}", "series_info": "{}", "isbn": null, "description": null,
                    "mediatype": 1, "main_cat": 13, "maincat": 1, "category": 39, "categories": "[13]",
                    "language": 1, "browseflags": 0, "filetype": "m4b", "numfiles": 1, "size": "10 MiB",
                    "tags": "", "owner": 1, "owner_name": "uploader", "added": "2026-01-01 00:00:00",
                    "seeders": 5, "leechers": 1, "times_completed": 1,
                    "free": true, "personal_freeleech": false, "fl_vip": false, "vip": false,
                    "vip_expire": 0, "dl": "dlhash1"
                }
            ]
        }
        """;

    private sealed class CountingMam
    {
        private int _requests;

        public int Requests => _requests;

        public HttpResponseMessage Handle(HttpRequestMessage request)
        {
            Interlocked.Increment(ref _requests);
            return request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchJson) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static FetchshelfWebAppFixture Fixture(CountingMam mam) => new(
        "mam_id = \"test-mam-id\"\n",
        mamHandler: mam.Handle,
        configureServices: services => services.AddSingleton(sp =>
            new MamPageGate(sp.GetRequiredService<MamAccessGate>(), TimeSpan.FromMilliseconds(50))));

    private static async Task<IDisposable> HoldTheGateAsync(FetchshelfWebAppFixture fixture) =>
        await fixture.CreateScope().ServiceProvider.GetRequiredService<MamAccessGate>().AcquireAsync("Autograb Freeleech");

    private static async Task<string> TokenAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/Torrents");
        return ValueAttribute().Match(AntiforgeryInput().Match(html).Value).Groups[1].Value;
    }

    private static async Task<string> PostAsync(HttpClient client, string handler, Dictionary<string, string> form)
    {
        form["__RequestVerificationToken"] = await TokenAsync(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/?handler={handler}") { Content = new FormUrlEncodedContent(form) };
        request.Headers.Add("HX-Request", "true");
        return await (await client.SendAsync(request)).Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Search_GateHeldByAJob_SaysWhichJob_AndAsksMamNothing()
    {
        var mam = new CountingMam();
        using var fixture = Fixture(mam);
        using var client = fixture.CreateClient();
        using var held = await HoldTheGateAsync(fixture);

        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("id=\"search-busy\"", html);
        Assert.Contains("MAM is busy with Autograb Freeleech; try again in a moment.", html);
        Assert.Contains("Try again", html);
        Assert.Equal(0, mam.Requests);
    }

    [Fact]
    public async Task Search_GateFree_Searches_AndLetsGoOfTheGate()
    {
        var mam = new CountingMam();
        using var fixture = Fixture(mam);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("The Way of Kings", html);
        Assert.DoesNotContain("search-busy", html);
        var gate = fixture.CreateScope().ServiceProvider.GetRequiredService<MamAccessGate>();
        Assert.Null(gate.Owner);
        using var free = await gate.TryAcquireAsync("test", TimeSpan.Zero);
        Assert.NotNull(free);
    }

    [Fact]
    public async Task Download_GateHeldByAJob_SelectsNothing_AndSaysWhy()
    {
        var mam = new CountingMam();
        using var fixture = Fixture(mam);
        using var client = fixture.CreateClient();
        await client.GetStringAsync("/Torrents");
        using var held = await HoldTheGateAsync(fixture);

        var body = await PostAsync(client, "Download", new() { ["mamId"] = "1" });

        Assert.Contains("class=\"notice error\"", body);
        Assert.Contains("Not downloaded: MAM is busy with Autograb Freeleech; try again in a moment.", body);
        Assert.Equal(0, mam.Requests);
        using var scope = fixture.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().SelectedTorrents);
    }

    [Fact]
    public async Task Import_GateHeldByAJob_ImportsNothing_AndSaysWhy()
    {
        var mam = new CountingMam();
        using var fixture = Fixture(mam);
        using var client = fixture.CreateClient();
        await client.GetStringAsync("/Torrents");
        using var held = await HoldTheGateAsync(fixture);

        var body = await PostAsync(client, "Import", new() { ["mamId"] = "1" });

        Assert.Contains("Not imported: MAM is busy with Autograb Freeleech; try again in a moment.", body);
        Assert.Equal(0, mam.Requests);
        using var scope = fixture.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Torrents);
    }

    [Fact]
    public async Task ResultPage_GateHeldByAJob_SaysWhichJob_WithAReloadLink()
    {
        var mam = new CountingMam();
        using var fixture = Fixture(mam);
        using var client = fixture.CreateClient();
        using var held = await HoldTheGateAsync(fixture);

        var html = await client.GetStringAsync("/search/1?Q=kings");

        Assert.Contains("id=\"hit-busy\"", html);
        Assert.Contains("MAM is busy with Autograb Freeleech; try again in a moment.", html);
        Assert.Contains("href=\"/search/1?Q=kings\">Try again</a>", html);
        Assert.Equal(0, mam.Requests);
    }

    [Fact]
    public async Task ResultDialog_GateHeldByAJob_HasATryAgainButtonForTheDialog()
    {
        var mam = new CountingMam();
        using var fixture = Fixture(mam);
        using var client = fixture.CreateClient();
        using var held = await HoldTheGateAsync(fixture);

        var html = await client.GetStringAsync("/search/1?handler=Partial");

        Assert.Contains("MAM is busy with Autograb Freeleech", html);
        Assert.Contains("hx-target=\"#hit-dialog-body\"", html);
        Assert.Equal(0, mam.Requests);
    }

    [Fact]
    public void Wait_IsTenSecondsByDefault()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), new MamPageGate(new MamAccessGate()).Wait);
    }

    [GeneratedRegex(@"<input[^>]*name=""__RequestVerificationToken""[^>]*>")]
    private static partial Regex AntiforgeryInput();

    [GeneratedRegex(@"value=""([^""]*)""")]
    private static partial Regex ValueAttribute();
}
