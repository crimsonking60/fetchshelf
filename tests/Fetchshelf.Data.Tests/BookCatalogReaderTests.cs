using System.Text.Json;
using Fetchshelf.Core.Catalog;
using Fetchshelf.Core.Metadata;
using Fetchshelf.Core.Opds;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Catalog;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public sealed class BookCatalogReaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-catalog-reader-tests-").FullName;

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
        MediaType mediaType = MediaType.Ebook, string? audioExtension = null, params string[] files)
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
            ChosenAudioExtension = audioExtension,
            ChosenEbookExtension = ebookExtension,
            LinkedFilesJson = JsonSerializer.Serialize((files.Length == 0 ? [$"Book {id}.epub"] : files).Select(f => new LinkedFileRecord(f, 10)).ToList()),
        });
        await context.SaveChangesAsync();
    }

    private static Task<BookCatalog> ReadAsync(FetchshelfDbContext context) => new BookCatalogReader(context, Libraries).ReadAsync();

    [Fact]
    public async Task ReadAsync_ReadsEveryLinkedBookInALibrary_AndLeavesOutTheRest()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "served", "/library/ebooks/Author/Book");
        await SeedAsync(context, "comic", "/library/comics/Artist/Comic", "cbz", MediaType.ComicBook, files: "Comic.cbz");
        await SeedAsync(context, "unlinked", libraryPath: null);
        await SeedAsync(context, "nothing-chosen", "/library/ebooks/Author/Other", ebookExtension: null);
        await SeedAsync(context, "listen", "/library/audiobooks/Author/Listen", "pdf", MediaType.Audiobook, "m4b", "Listen.m4b", "Listen.pdf");
        await SeedAsync(context, "outside", "/elsewhere/Author/Book");

        var catalog = await ReadAsync(context);

        Assert.Equal(["comic", "listen", "served"], catalog.Books.Select(book => book.Id).Order());
        Assert.Equal(1, catalog.Find("comic")!.LibraryIndex);
        Assert.Equal("/library/ebooks/Author/Book", catalog.Find("served")!.Folder);
        Assert.Equal([new("Book served.epub", 10, "epub")], catalog.Find("served")!.EbookFiles);
        Assert.Equal([new("Listen.m4b", 10, "m4b")], catalog.Find("listen")!.AudioFiles);
        Assert.Equal([new("Listen.pdf", 10, "pdf")], catalog.Find("listen")!.EbookFiles);
    }

    [Fact]
    public async Task ReadAsync_SplitCollection_HasEachBookButNotTheCollection()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "collection", libraryPath: null, ebookExtension: null);
        await SeedAsync(context, "collection-1", "/library/ebooks/Author/First", files: "First.epub");
        await SeedAsync(context, "collection-2", "/library/ebooks/Author/Second", files: "Second.epub");

        var catalog = await ReadAsync(context);

        Assert.Equal(["collection-1", "collection-2"], catalog.Books.Select(book => book.Id).Order());
        Assert.Equal("First.epub", Assert.Single(catalog.Find("collection-1")!.EbookFiles).RelativePath);
    }

    [Fact]
    public async Task ReadAsync_TwoBooksInOneSeriesFolder_AreReadOnceEach_WithTheirOwnFile()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "saga1", "/library/comics/Saga", "cbz", MediaType.ComicBook, files: "Saga #001 - Saga, Volume 1.cbz");
        await SeedAsync(context, "saga2", "/library/comics/Saga", "cbz", MediaType.ComicBook, files: "Saga #002 - Saga, Volume 2.cbz");

        var catalog = await ReadAsync(context);

        Assert.Equal(["saga1", "saga2"], catalog.Books.Select(book => book.Id).Order());
        Assert.Equal("Saga #001 - Saga, Volume 1.cbz", Assert.Single(catalog.Find("saga1")!.EbookFiles).RelativePath);
        Assert.Equal("Saga #002 - Saga, Volume 2.cbz", Assert.Single(catalog.Find("saga2")!.EbookFiles).RelativePath);
    }

    [Fact]
    public async Task OpdsLibrariesSetting_NarrowsWhatIsServed()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "served", "/library/ebooks/Author/Book");
        await SeedAsync(context, "comic", "/library/comics/Artist/Comic", "cbz", MediaType.ComicBook, files: "Comic.cbz");
        await SeedAsync(context, "listen", "/library/ebooks/Author/Listen", null, MediaType.Audiobook, "m4b", "Listen.m4b");

        var catalog = await ReadAsync(context);
        var opds = OpdsCatalog.From(catalog, new OpdsSettings { Libraries = ["Ebooks"] }.ServedLibraryIndexes(Libraries));

        Assert.Equal(["served"], opds.Books.Select(book => book.Id));
        Assert.Equal(["Ebooks"], opds.Libraries.Select(library => library.Name));
        Assert.Null(new OpdsSettings().ServedLibraryIndexes(Libraries));
    }

    [Fact]
    public async Task ReadAsync_UnnamedLibrary_IsShownByItsFolderName()
    {
        using var context = OrganizerTestSupport.CreateContext(DbPath);
        await SeedAsync(context, "comic", "/library/comics/Artist/Comic", "cbz", MediaType.ComicBook, files: "Comic.cbz");

        var catalog = await ReadAsync(context);

        Assert.Equal([new(1, "comics")], catalog.Libraries);
    }
}
