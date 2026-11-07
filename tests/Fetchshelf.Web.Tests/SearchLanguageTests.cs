using System.Net;

namespace Fetchshelf.Web.Tests;

/// <summary>The Search page's language filter and its default from the top-level <c>languages</c>.</summary>
public class SearchLanguageTests
{
    private const string EmptySearchJson = """{ "perpage": 100, "start": 0, "total": 0, "found": 0, "data": [] }""";

    /// <summary>A MAM that answers every search with no results, and keeps the body of each search request.</summary>
    private static (Func<HttpRequestMessage, HttpResponseMessage> Handler, List<string> Searches) RecordingMam()
    {
        var searches = new List<string>();
        return (request =>
        {
            if (request.RequestUri!.AbsolutePath.Contains("loadSearchJSONbasic.php"))
            {
                searches.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(EmptySearchJson) };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }, searches);
    }

    private static string Config(string languages) => $"mam_id = \"\"\nlanguages = {languages}\n";

    [Fact]
    public async Task Results_WithoutALanguageChoice_SearchTheTopLevelLanguages()
    {
        var (handler, searches) = RecordingMam();
        using var fixture = new FetchshelfWebAppFixture(configToml: Config("[\"English\", \"German\"]"), mamHandler: handler);
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.Contains("\"browse_lang\":[1,37]", Assert.Single(searches));
    }

    [Fact]
    public async Task Results_AnyLanguage_SearchesWithoutALanguage()
    {
        var (handler, searches) = RecordingMam();
        using var fixture = new FetchshelfWebAppFixture(configToml: Config("[\"English\"]"), mamHandler: handler);
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/?handler=Results&Q=kings&Lang=any");

        Assert.DoesNotContain("browse_lang", Assert.Single(searches));
    }

    [Fact]
    public async Task Results_OneLanguage_SearchesOnlyThatLanguage()
    {
        var (handler, searches) = RecordingMam();
        using var fixture = new FetchshelfWebAppFixture(configToml: Config("[\"English\"]"), mamHandler: handler);
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/?handler=Results&Q=kings&Lang=French");

        Assert.Contains("\"browse_lang\":[36]", Assert.Single(searches));
    }

    [Fact]
    public async Task Results_NoTopLevelLanguages_SearchesWithoutALanguage()
    {
        var (handler, searches) = RecordingMam();
        using var fixture = new FetchshelfWebAppFixture(mamHandler: handler);
        using var client = fixture.CreateClient();

        await client.GetStringAsync("/?handler=Results&Q=kings");

        Assert.DoesNotContain("browse_lang", Assert.Single(searches));
    }

    [Fact]
    public async Task Page_PreSelectsTheTopLevelLanguages_AndOffersAnyLanguage()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config("[\"English\", \"German\"]"), mamHandler: RecordingMam().Handler);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<option value=\"\" selected=\"selected\">Your languages (English, German)</option>", html);
        Assert.Contains("<option value=\"any\">Any language</option>", html);
        Assert.Contains("<option value=\"BrazilianPortuguese\">Brazilian Portuguese</option>", html);
        Assert.Contains("Newest on MAM in English, German", html);
    }

    [Fact]
    public async Task Page_KeepsTheChosenLanguageSelected_AndPassesItToTheResults()
    {
        using var fixture = new FetchshelfWebAppFixture(configToml: Config("[\"English\"]"), mamHandler: RecordingMam().Handler);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/?Q=kings&Lang=French");

        Assert.Contains("<option value=\"French\" selected=\"selected\">French</option>", html);
        Assert.Contains("Lang=French", html);
        Assert.Contains("Matches for 'kings' in French", html);
    }

    [Fact]
    public async Task Page_NoTopLevelLanguages_DefaultsToAnyLanguage()
    {
        using var fixture = new FetchshelfWebAppFixture(mamHandler: RecordingMam().Handler);
        using var client = fixture.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<option value=\"\" selected=\"selected\">Any language</option>", html);
        Assert.DoesNotContain("value=\"any\"", html);
        Assert.Contains("<h3>Newest on MAM</h3>", html);
    }
}
