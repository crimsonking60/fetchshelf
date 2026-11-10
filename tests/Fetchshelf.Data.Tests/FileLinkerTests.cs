using Fetchshelf.Data.Configuration;
using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public sealed class FileLinkerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("fetchshelf-file-linker-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Link_Hardlink_CreatesLinkedFileWithSameContent()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destination = Path.Combine(_tempDir, "dest", "source.txt");

        var outcome = FileLinker.Link(source, destination, LibraryLinkMethod.Hardlink).Outcome;

        Assert.Equal(LinkOutcome.Hardlinked, outcome);
        Assert.Equal("hello", File.ReadAllText(destination));
    }

    [Fact]
    public void Link_HardlinkOrCopy_FallsBackToCopyWhenHardlinkFails()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destination = Path.Combine(_tempDir, "dest", "source.txt");

        var outcome = FileLinker.Link(
            source, destination, LibraryLinkMethod.HardlinkOrCopy,
            hardLink: (_, _) => throw new IOException("simulated cross-device failure")).Outcome;

        Assert.Equal(LinkOutcome.Copied, outcome);
        Assert.Equal("hello", File.ReadAllText(destination));
    }

    [Fact]
    public void Link_HardlinkOrCopy_HardlinkWorks_ReportsHardlinked()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destination = Path.Combine(_tempDir, "dest", "source.txt");

        var outcome = FileLinker.Link(source, destination, LibraryLinkMethod.HardlinkOrCopy).Outcome;

        Assert.Equal(LinkOutcome.Hardlinked, outcome);
    }

    [Fact]
    public void Link_HardlinkOrSymlink_FallsBackToSymlinkWhenHardlinkFails()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destination = Path.Combine(_tempDir, "dest", "source.txt");

        var outcome = FileLinker.Link(
            source, destination, LibraryLinkMethod.HardlinkOrSymlink,
            hardLink: (_, _) => throw new IOException("simulated cross-device failure")).Outcome;

        Assert.Equal(LinkOutcome.Symlinked, outcome);
        Assert.Equal(source, new FileInfo(destination).LinkTarget);
    }

    [Theory]
    [InlineData(LibraryLinkMethod.Copy, LinkOutcome.Copied)]
    [InlineData(LibraryLinkMethod.Symlink, LinkOutcome.Symlinked)]
    [InlineData(LibraryLinkMethod.NoLink, LinkOutcome.NotPlaced)]
    public void Link_EachMethod_ReportsWhatItDid(LibraryLinkMethod method, LinkOutcome expected)
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destination = Path.Combine(_tempDir, "dest", "source.txt");

        Assert.Equal(expected, FileLinker.Link(source, destination, method).Outcome);
    }

    [Fact]
    public void Link_DestinationAlreadyExists_IsTolerated()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destination = Path.Combine(_tempDir, "dest", "source.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "hello");

        var outcome = FileLinker.Link(source, destination, LibraryLinkMethod.Hardlink).Outcome;

        Assert.Equal(LinkOutcome.AlreadyExists, outcome);
    }

    [Fact]
    public void Link_MissingSource_ThrowsClearDiagnostic()
    {
        var source = Path.Combine(_tempDir, "missing.txt");
        var destination = Path.Combine(_tempDir, "dest", "missing.txt");

        var exception = Assert.Throws<FileNotFoundException>(
            () => FileLinker.Link(source, destination, LibraryLinkMethod.Hardlink));

        Assert.Contains(source, exception.Message);
    }

    private (string Source, string Destination) SourceAndExisting(string sourceContent, string existingContent, string name = "Book.epub")
    {
        var source = Path.Combine(_tempDir, "downloads", name);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllText(source, sourceContent);
        var destination = Path.Combine(_tempDir, "library", name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, existingContent);
        return (source, destination);
    }

    [Fact]
    public void Link_DifferentFileOfTheSameNameAlreadyThere_PlacesItUnderTheNextFreeName()
    {
        var (source, destination) = SourceAndExisting("the new book", "another torrent's book");

        var result = FileLinker.Link(source, destination, LibraryLinkMethod.Copy);

        Assert.Equal(LinkOutcome.Copied, result.Outcome);
        Assert.Equal(Path.Combine(_tempDir, "library", "Book (2).epub"), result.PlacedAt);
        Assert.Equal("another torrent's book", File.ReadAllText(destination));
        Assert.Equal("the new book", File.ReadAllText(result.PlacedAt));
    }

    [Fact]
    public void Link_NumberedNameTakenByAnotherDifferentFile_GoesOnToTheNextNumber()
    {
        var (source, destination) = SourceAndExisting("the new book", "another torrent's book");
        File.WriteAllText(FileLinker.NumberedName(destination, 2), "a third, longer book");

        var result = FileLinker.Link(source, destination, LibraryLinkMethod.Copy);

        Assert.Equal(Path.Combine(_tempDir, "library", "Book (3).epub"), result.PlacedAt);
    }

    [Fact]
    public void Link_PlacedUnderANumberedNameBefore_IsFoundThereAgain()
    {
        var (source, destination) = SourceAndExisting("the new book", "another torrent's book");
        File.WriteAllText(FileLinker.NumberedName(destination, 2), "the new book");

        var result = FileLinker.Link(source, destination, LibraryLinkMethod.Copy);

        Assert.Equal(new LinkResult(LinkOutcome.AlreadyExists, FileLinker.NumberedName(destination, 2)), result);
        Assert.False(File.Exists(FileLinker.NumberedName(destination, 3)));
    }

    [Fact]
    public void Link_SymlinkToTheSourceAlreadyThere_IsAlreadyPlaced()
    {
        var (source, destination) = SourceAndExisting("book", "x");
        File.Delete(destination);
        File.CreateSymbolicLink(destination, source);

        var result = FileLinker.Link(source, destination, LibraryLinkMethod.Symlink);

        Assert.Equal(new LinkResult(LinkOutcome.AlreadyExists, destination), result);
    }

    [Fact]
    public void Link_SymlinkToSomethingElseAlreadyThere_IsLeftAlone()
    {
        var (source, destination) = SourceAndExisting("book", "x");
        var other = Path.Combine(_tempDir, "other.epub");
        File.WriteAllText(other, "book");
        File.Delete(destination);
        File.CreateSymbolicLink(destination, other);

        var result = FileLinker.Link(source, destination, LibraryLinkMethod.Symlink);

        Assert.Equal(FileLinker.NumberedName(destination, 2), result.PlacedAt);
        Assert.Equal(other, new FileInfo(destination).LinkTarget);
    }

    [Fact]
    public void Link_Copy_LeavesNoTemporaryFileAndOverwritesALeftoverOne()
    {
        var source = Path.Combine(_tempDir, "Book.epub");
        File.WriteAllText(source, "complete book");
        var destination = Path.Combine(_tempDir, "library", "Book.epub");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var leftover = Path.Combine(_tempDir, "library", ".Book.epub.fetchshelf-partial");
        File.WriteAllText(leftover, "half a bo");

        FileLinker.Link(source, destination, LibraryLinkMethod.Copy);

        Assert.Equal("complete book", File.ReadAllText(destination));
        Assert.False(File.Exists(leftover));
        Assert.Equal(["Book.epub"], Directory.GetFiles(Path.Combine(_tempDir, "library")).Select(Path.GetFileName));
    }
}
