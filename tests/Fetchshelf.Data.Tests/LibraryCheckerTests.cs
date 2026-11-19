using System.Text.Json;
using Fetchshelf.Core.Organizer;
using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Entities;
using Fetchshelf.Data.Organizer;
using Microsoft.EntityFrameworkCore;

namespace Fetchshelf.Data.Tests;

public sealed class LibraryCheckerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-library-checker-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string LibraryDir => Path.Combine(_root, "library");

    private Library[] Libraries => [new() { Name = "Audiobooks", LibraryDir = LibraryDir, Category = "audiobooks", Method = LibraryLinkMethod.Hardlink }];

    private FetchshelfDbContext CreateContext() => OrganizerTestSupport.CreateContext(Path.Combine(_root, "test.db"));

    private string Write(string relativePath, string content = "x")
    {
        var path = Path.Combine(LibraryDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task LinkAsync(FetchshelfDbContext context, long mamId, string folder, params string[] files)
    {
        var torrent = await OrganizerTestSupport.SeedTorrentAsync(context, $"hash{mamId}", mamId);
        torrent.LibraryPath = Path.Combine(LibraryDir, folder);
        torrent.LinkedFilesJson = JsonSerializer.Serialize(files.Select(f => new LinkedFileRecord(f, 1)).ToList());
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task RunAsync_FindsUnknownFoldersAndMissingFiles_AndKeepsThem()
    {
        using var context = CreateContext();
        Write("Author/Book/book.m4b");
        Write("Author/Old/book.m4b");
        Write("Author/Half/one.mp3");
        Write("@eaDir/thumb.jpg");
        Write("Author/.hidden/x");
        await LinkAsync(context, 1, "Author/Book", "book.m4b");
        await LinkAsync(context, 2, "Author/Half", "one.mp3", "two.mp3");
        await LinkAsync(context, 3, "Author/Gone", "gone.m4b");
        var results = new LibraryCheckResults();

        var summary = await new LibraryChecker(context, Libraries, results).RunAsync();

        Assert.Equal(new LibraryCheckSummary(UnknownFolders: 1, UnknownFiles: 0, FoldersMissing: 1, FilesMissing: 1), summary);
        Assert.Equal(
            [
                (LibraryFindingKind.UnknownFolder, Path.Combine(LibraryDir, "Author/Old")),
                (LibraryFindingKind.FolderMissing, Path.Combine(LibraryDir, "Author/Gone")),
                (LibraryFindingKind.FilesMissing, Path.Combine(LibraryDir, "Author/Half")),
            ],
            results.Findings.Select(f => (f.Finding.Kind, f.Finding.Path)));
        Assert.NotNull(results.CheckedAt);
        Assert.Equal(results.Findings.Count, results.Findings.Select(f => f.Id).Distinct().Count());
    }

    [Fact]
    public async Task RunAsync_RelativePathsOnRecordAndInTheConfiguration_AreTakenFromTheWorkingFolder()
    {
        using var context = CreateContext();
        Write("Author/Book/book.m4b");
        var torrent = await OrganizerTestSupport.SeedTorrentAsync(context, "hash1", 1);
        torrent.LibraryPath = Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(LibraryDir, "Author/Book"));
        torrent.LinkedFilesJson = JsonSerializer.Serialize(new List<LinkedFileRecord> { new("book.m4b", 1) });
        await context.SaveChangesAsync();
        Library[] relative = [new() { Name = "Audiobooks", LibraryDir = Path.GetRelativePath(Environment.CurrentDirectory, LibraryDir), Category = "audiobooks" }];
        var results = new LibraryCheckResults();

        var summary = await new LibraryChecker(context, relative, results).RunAsync();

        Assert.Equal(0, summary.Total);
    }

    [Fact]
    public async Task RunAsync_ReadsAnUnknownFoldersMetadataJson_ToSayWhichBookItIsProbablyAnOlderFolderOf()
    {
        using var context = CreateContext();
        Write("Brandon Sanderson/Way of Kings (old)/book.m4b");
        Write("Brandon Sanderson/Way of Kings (old)/metadata.json", """{"title":"Book 1","authors":["Brandon Sanderson"]}""");
        Write("Brandon Sanderson/Book 1/book.m4b");
        await LinkAsync(context, 1, "Brandon Sanderson/Book 1", "book.m4b");
        var results = new LibraryCheckResults();

        await new LibraryChecker(context, Libraries, results).RunAsync();

        Assert.Equal("hash1", Assert.Single(results.Findings).Finding.ProbablyOlderFolderOf?.TorrentId);
    }

    [Fact]
    public async Task Delete_UnknownFolder_RemovesItAndEmptyFoldersAbove_AndRecordsAnEvent()
    {
        using var context = CreateContext();
        Write("Gone Author/Old/book.m4b");
        Write("Author/Book/book.m4b");
        await LinkAsync(context, 1, "Author/Book", "book.m4b");
        var results = new LibraryCheckResults();
        await new LibraryChecker(context, Libraries, results).RunAsync();

        var refused = await LibraryCheckDeleter.DeleteAsync(Assert.Single(results.Findings).Finding, Libraries, context, new EventStore(context));

        Assert.Null(refused);
        Assert.False(Directory.Exists(Path.Combine(LibraryDir, "Gone Author")));
        Assert.True(File.Exists(Path.Combine(LibraryDir, "Author/Book/book.m4b")));
        var deleted = await context.Events.SingleAsync(e => e.Kind == EventKind.DeletedFromLibrary);
        Assert.Equal(Path.Combine(LibraryDir, "Gone Author/Old"), deleted.LibraryPath);
    }

    [Fact]
    public async Task Delete_FolderABookWasLinkedInSinceTheCheck_IsRefused()
    {
        using var context = CreateContext();
        Write("Author/Old/book.m4b");
        var results = new LibraryCheckResults();
        await new LibraryChecker(context, Libraries, results).RunAsync();
        await LinkAsync(context, 1, "Author/Old", "book.m4b");

        var refused = await LibraryCheckDeleter.DeleteAsync(Assert.Single(results.Findings).Finding, Libraries, context, new EventStore(context));

        Assert.Contains("A book is linked in", refused);
        Assert.True(File.Exists(Path.Combine(LibraryDir, "Author/Old/book.m4b")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../outside")]
    public async Task Delete_TheLibrarysOwnFolderOrOutsideIt_IsRefused(string relative)
    {
        using var context = CreateContext();
        var path = Path.GetFullPath(Path.Combine(LibraryDir, relative));
        Directory.CreateDirectory(path);
        var finding = new LibraryFinding(LibraryFindingKind.UnknownFolder, path, "Audiobooks");

        var refused = await LibraryCheckDeleter.DeleteAsync(finding, Libraries, context, new EventStore(context));

        Assert.Contains("is not inside a library's folder", refused);
        Assert.True(Directory.Exists(path));
    }

    [Fact]
    public async Task Delete_UnknownFileInASeriesFolder_LeavesTheBooksFiles()
    {
        using var context = CreateContext();
        Write("Saga/Saga #001 - One.cbz");
        Write("Saga/Saga #002 - Old.cbz");
        await LinkAsync(context, 1, "Saga", "Saga #001 - One.cbz");
        var library = new Library { Name = "Comics", LibraryDir = LibraryDir, Category = "comics", Method = LibraryLinkMethod.Hardlink, Layout = LibraryLayout.Series };
        var results = new LibraryCheckResults();
        await new LibraryChecker(context, [library], results).RunAsync();

        var finding = Assert.Single(results.Findings).Finding;
        Assert.Equal(LibraryFindingKind.UnknownFile, finding.Kind);
        Assert.Null(await LibraryCheckDeleter.DeleteAsync(finding, [library], context, new EventStore(context)));
        Assert.Equal(["Saga #001 - One.cbz"], Directory.GetFiles(Path.Combine(LibraryDir, "Saga")).Select(Path.GetFileName));
    }

    [Fact]
    public void Results_RemovingActedOnFindings_KeepsTheRest()
    {
        var results = new LibraryCheckResults();
        results.Set(
            [new LibraryFinding(LibraryFindingKind.UnknownFolder, "/a", "L"), new LibraryFinding(LibraryFindingKind.UnknownFolder, "/b", "L")],
            DateTimeOffset.UtcNow);

        results.Remove([results.Findings[0].Id]);

        Assert.Equal("/b", Assert.Single(results.Findings).Finding.Path);
    }
}
