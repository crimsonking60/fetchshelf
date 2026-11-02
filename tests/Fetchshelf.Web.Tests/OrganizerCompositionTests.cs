using System.Text.RegularExpressions;

namespace Fetchshelf.Web.Tests;

/// <summary>
/// Links a fake-metadata-bearing finished torrent end to end through the real host -
/// config, the real "Link" job, the library-organizer orchestration, and the Torrents/detail/Events
/// pages - then exercises refresh-and-relink and confirms the result is visible everywhere.
/// </summary>
public partial class OrganizerCompositionTests
{
    private const string Hash = "composition-hash";

    [Fact]
    public async Task EndToEnd_LinkThenRefreshAndRelink_VisibleAcrossPages()
    {
        var tempDir = Directory.CreateTempSubdirectory("fetchshelf-organizer-composition-").FullName;
        try
        {
            var downloadsDir = Path.Combine(tempDir, "downloads");
            var libraryDir = Path.Combine(tempDir, "library");
            Directory.CreateDirectory(downloadsDir);
            File.WriteAllText(Path.Combine(downloadsDir, "book.m4b"), "data");

            var metadataLookup = new FakeMetadataLookup(OrganizerTestSupport.BuildMetadata("Old Title"));
            var configToml = $"""
                mam_id = "test-mam-id"

                [[qbittorrent]]
                url = "http://qbittorrent.test"
                username = "admin"
                password = "adminadmin"

                [[library]]
                library_dir = "{libraryDir.Replace("\\", "\\\\")}"
                category = "audiobooks"
                """;
            using var fixture = new FetchshelfWebAppFixture(
                configToml, metadataLookup,
                OrganizerTestSupport.FakeQBittorrentServer(Hash, downloadsDir, ["book.m4b"]));
            var client = fixture.CreateClient();

            var jobsHtml = await (await client.GetAsync("/Jobs")).Content.ReadAsStringAsync();
            var token = ExtractAntiforgeryToken(jobsHtml);
            using var triggerForm = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["jobName"] = "Link",
                ["__RequestVerificationToken"] = token,
            });
            (await client.PostAsync("/Jobs?handler=Trigger", triggerForm)).EnsureSuccessStatusCode();

            var listHtml = await (await client.GetAsync("/torrents")).Content.ReadAsStringAsync();
            Assert.Contains("Old Title", listHtml);

            var detailHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
            Assert.Contains("Old Title", detailHtml);
            var oldFolder = Path.Combine(libraryDir, "Brandon Sanderson", "Old Title");
            Assert.Contains(oldFolder, detailHtml);
            Assert.True(File.Exists(Path.Combine(oldFolder, "book.m4b")));

            var eventsHtml = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();
            Assert.Contains("Linked", eventsHtml);

            metadataLookup.Result = OrganizerTestSupport.BuildMetadata("New Title");
            var detailTokenHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
            var refreshToken = ExtractAntiforgeryToken(detailTokenHtml);
            using var refreshForm = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = refreshToken });
            (await client.PostAsync($"/torrents/{Hash}?handler=RefreshAndRelink", refreshForm)).EnsureSuccessStatusCode();

            var newFolder = Path.Combine(libraryDir, "Brandon Sanderson", "New Title");
            Assert.True(File.Exists(Path.Combine(newFolder, "book.m4b")));
            Assert.False(Directory.Exists(oldFolder));

            var updatedDetailHtml = await (await client.GetAsync($"/torrents/{Hash}")).Content.ReadAsStringAsync();
            Assert.Contains("New Title", updatedDetailHtml);

            var updatedEventsHtml = await (await client.GetAsync("/events")).Content.ReadAsStringAsync();
            Assert.Contains("Updated", updatedEventsHtml);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""")]
    private static partial Regex AntiforgeryTokenPattern();

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenPattern().Match(html);
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }
}
