using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public sealed class LibraryFileRemoverTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fetchshelf-library-file-remover-tests-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relativePath)
    {
        var path = Path.Combine(_root, "book", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Remove_OnlyFetchshelfsFiles_RemovesThemDiscFoldersAndTheFolder()
    {
        Write("Disc 1/01.mp3");
        Write("Disc 2/01.mp3");
        Write("metadata.json");
        Write("metadata.opf");
        Write("cover.jpg");

        var removed = LibraryFileRemover.Remove(
            Path.Combine(_root, "book"), [new("Disc 1/01.mp3", 1), new("Disc 2/01.mp3", 1)], folderSharedWithAnotherTorrent: false);

        Assert.Equal(["Disc 1/01.mp3", "Disc 2/01.mp3"], removed);
        Assert.False(Directory.Exists(Path.Combine(_root, "book")));
    }

    [Fact]
    public void Remove_FolderHoldsOtherFiles_KeepsThemAndTheFolder()
    {
        Write("book.m4b");
        var foreign = Write("notes.txt");

        LibraryFileRemover.Remove(Path.Combine(_root, "book"), [new("book.m4b", 1)], folderSharedWithAnotherTorrent: false);

        Assert.True(File.Exists(foreign));
        Assert.False(File.Exists(Path.Combine(_root, "book", "book.m4b")));
    }

    [Fact]
    public void Remove_FolderSharedWithAnotherTorrent_KeepsCoverAndMetadata()
    {
        Write("book.mobi");
        var otherCopy = Write("book.epub");
        var metadata = Write("metadata.json");
        var cover = Write("cover.jpg");

        LibraryFileRemover.Remove(Path.Combine(_root, "book"), [new("book.mobi", 1)], folderSharedWithAnotherTorrent: true);

        Assert.False(File.Exists(Path.Combine(_root, "book", "book.mobi")));
        Assert.True(File.Exists(otherCopy));
        Assert.True(File.Exists(metadata));
        Assert.True(File.Exists(cover));
    }

    [Fact]
    public void Remove_FolderAlreadyGone_DoesNothing()
    {
        var removed = LibraryFileRemover.Remove(Path.Combine(_root, "missing"), [new("book.m4b", 1)], folderSharedWithAnotherTorrent: false);

        Assert.Empty(removed);
    }

    [Fact]
    public void Remove_FileAnotherRecordAlsoLists_IsKeptIgnoringCase()
    {
        Write("Book.epub");
        Write("Book.mobi");

        var removed = LibraryFileRemover.Remove(
            Path.Combine(_root, "book"), [new("Book.epub", 1), new("Book.mobi", 1)], folderSharedWithAnotherTorrent: true,
            filesUsedByOthers: new HashSet<string>(["book.EPUB"], StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["Book.mobi"], removed);
        Assert.True(File.Exists(Path.Combine(_root, "book", "Book.epub")));
    }
}
