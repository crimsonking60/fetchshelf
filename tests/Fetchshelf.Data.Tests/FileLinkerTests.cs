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

        var outcome = FileLinker.Link(source, destination, LibraryLinkMethod.Hardlink);

        Assert.Equal(LinkOutcome.Linked, outcome);
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
            hardLink: (_, _) => throw new IOException("simulated cross-device failure"));

        Assert.Equal(LinkOutcome.Linked, outcome);
        Assert.Equal("hello", File.ReadAllText(destination));
    }

    [Fact]
    public void Link_DestinationAlreadyExists_IsTolerated()
    {
        var source = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(source, "hello");
        var destination = Path.Combine(_tempDir, "dest", "source.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "hello");

        var outcome = FileLinker.Link(source, destination, LibraryLinkMethod.Hardlink);

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
}
