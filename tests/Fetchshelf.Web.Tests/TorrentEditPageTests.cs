using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>The manual metadata edit page.</summary>
public partial class TorrentEditPageTests
{
    [Fact]
    public async Task Post_EditSubmission_UpdatesStoredMetadataAndSourceBecomesManual()
    {
        using var fixture = new FetchshelfWebAppFixture();
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", OrganizerTestSupport.BuildMetadata("Original Title"));
        var client = fixture.CreateClient();

        var editHtml = await (await client.GetAsync("/torrents/hash1/edit")).Content.ReadAsStringAsync();
        var token = ExtractAntiforgeryToken(editHtml);

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "New Title",
            ["Input.Authors"] = "Jane Doe",
            ["Input.Narrators"] = "",
            ["Input.Series"] = "",
            ["Input.Language"] = "English",
            ["Input.Category"] = "AudiobookGeneralFiction",
            ["__RequestVerificationToken"] = token,
        });
        var response = await client.PostAsync("/torrents/hash1/edit", form);
        response.EnsureSuccessStatusCode();

        var detailHtml = await (await client.GetAsync("/torrents/hash1")).Content.ReadAsStringAsync();
        Assert.Contains("New Title", detailHtml);
        Assert.Contains("Jane Doe", detailHtml);
        Assert.Contains("Manual", detailHtml);
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""")]
    private static partial Regex AntiforgeryTokenPattern();

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
