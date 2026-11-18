using System.Text.RegularExpressions;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

public sealed class LibraryCheckPageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-library-check-page-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string LibraryDir => Path.Combine(_root, "library");

    private FetchshelfWebAppFixture Fixture() => new($"""
        mam_id = ""

        [[library]]
        name = "Audiobooks"
        category = "audiobooks"
        library_dir = "{LibraryDir}"
        """);

    private static LibraryCheckResults Results(FetchshelfWebAppFixture fixture) =>
        fixture.CreateScope().ServiceProvider.GetRequiredService<LibraryCheckResults>();

    private static string Token(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""__RequestVerificationToken""");
        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
    }

    private static async Task<string> PostAsync(HttpClient client, string handler, params int[] ids)
    {
        var html = await client.GetStringAsync("/LibraryCheck");
        var form = ids.Select(id => new KeyValuePair<string, string>("selectedIds", id.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .Append(new("__RequestVerificationToken", Token(html)));
        using var content = new FormUrlEncodedContent(form);
        // The redirect after the post is followed: the page it comes back to carries the message once.
        var response = await client.PostAsync($"/LibraryCheck?handler={handler}", content);
        return System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private string MakeFolder(string relative)
    {
        var path = Path.Combine(LibraryDir, relative);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "book.m4b"), "x");
        return path;
    }

    [Fact]
    public async Task BeforeACheck_SaysSo_AndTheMenuHasNoEntry()
    {
        using var fixture = Fixture();

        var html = await fixture.CreateClient().GetStringAsync("/LibraryCheck");

        Assert.Contains("Not checked since Fetchshelf started.", html);
        Assert.DoesNotContain("library-check-badge", html);
    }

    [Fact]
    public async Task WithFindings_ListsThem_AndTheMenuShowsHowMany()
    {
        using var fixture = Fixture();
        Results(fixture).Set(
            [
                new LibraryFinding(LibraryFindingKind.UnknownFolder, "/lib/Author/Old", "Audiobooks") { Files = 2, Bytes = 2048 },
                new LibraryFinding(LibraryFindingKind.FilesMissing, "/lib/Author/Book", "Audiobooks") { TorrentId = "hash1", Title = "The Book", MissingFiles = ["02.mp3"] },
            ],
            DateTimeOffset.UtcNow);

        var html = System.Net.WebUtility.HtmlDecode(await fixture.CreateClient().GetStringAsync("/LibraryCheck"));

        Assert.Contains("/lib/Author/Old (2 files)", html);
        Assert.Contains(">The Book</a>", html);
        Assert.Contains("1 file (02.mp3)", html);
        Assert.Matches("<span id=\"library-check-badge\"[^>]*>2</span>", html);
    }

    [Fact]
    public async Task Delete_RemovesTheFolder_TheFinding_AndRecordsAnEvent()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        var folder = MakeFolder("Author/Old");
        Results(fixture).Set([new LibraryFinding(LibraryFindingKind.UnknownFolder, folder, "Audiobooks")], DateTimeOffset.UtcNow);

        var html = await PostAsync(client, "Delete", Results(fixture).Findings[0].Id);

        Assert.False(Directory.Exists(Path.Combine(LibraryDir, "Author")));
        Assert.Empty(Results(fixture).Findings);
        Assert.Contains("Deleted 1.", html);
        using var scope = fixture.CreateScope();
        Assert.Equal(folder, (await scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Events.SingleAsync(e => e.Kind == EventKind.DeletedFromLibrary)).LibraryPath);
    }

    [Fact]
    public async Task Delete_AFolderABookUsesNow_IsRefusedAndKept()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs"); // starts the host, which creates the database
        var folder = MakeFolder("Author/Old");
        Results(fixture).Set([new LibraryFinding(LibraryFindingKind.UnknownFolder, folder, "Audiobooks")], DateTimeOffset.UtcNow);
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: folder);

        var html = await PostAsync(client, "Delete", Results(fixture).Findings[0].Id);

        Assert.True(Directory.Exists(folder));
        Assert.Contains("A book is linked in", html);
        Assert.Single(Results(fixture).Findings);
    }

    [Fact]
    public async Task RemoveRecord_ForgetsTheBook_AndTheFinding()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs");
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: Path.Combine(LibraryDir, "Author/Gone"));
        Results(fixture).Set(
            [new LibraryFinding(LibraryFindingKind.FolderMissing, Path.Combine(LibraryDir, "Author/Gone"), "Audiobooks") { TorrentId = "hash1", Title = "Gone" }],
            DateTimeOffset.UtcNow);

        await PostAsync(client, "RemoveRecord", Results(fixture).Findings[0].Id);

        using var scope = fixture.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>().Torrents);
        Assert.Empty(Results(fixture).Findings);
    }

    [Fact]
    public async Task JobsPage_LinksTheJobToItsFindings()
    {
        using var fixture = new FetchshelfWebAppFixture();
        fixture.JobRegistry.Register(Fetchshelf.Data.Configuration.JobNames.LibraryCheck, _ => Task.CompletedTask);

        var html = await fixture.CreateClient().GetStringAsync("/Jobs");

        Assert.Matches("<a id=\"library-check-findings\" href=\"/LibraryCheck\">Findings</a>", html);
    }
}
