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
    public async Task WhileACheckRuns_SaysTheFindingsAreThePreviousChecks_AndRefusesToActOnThem()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        var folder = MakeFolder("Author/Old");
        Results(fixture).Set([new LibraryFinding(LibraryFindingKind.UnknownFolder, folder, "Audiobooks")], DateTimeOffset.UtcNow);
        var started = new TaskCompletionSource();
        fixture.JobRegistry.Register(Fetchshelf.Data.Configuration.JobNames.LibraryCheck, async ct =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
        });
        var run = fixture.JobRegistry.TriggerAsync(Fetchshelf.Data.Configuration.JobNames.LibraryCheck);
        await started.Task;

        var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/LibraryCheck"));
        var afterDelete = await PostAsync(client, "Delete", Results(fixture).Findings[0].Id);
        var status = await client.GetAsync("/LibraryCheck?handler=Status");

        Assert.Contains("Until then, the findings below are from the previous check", page);
        Assert.Contains("hx-get=\"?handler=Status\"", page);
        Assert.Contains("<button type=\"submit\" class=\"danger\" disabled=\"disabled\">Delete selected</button>", page);
        Assert.Contains("The Library check is running, so these are the previous check's findings.", afterDelete);
        Assert.True(Directory.Exists(folder));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, status.StatusCode);

        fixture.JobRegistry.Cancel(Fetchshelf.Data.Configuration.JobNames.LibraryCheck);
        await run;
        var ended = await client.GetAsync("/LibraryCheck?handler=Status");
        Assert.Equal("true", ended.Headers.GetValues("HX-Refresh").Single());
    }

    [Fact]
    public async Task RunTheCheck_AQuickCheck_ComesBackWithItsFindings_NotSayingItRuns()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        var results = Results(fixture);
        fixture.JobRegistry.Register(Fetchshelf.Data.Configuration.JobNames.LibraryCheck, _ =>
        {
            results.Set([new LibraryFinding(LibraryFindingKind.UnknownFolder, "/lib/Author/Old", "Audiobooks")], DateTimeOffset.UtcNow);
            return Task.CompletedTask;
        });

        var html = await PostAsync(client, "Run");

        Assert.Contains("/lib/Author/Old", html);
        Assert.Contains("Last checked", html);
        Assert.DoesNotContain("running", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunTheCheck_ALongCheck_ComesBackSayingItRuns()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        fixture.JobRegistry.Register(Fetchshelf.Data.Configuration.JobNames.LibraryCheck, ct => Task.Delay(Timeout.Infinite, ct));

        var html = await PostAsync(client, "Run");

        Assert.Contains("The check is running; the page shows what it finds when it ends.", html);
        Assert.DoesNotContain("Until then", html);
        fixture.JobRegistry.Cancel(Fetchshelf.Data.Configuration.JobNames.LibraryCheck);
    }

    [Fact]
    public async Task Delete_FindingsANewerCheckReplaced_DoesNothingAndSaysSo()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        var folder = MakeFolder("Author/Old");
        var finding = new LibraryFinding(LibraryFindingKind.UnknownFolder, folder, "Audiobooks");
        Results(fixture).Set([finding], DateTimeOffset.UtcNow);
        var oldId = Results(fixture).Findings[0].Id;
        var html = await client.GetStringAsync("/LibraryCheck");
        Results(fixture).Set([finding], DateTimeOffset.UtcNow); // a newer check, with new ids

        using var content = new FormUrlEncodedContent([new("selectedIds", oldId.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("__RequestVerificationToken", Token(html))]);
        var answer = System.Net.WebUtility.HtmlDecode(await (await client.PostAsync("/LibraryCheck?handler=Delete", content)).Content.ReadAsStringAsync());

        Assert.Contains("1 of the ticked findings is from an earlier check", answer);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Relink_ABookItCannotRelink_StaysWithTheReason()
    {
        using var fixture = Fixture();
        var client = fixture.CreateClient();
        await client.GetAsync("/Jobs"); // starts the host, which creates the database
        var folder = Path.Combine(LibraryDir, "Author/Gone");
        await OrganizerTestSupport.SeedTorrentAsync(fixture, "hash1", libraryPath: folder);
        Results(fixture).Set(
            [new LibraryFinding(LibraryFindingKind.FolderMissing, folder, "Audiobooks") { TorrentId = "hash1", Title = "Gone" }],
            DateTimeOffset.UtcNow);

        var html = await PostAsync(client, "Relink", Results(fixture).Findings[0].Id);

        Assert.Contains("Relinked 0 of 1. 1 could not be", html);
        Assert.Contains("<div class=\"finding-note\">Not relinked: It is not in qBittorrent, so there is nothing to link from. Nothing was changed.</div>", html);
        Assert.Single(Results(fixture).Findings);
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
