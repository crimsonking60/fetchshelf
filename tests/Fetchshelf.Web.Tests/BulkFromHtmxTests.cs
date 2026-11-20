using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// A bulk action sent by htmx (the page's script) answers with only what changed: the progress panel for a queued action,
/// the list for Remove selected. The list keeps its ticks and its scroll, so the strip at the bottom shows the progress.
/// </summary>
public partial class BulkFromHtmxTests
{
    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""")]
    private static partial Regex TokenPattern();

    private static string Token(string html)
    {
        var match = TokenPattern().Match(html);
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private static async Task<HttpResponseMessage> PostBulkAsync(HttpClient client, bool htmx, params (string Name, string Value)[] fields)
    {
        var page = await client.GetStringAsync("/torrents");
        var form = fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Value))
            .Append(new("__RequestVerificationToken", Token(page)));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/torrents?handler=Bulk") { Content = new FormUrlEncodedContent(form) };
        if (htmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Get_Torrents_SendsTheBulkFormWithHtmx_ToThePanel()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("First Book", ["An Author"]));

        var html = await fixture.CreateClient().GetStringAsync("/torrents");

        Assert.Matches(@"<form method=""post"" hx-post=""/Torrents\?handler=Bulk"" hx-target=""#bulk-operations"" hx-swap=""outerHTML""", html);
    }

    [Fact]
    public async Task Post_QueuedActionFromHtmx_AnswersWithThePanel_NotARedirect()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("First Book", ["An Author"]));
        var client = fixture.CreateNoRedirectClient();

        var response = await PostBulkAsync(client, htmx: true, ("selectedIds", "hash1"), ("action", "clean"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("<div id=\"bulk-operations\"", body.Trim());
        Assert.DoesNotContain("<html", body);
    }

    [Fact]
    public async Task Post_RemoveFromHtmx_AnswersWithTheListWithoutTheRows_InPlaceOfTheOldList()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("First Book", ["An Author"]));
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash2", OrganizerTestSupport.BuildMetadata("Second Book", ["An Author"]));
        var client = fixture.CreateNoRedirectClient();

        var response = await PostBulkAsync(client, htmx: true, ("selectedIds", "hash1"), ("action", "removeRecord"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("#torrent-list-body", response.Headers.GetValues("HX-Retarget").Single());
        Assert.Equal("innerHTML", response.Headers.GetValues("HX-Reswap").Single());
        Assert.DoesNotContain("First Book", body);
        Assert.Contains("Second Book", body);
    }

    [Fact]
    public async Task Post_SetCategoryWithoutOneFromHtmx_ReloadsThePage_ToSayWhy()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateNoRedirectClient();

        var response = await PostBulkAsync(client, htmx: true, ("action", "setCategory"), ("category", ""));

        Assert.Equal("true", response.Headers.GetValues("HX-Refresh").Single());
        Assert.Contains("Choose a category first.", await client.GetStringAsync("/torrents"));
    }

    [Fact]
    public async Task Post_WithoutHtmx_StillRedirectsBackToTheList()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("First Book", ["An Author"]));
        var client = fixture.CreateNoRedirectClient();

        var response = await PostBulkAsync(client, htmx: false, ("selectedIds", "hash1"), ("action", "clean"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Script_FocusesTheConfirmedButtonBeforeSending_SoHtmxSendsItsValue()
    {
        using var fixture = new FetchshelfWebAppFixture();
        using var client = fixture.CreateClient();
        var page = await client.GetStringAsync("/torrents");

        var script = await client.GetStringAsync(Regex.Match(page, "src=\"(/js/confirm[^\"]*)\"").Groups[1].Value);

        Assert.Matches(@"button\.focus\(\);\s*button\.form\.requestSubmit\(button\);", script);
    }
}
