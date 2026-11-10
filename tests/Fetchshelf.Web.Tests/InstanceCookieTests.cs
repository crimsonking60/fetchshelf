using System.Net;
using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// Two installations on one host share browser cookies, since browsers ignore the port. Their encrypted cookies are
/// named after each installation's own id, so neither can overwrite the other's.
/// </summary>
public partial class InstanceCookieTests
{
    [Fact]
    public void LoadOrCreate_NoIdYet_CreatesOneAndKeepsIt()
    {
        var directory = Directory.CreateTempSubdirectory("fetchshelf-instance-id-tests-").FullName;
        try
        {
            var first = InstanceId.LoadOrCreate(directory);
            var second = InstanceId.LoadOrCreate(directory);

            Assert.Matches("^[0-9a-f]{8}$", first);
            Assert.Equal(first, second);
            Assert.Equal(first, File.ReadAllText(Path.Combine(directory, InstanceId.FileName)).Trim());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an id; with spaces")]
    public void LoadOrCreate_UnusableStoredId_IsReplaced(string stored)
    {
        var directory = Directory.CreateTempSubdirectory("fetchshelf-instance-id-tests-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, InstanceId.FileName), stored);

            Assert.Matches("^[0-9a-f]{8}$", InstanceId.LoadOrCreate(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Get_FormPage_SetsAnAntiforgeryCookieNamedAfterTheInstallation()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/Torrents");

        var id = File.ReadAllText(Path.Combine(fixture.DataDirectory, InstanceId.FileName)).Trim();
        var cookies = SetCookies(response);
        Assert.Contains(cookies, c => c.StartsWith($".Fetchshelf.Antiforgery.{id}=", StringComparison.Ordinal));
        Assert.DoesNotContain(cookies, c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_WithAMessageForTheNextPage_SetsATempDataCookieNamedAfterTheInstallation()
    {
        using var fixture = new FetchshelfWebAppFixture();
        var client = fixture.CreateNoRedirectClient();
        var token = AntiforgeryToken(await (await client.GetAsync("/Torrents")).Content.ReadAsStringAsync());

        var response = await client.PostAsync("/Torrents?handler=Bulk", BulkSetCategoryWithoutCategory(token));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var id = File.ReadAllText(Path.Combine(fixture.DataDirectory, InstanceId.FileName)).Trim();
        Assert.Contains(SetCookies(response), c => c.StartsWith($".Fetchshelf.TempData.{id}=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_WhileTheBrowserAlsoHoldsAnotherInstallationsCookies_IsStillAccepted()
    {
        using var mine = new FetchshelfWebAppFixture();
        var myPage = await mine.CreateNoRedirectClient().GetAsync("/Torrents");
        var token = AntiforgeryToken(await myPage.Content.ReadAsStringAsync());
        var myCookie = CookiePair(SetCookies(myPage).Single(c => c.StartsWith(".Fetchshelf.Antiforgery.", StringComparison.Ordinal)));

        string otherCookie;
        using (var other = new FetchshelfWebAppFixture())
        {
            var otherPage = await other.CreateNoRedirectClient().GetAsync("/Torrents");
            otherCookie = CookiePair(SetCookies(otherPage).Single(c => c.StartsWith(".Fetchshelf.Antiforgery.", StringComparison.Ordinal)));
        }

        Assert.NotEqual(myCookie.Split('=')[0], otherCookie.Split('=')[0]);

        var client = mine.CreateNoRedirectClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/Torrents?handler=Bulk") { Content = BulkSetCategoryWithoutCategory(token) };
        request.Headers.Add("Cookie", $"{myCookie}; {otherCookie}");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>Set category with no category chosen: accepted by the form check, and changes nothing.</summary>
    private static FormUrlEncodedContent BulkSetCategoryWithoutCategory(string token) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        ["action"] = "setCategory",
        ["category"] = "",
    });

    private static List<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

    private static string CookiePair(string setCookie) => setCookie.Split(';')[0];

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""")]
    private static partial Regex AntiforgeryTokenPattern();

    private static string AntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
