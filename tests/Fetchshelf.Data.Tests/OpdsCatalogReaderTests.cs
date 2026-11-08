using System.Text.Json;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Opds;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public sealed class OpdsCatalogReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-opds-reader-tests-").FullName;

    private string DbPath => Path.Combine(_root, "test.db");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static readonly List<Library> Libraries =
    [
        new() { Name = "Ebooks", Category = "ebooks", LibraryDir = "/library/ebooks" },
        new() { Category = "comics", LibraryDir = "/library/comics/" },
        new() { Name = "Audiobooks", Category = "audiobooks", LibraryDir = "/library/audiobooks" },
    ];

    private static async Task SeedAsync(
        FetchshelfDbContext context, string id, string? libraryPath, string? ebookExtension = "epub",
        MediaType mediaType = MediaType.Ebook, params string[] files)
    {
        var metadata = OrganizerTestSupport.BuildMetadata(title: $"Book {id}") with { MediaType = mediaType };
        context.Torrents.Add(new TorrentEntity
        {
            Id = id,
            MamId = 1,
            NormalizedTitleKey = TitleNormalizer.Normalize(metadata.Title),
            Title = metadata.Title,
            MetadataJson = TorrentMetadataSerializer.Serialize(metadata),
            CreatedAt = DateTimeOffset.UtcNow,
            LibraryPath = libraryPath,
            ChosenEbookExtension = ebookExtension,
            LinkedFilesJson = JsonSerializer.Serialize((files.Length == 0 ? [$"Book {id}.epub"] : files).Select(f => new LinkedFileRecord(f, 10)).ToList()),
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ReadAsync_ServesLinkedEbooksInServedLibraries_AndLeavesOutTheRest()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "served", "/library/ebooks/Author/Book");
        await SeedAsync(context, "comic", "/library/comics/Artist/Comic", "cbz", MediaType.ComicBook, "Comic.cbz");
        await SeedAsync(context, "unlinked", libraryPath: null);
        await SeedAsync(context, "no-ebook", "/library/ebooks/Author/Other", ebookExtension: null);
        await SeedAsync(context, "audiobook-pdf", "/library/audiobooks/Author/Listen", "pdf", MediaType.Audiobook, "Listen.pdf");
        await SeedAsync(context, "outside", "/elsewhere/Author/Book");

        var catalog = await new OpdsCatalogReader(context, Libraries, new OpdsSettings()).ReadAsync();

        Assert.Equal(["comic", "served"], catalog.Books.Select(book => book.Id).Order());
        Assert.Equal(1, catalog.Find("comic")!.LibraryIndex);
        Assert.Equal("/library/ebooks/Author/Book", catalog.Find("served")!.Folder);
        Assert.Equal([new("Book served.epub", 10, "epub")], catalog.Find("served")!.Files);
    }

    [Fact]
    public async Task ReadAsync_LibrariesSetting_NarrowsWhatIsServed()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "served", "/library/ebooks/Author/Book");
        await SeedAsync(context, "comic", "/library/comics/Artist/Comic", "cbz", MediaType.ComicBook, "Comic.cbz");

        var catalog = await new OpdsCatalogReader(context, Libraries, new OpdsSettings { Libraries = ["Ebooks"] }).ReadAsync();

        Assert.Equal(["served"], catalog.Books.Select(book => book.Id));
        Assert.Equal(["Ebooks"], catalog.Libraries.Select(library => library.Name));
    }

    [Fact]
    public async Task ReadAsync_UnnamedLibrary_IsShownByItsFolderName()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "comic", "/library/comics/Artist/Comic", "cbz", MediaType.ComicBook, "Comic.cbz");

        var catalog = await new OpdsCatalogReader(context, Libraries, new OpdsSettings()).ReadAsync();

        Assert.Equal([new(1, "comics")], catalog.Libraries);
    }
}
