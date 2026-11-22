using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.Extensions.DependencyInjection;

namespace Fetchshelf.Web.Tests;

/// <summary>The Books page, its book dialog, and its covers and downloads, served from real library folders.</summary>
public sealed class BooksPageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-books-page-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Ebooks => Path.Combine(_root, "ebooks");

    private string Audiobooks => Path.Combine(_root, "audiobooks");

    private string Config(string extra = "") => $"""
        mam_id = ""

        [[library]]
        name = "Ebooks"
        category = "ebooks"
        library_dir = "{Ebooks}"

        [[library]]
        name = "Audiobooks"
        category = "audiobooks"
        library_dir = "{Audiobooks}"
        {extra}
        """;

    private const string Opds = "\n[opds]\n";

    /// <summary>Puts a book's files in its library folder and records the torrent the way the Link job would.</summary>
    private async Task SeedAsync(
        FetchshelfWebAppFixture fixture, string hash, string title, MediaType mediaType, string[] files, string author = "Ann Leckie",
        Series? series = null, string? absId = null, DateTimeOffset? createdAt = null, string? description = null, string[]? narrators = null,
        string[]? authors = null)
    {
        var library = mediaType == MediaType.Audiobook ? Audiobooks : Ebooks;
        var folder = Path.Combine(library, author, title);
        foreach (var file in files)
        {
            var path = Path.Combine(folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"contents of {file}");
        }

        string? Chosen(params string[] extensions) =>
            files.Select(file => Path.GetExtension(file).TrimStart('.')).FirstOrDefault(extensions.Contains);
        var metadata = OrganizerTestSupport.BuildMetadata(title, authors ?? [author]) with
        {
            MediaType = mediaType,
            Series = series is null ? [] : [series],
            Description = description,
            Narrators = narrators ?? [],
        };
        using var scope = fixture.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
        dbContext.Torrents.Add(new TorrentEntity
        {
            Id = hash,
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize(title),
            Title = title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            LibraryPath = folder,
            ChosenAudioExtension = Chosen("m4b", "mp3"),
            ChosenEbookExtension = Chosen("epub", "pdf"),
            AbsId = absId,
            LinkedFilesJson = JsonSerializer.Serialize(files.Select(file => new LinkedFileRecord(file, new FileInfo(Path.Combine(folder, file)).Length)).ToList()),
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task<FetchshelfWebAppFixture> LibraryAsync(string extra = "")
    {
        var fixture = new FetchshelfWebAppFixture(Config(extra));
        var day = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(fixture, "justice", "Ancillary Justice", MediaType.Ebook, ["Ancillary Justice.epub"],
            series: new Series { Name = "Imperial Radch", Entries = [SeriesEntry.ForNumber(1, "1")] }, createdAt: day);
        await SeedAsync(fixture, "sword", "Ancillary Sword", MediaType.Audiobook, ["Ancillary Sword.m4b", "Ancillary Sword.pdf"],
            series: new Series { Name = "Imperial Radch", Entries = [SeriesEntry.ForNumber(2, "2")] }, absId: "li_sword", createdAt: day.AddDays(1),
            description: "<strong>Narrated by Adjoa Andoh</strong><br />The second book.", narrators: ["Adjoa Andoh", "Celeste Ciulla"]);
        await SeedAsync(fixture, "dune", "Dune", MediaType.Audiobook, ["Disc 2/02.mp3", "Disc 1/01.mp3"], author: "Frank Herbert", createdAt: day.AddDays(2));
        return fixture;
    }

    private static async Task<HttpResponseMessage> GetAsync(FetchshelfWebAppFixture fixture, string path, string? login = null, RangeHeaderValue? range = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (login is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(login)));
        }

        request.Headers.Range = range;
        return await fixture.CreateClient().SendAsync(request);
    }

    private static int IndexOf(string html, string text) =>
        html.IndexOf(text, StringComparison.Ordinal) is var index and >= 0 ? index : throw new Xunit.Sdk.XunitException($"'{text}' is not on the page");

    [Fact]
    public async Task RecentlyAdded_ListsEveryBook_NewestFirst_AndTheFormatFilterNarrowsIt()
    {
        using var fixture = await LibraryAsync();
        var client = fixture.CreateClient();

        var all = await client.GetStringAsync("/books");
        var audiobooks = await client.GetStringAsync("/books?Format=audiobooks");
        var ebooks = await client.GetStringAsync("/books?Format=ebooks");

        Assert.True(IndexOf(all, "Dune") < IndexOf(all, "Ancillary Sword"));
        Assert.True(IndexOf(all, "Ancillary Sword") < IndexOf(all, "Ancillary Justice"));
        Assert.Contains("Imperial Radch #2", all);
        Assert.Contains("M4B", all);
        Assert.Contains("Ancillary Sword", audiobooks);
        Assert.Contains("Dune", audiobooks);
        Assert.DoesNotContain("Ancillary Justice", audiobooks);
        // An audiobook's companion PDF doesn't make it an ebook.
        Assert.Contains("Ancillary Justice", ebooks);
        Assert.DoesNotContain("Ancillary Sword", ebooks);
    }

    [Fact]
    public async Task AuthorsSeriesAndLibraries_ListTheirNames_AndLeadToTheirBooks()
    {
        using var fixture = await LibraryAsync();
        var client = fixture.CreateClient();

        var authors = await client.GetStringAsync("/books?View=authors");
        var oneAuthor = await client.GetStringAsync("/books?Author=Frank%20Herbert");
        var series = await client.GetStringAsync("/books?View=series");
        var oneSeries = await client.GetStringAsync("/books?Series=Imperial%20Radch");
        var libraries = await client.GetStringAsync("/books?View=libraries");
        var audiobookLibrary = await client.GetStringAsync("/books?Library=1");

        Assert.Contains("Ann Leckie</a> <span class=\"muted\">2 books", authors);
        Assert.Contains("Frank Herbert</a> <span class=\"muted\">1 book<", authors);
        Assert.Contains("Dune", oneAuthor);
        Assert.DoesNotContain("Ancillary", oneAuthor);
        Assert.Contains("Imperial Radch</a> <span class=\"muted\">2 books", series);
        Assert.True(IndexOf(oneSeries, "Ancillary Justice") < IndexOf(oneSeries, "Ancillary Sword"));
        Assert.Contains("id=\"series-page-link\" href=\"/Series?Q=Imperial%20Radch&amp;Show=all\"", oneSeries);
        Assert.Contains("class=\"book-groups single-column\"", libraries);
        Assert.True(IndexOf(libraries, "Audiobooks</a>") < IndexOf(libraries, "Ebooks</a>"));
        Assert.DoesNotContain("single-column", authors);
        Assert.Contains("Ebooks</a> <span class=\"muted\">1 book<", libraries);
        Assert.Contains("Audiobooks</a> <span class=\"muted\">2 books", libraries);
        Assert.Contains("Dune", audiobookLibrary);
        Assert.DoesNotContain("Ancillary Justice", audiobookLibrary);
    }

    [Fact]
    public async Task ABookWithMoreThanThreeAuthors_ShowsThreeOnItsCard_AndMoreListsThemAll()
    {
        using var fixture = new FetchshelfWebAppFixture(Config());
        string[] five = ["Ann Leckie", "Martha Wells", "Becky Chambers", "Yoon Ha Lee", "Aliette de Bodard"];
        await SeedAsync(fixture, "anthology", "Starships", MediaType.Ebook, ["Starships.epub"], authors: five);
        await SeedAsync(fixture, "trio", "Three Hands", MediaType.Ebook, ["Three Hands.epub"], authors: five[..3]);

        var page = await fixture.CreateClient().GetStringAsync("/books");

        var start = IndexOf(page, ">Starships<");
        var card = page[start..page.IndexOf("</template>", start, StringComparison.Ordinal)];
        Assert.Contains("Ann Leckie, Martha Wells, Becky Chambers <button", card);
        Assert.Contains(">... more</button>", card);
        var template = card[IndexOf(card, "<template class=\"authors-all\">")..];
        Assert.All(five, author => Assert.Contains($"Author={Uri.EscapeDataString(author)}\">{author}</a>", template));
        // Three authors fit on the card: no "... more".
        Assert.Equal(1, page.Split("class=\"link-button authors-more\"").Length - 1);
        Assert.Contains("Ann Leckie, Martha Wells, Becky Chambers</span>", page);
    }

    [Fact]
    public async Task Search_MatchesTitleAuthorAndSeries()
    {
        using var fixture = await LibraryAsync();
        var client = fixture.CreateClient();

        var byAuthor = await client.GetStringAsync("/books?Q=herbert");
        var bySeries = await client.GetStringAsync("/books?Q=radch%20sword");
        var nothing = await client.GetStringAsync("/books?Q=nothing-like-this");

        Assert.Contains("Dune", byAuthor);
        Assert.DoesNotContain("Ancillary", byAuthor);
        Assert.Contains("Ancillary Sword", bySeries);
        Assert.DoesNotContain("Ancillary Justice", bySeries);
        Assert.Contains("id=\"no-books-message\"", nothing);
    }

    [Fact]
    public async Task EmptyLibrary_SaysSo()
    {
        using var fixture = new FetchshelfWebAppFixture(Config());

        var html = await fixture.CreateClient().GetStringAsync("/books");

        Assert.Contains("No books yet.", html);
    }

    [Fact]
    public async Task Detail_LinksToAudiobookshelfOnlyForAMatchedBook_AndAlwaysToTheTorrent()
    {
        using var fixture = await LibraryAsync("\n[audiobookshelf]\nurl = \"http://abs.local:13378/\"\ntoken = \"t\"\ninterval = 0\n");
        var client = fixture.CreateClient();

        var matched = await client.GetStringAsync("/books/sword?handler=Partial");
        var notMatched = await client.GetStringAsync("/books/justice");

        Assert.Contains("href=\"http://abs.local:13378/audiobookshelf/item/li_sword\"", matched);
        Assert.Contains("id=\"torrent-link\" href=\"/torrents/sword\"", matched);
        Assert.DoesNotContain("<html", matched);
        Assert.Contains("id=\"book-description\" class=\"hit-description\">Narrated by Adjoa Andoh", matched);
        Assert.DoesNotContain("&lt;strong&gt;", matched);
        Assert.Contains("<span class=\"value\" id=\"book-narrators\">Adjoa Andoh, Celeste Ciulla</span>", matched);
        Assert.DoesNotContain("id=\"book-narrators\"", notMatched);
        Assert.DoesNotContain("id=\"abs-link\"", notMatched);
        Assert.Contains("<html", notMatched);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAsync(fixture, "/books/no-such-book")).StatusCode);
    }

    [Fact]
    public async Task WithoutOpds_FilesAreListedButNotOffered()
    {
        using var fixture = await LibraryAsync();

        var detail = await fixture.CreateClient().GetStringAsync("/books/sword");
        using var download = await GetAsync(fixture, "/books/sword/audio/0");

        Assert.Contains("id=\"downloads-off\"", detail);
        Assert.Contains("Ancillary Sword.m4b", detail);
        Assert.DoesNotContain("/books/sword/audio/0", detail);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
    }

    [Fact]
    public async Task WithOpds_EachFileDownloads_WithRangesAndAReadableName()
    {
        using var fixture = await LibraryAsync(Opds);

        var detail = await fixture.CreateClient().GetStringAsync("/books/dune");
        using var part = await GetAsync(fixture, "/books/dune/audio/1");
        using var range = await GetAsync(fixture, "/books/sword/audio/0", range: new RangeHeaderValue(0, 7));
        using var pdf = await GetAsync(fixture, "/books/sword/ebook/0");
        using var pastTheEnd = await GetAsync(fixture, "/books/dune/audio/2");

        Assert.True(IndexOf(detail, "/books/dune/audio/0\" download>Disc 1/01.mp3") < IndexOf(detail, "/books/dune/audio/1\" download>Disc 2/02.mp3"));
        Assert.Equal("contents of Disc 2/02.mp3", await part.Content.ReadAsStringAsync());
        Assert.Equal("audio/mpeg", part.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Dune - Frank Herbert (2).mp3", part.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
        Assert.Equal("contents", await range.Content.ReadAsStringAsync());
        Assert.Equal("audio/mp4", range.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, pastTheEnd.StatusCode);
    }

    [Fact]
    public async Task Download_StoredPathOutsideTheBookFolder_IsRefused()
    {
        using var fixture = await LibraryAsync(Opds);
        await File.WriteAllTextAsync(Path.Combine(_root, "secret.mp3"), "secret");
        using (var scope = fixture.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FetchshelfDbContext>();
            var dune = await dbContext.Torrents.FindAsync("dune");
            dune!.LinkedFilesJson = JsonSerializer.Serialize(new List<LinkedFileRecord> { new("../../../secret.mp3", 6) });
            await dbContext.SaveChangesAsync();
        }

        using var response = await GetAsync(fixture, "/books/dune/audio/0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpdsLogin_GuardsDownloadsAndCovers_ButNotThePage()
    {
        using var fixture = await LibraryAsync(Opds + "username = \"reader\"\npassword = \"s3cret\"\n");

        using var page = await GetAsync(fixture, "/books");
        using var detail = await GetAsync(fixture, "/books/sword");
        using var download = await GetAsync(fixture, "/books/sword/audio/0");
        using var cover = await GetAsync(fixture, "/books/justice/cover");
        using var withLogin = await GetAsync(fixture, "/books/sword/audio/0", "reader:s3cret");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, download.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, cover.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withLogin.StatusCode);
    }

    [Fact]
    public async Task Menu_HasBooksFirstInTheLibraryMenu()
    {
        using var fixture = new FetchshelfWebAppFixture(Config());

        var html = await fixture.CreateClient().GetStringAsync("/books");

        Assert.True(IndexOf(html, "href=\"/Books\"") < IndexOf(html, "href=\"/Series\""));
    }

    [Fact]
    public async Task AudioZip_HoldsExactlyThePlacedAudioFiles_InTheirDiscFolders_Uncompressed()
    {
        using var fixture = await LibraryAsync(Opds);
        await File.WriteAllTextAsync(Path.Combine(Audiobooks, "Frank Herbert", "Dune", "cover.jpg"), "not placed by the Link job");

        using var response = await GetAsync(fixture, "/books/dune/audio.zip");
        using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Dune - Frank Herbert.zip", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(["Disc 1/01.mp3", "Disc 2/02.mp3"], zip.Entries.Select(entry => entry.FullName));
        Assert.All(zip.Entries, entry => Assert.Equal(entry.Length, entry.CompressedLength));
        using var reader = new StreamReader(zip.Entries[1].Open());
        Assert.Equal("contents of Disc 2/02.mp3", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task AudioZip_RefusedWithoutOpds_WithoutTheLogin_AndForABookWithoutAudio()
    {
        using var withoutOpds = await LibraryAsync();
        using var withLogin = await LibraryAsync(Opds + "username = \"reader\"\npassword = \"s3cret\"\n");

        using var notMapped = await GetAsync(withoutOpds, "/books/dune/audio.zip");
        using var noLogin = await GetAsync(withLogin, "/books/dune/audio.zip");
        using var loggedIn = await GetAsync(withLogin, "/books/dune/audio.zip", "reader:s3cret");
        using var ebook = await GetAsync(withLogin, "/books/justice/audio.zip", "reader:s3cret");

        Assert.Equal(HttpStatusCode.NotFound, notMapped.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, ebook.StatusCode);
    }

    [Fact]
    public async Task Detail_SingleFileAudiobookGetsAPreview_SeveralFilesAZip()
    {
        using var fixture = await LibraryAsync(Opds);
        using var withoutOpds = await LibraryAsync();
        var client = fixture.CreateClient();

        var single = await client.GetStringAsync("/books/sword");
        var several = await client.GetStringAsync("/books/dune");
        var ebook = await client.GetStringAsync("/books/justice");
        var off = await withoutOpds.CreateClient().GetStringAsync("/books/sword");

        Assert.Contains("<audio id=\"book-preview\" controls preload=\"none\" src=\"/books/sword/audio/0\">", single);
        Assert.DoesNotContain("id=\"audio-zip\"", single);
        Assert.Contains("id=\"audio-zip\" href=\"/books/dune/audio.zip\"", several);
        Assert.DoesNotContain("<audio", several);
        Assert.DoesNotContain("<audio", ebook);
        Assert.DoesNotContain("id=\"audio-zip\"", ebook);
        Assert.DoesNotContain("<audio", off);
    }
}
