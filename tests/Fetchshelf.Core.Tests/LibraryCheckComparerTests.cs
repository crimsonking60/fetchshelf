using Fetchshelf.Core.Organizer;

namespace Fetchshelf.Core.Tests;

public class LibraryCheckComparerTests
{
    private static readonly DateTimeOffset Then = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly CheckedLibrary Books = new("Audiobooks", "/lib/audio", IsSeriesLayout: false, PlacesFiles: true);

    private static readonly CheckedLibrary Comics = new("Comics", "/lib/comics", IsSeriesLayout: true, PlacesFiles: true);

    private static CheckedFolder Folder(string path, params string[] files) =>
        new(path, files.Select((name, i) => new CheckedFile(name, 10, Then.AddDays(i))).ToList());

    private static KnownBook Book(string id, string path, params string[] files) =>
        new(id, $"Title {id}", ["Author"], path, files);

    private static List<LibraryFinding> Compare(IReadOnlyList<CheckedFolder> folders, IReadOnlyList<KnownBook> books, params CheckedLibrary[] libraries) =>
        LibraryCheckComparer.Compare(libraries.Length == 0 ? [Books] : libraries, folders, books);

    [Fact]
    public void EverythingMatches_FindsNothing()
    {
        var findings = Compare(
            [Folder("/lib/audio"), Folder("/lib/audio/Author"), Folder("/lib/audio/Author/Book", "01.mp3", "metadata.json"), Folder("/lib/audio/Author/Book/Disc 2", "02.mp3")],
            [Book("a", "/lib/audio/Author/Book", "01.mp3", "Disc 2/02.mp3")]);

        Assert.Empty(findings);
    }

    [Fact]
    public void UnknownFolder_IsReportedOnceAtItsOutermostFolderWithFiles_WithEverythingBelowIt()
    {
        var findings = Compare(
            [
                Folder("/lib/audio"), Folder("/lib/audio/Author"), Folder("/lib/audio/Author/Old", "01.mp3", "metadata.json"),
                Folder("/lib/audio/Author/Old/Disc 2", "02.mp3"), Folder("/lib/audio/Author/Book", "book.m4b"),
            ],
            [Book("a", "/lib/audio/Author/Book", "book.m4b")]);

        var finding = Assert.Single(findings);
        Assert.Equal((LibraryFindingKind.UnknownFolder, "/lib/audio/Author/Old", "Audiobooks"), (finding.Kind, finding.Path, finding.LibraryName));
        Assert.Equal((30L, 3, Then.AddDays(1)), (finding.Bytes, finding.Files, finding.NewestAt));
    }

    [Fact]
    public void FolderWithOnlySideFiles_IsUnknown()
    {
        var finding = Assert.Single(Compare([Folder("/lib/audio/Author/Half done", "metadata.json")], []));

        Assert.Equal(LibraryFindingKind.UnknownFolder, finding.Kind);
    }

    [Fact]
    public void FoldersOutsideTheLibraries_AndTheLibrariesOwnFolders_AreNotReported()
    {
        Assert.Empty(Compare([Folder("/lib/audio"), Folder("/elsewhere/Book", "01.mp3"), Folder("/elsewhere/Empty")], []));
    }

    [Fact]
    public void EmptyFolders_AreReportedOnceAtTheOutermostEmptyOne()
    {
        // An author folder whose only book was unlinked by an older version, which left the series and author folders.
        var finding = Assert.Single(Compare([Folder("/lib/audio"), Folder("/lib/audio/Author"), Folder("/lib/audio/Author/Series")], []));

        Assert.Equal((LibraryFindingKind.UnknownFolder, "/lib/audio/Author"), (finding.Kind, finding.Path));
        Assert.Equal((0L, 0, (DateTimeOffset?)null), (finding.Bytes, finding.Files, finding.NewestAt));
    }

    [Fact]
    public void EmptyFolders_BesideABook_OrBesideAnUnknownFolder_AreReportedOnTheirOwn_AndFoldersInABookAreNot()
    {
        var findings = Compare(
            [
                Folder("/lib/audio"), Folder("/lib/audio/Author"), Folder("/lib/audio/Author/Book", "book.m4b"),
                Folder("/lib/audio/Author/Book/Extras"), Folder("/lib/audio/Author/Old series"),
                Folder("/lib/audio/Other"), Folder("/lib/audio/Other/Old", "01.mp3"), Folder("/lib/audio/Other/Empty"),
            ],
            [Book("a", "/lib/audio/Author/Book", "book.m4b")]);

        Assert.Equal(
            ["/lib/audio/Author/Old series", "/lib/audio/Other/Empty", "/lib/audio/Other/Old"],
            findings.Select(f => f.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void StrayFileAboveBookFolders_IsAnUnknownFile_NotItsFolder()
    {
        var findings = Compare(
            [Folder("/lib/audio", "notes.txt"), Folder("/lib/audio/Author", "author.jpg"), Folder("/lib/audio/Author/Book", "book.m4b")],
            [Book("a", "/lib/audio/Author/Book", "book.m4b")]);

        Assert.Equal(
            [(LibraryFindingKind.UnknownFile, "/lib/audio/Author/author.jpg"), (LibraryFindingKind.UnknownFile, "/lib/audio/notes.txt")],
            findings.Select(f => (f.Kind, f.Path)).Order());
    }

    [Fact]
    public void SeriesFolder_FilesNoBookListsAreUnknownFiles_ItsSideFilesAreNot()
    {
        var findings = Compare(
            [Folder("/lib/comics/Saga", "Saga #001 - One.cbz", "Saga #002 - Two.cbz", "Saga #003 - Old.cbz", "series.json", "cover.jpg")],
            [Book("a", "/lib/comics/Saga", "Saga #001 - One.cbz"), Book("b", "/lib/comics/Saga", "Saga #002 - Two.cbz")],
            Comics);

        var finding = Assert.Single(findings);
        Assert.Equal((LibraryFindingKind.UnknownFile, "/lib/comics/Saga/Saga #003 - Old.cbz", "Comics"), (finding.Kind, finding.Path, finding.LibraryName));
    }

    [Fact]
    public void BookWhoseFolderIsGone_IsFolderMissing_UnlessItsLibraryPlacesNoFiles()
    {
        var noLink = new CheckedLibrary("Records", "/lib/records", IsSeriesLayout: false, PlacesFiles: false);
        var findings = LibraryCheckComparer.Compare(
            [Books, noLink], [Folder("/lib/audio")],
            [Book("a", "/lib/audio/Author/Gone", "book.m4b"), Book("b", "/lib/records/Author/Book")]);

        var finding = Assert.Single(findings);
        Assert.Equal((LibraryFindingKind.FolderMissing, "a", "Title a"), (finding.Kind, finding.TorrentId, finding.Title));
    }

    [Fact]
    public void BookWithSomeFilesGone_IsFilesMissing_NamingThem()
    {
        var finding = Assert.Single(Compare(
            [Folder("/lib/audio/Author/Book", "01.mp3"), Folder("/lib/audio/Author/Book/Disc 2")],
            [Book("a", "/lib/audio/Author/Book", "01.mp3", "02.mp3", "Disc 2/03.mp3")]));

        Assert.Equal(LibraryFindingKind.FilesMissing, finding.Kind);
        Assert.Equal(["02.mp3", "Disc 2/03.mp3"], finding.MissingFiles);
    }

    [Fact]
    public void BookOutsideEveryLibrary_IsCheckedByTheFoldersGiven()
    {
        Assert.Empty(Compare([Folder("/old/audio/Author/Book", "book.m4b")], [Book("a", "/old/audio/Author/Book", "book.m4b")]));
    }

    [Fact]
    public void UnknownFolderLikeALinkedBook_ByItsMetadataJson_IsProbablyAnOlderFolderOfIt()
    {
        var book = new KnownBook("a", "The Way of Kings", ["Brandon Sanderson"], "/lib/audio/Brandon Sanderson/The Way of Kings {Kramer}", ["book.m4b"]);

        var findings = LibraryCheckComparer.Compare(
            [Books],
            [Folder("/lib/audio/Brandon Sanderson/Stormlight #1 - Way of Kings", "book.m4b", "metadata.json"), Folder(book.LibraryPath, "book.m4b")],
            [book],
            path => path.EndsWith("Way of Kings", StringComparison.Ordinal) ? new BookIdentity("The Way of Kings", ["brandon sanderson"]) : null);

        Assert.Equal(book, Assert.Single(findings).ProbablyOlderFolderOf);
    }

    [Fact]
    public void UnknownFolderNamedLikeABookBesideIt_IsProbablyAnOlderFolderOfIt()
    {
        var book = new KnownBook("a", "Gamma", ["Author"], "/lib/audio/Author/Gamma (Greek)", ["book.m4b"]);

        var findings = Compare([Folder("/lib/audio/Author/Gamma", "book.m4b"), Folder(book.LibraryPath, "book.m4b")], [book]);

        Assert.Equal(book, Assert.Single(findings).ProbablyOlderFolderOf);
    }
}
