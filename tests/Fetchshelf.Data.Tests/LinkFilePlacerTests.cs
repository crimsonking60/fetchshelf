using Fetchshelf.Data.Organizer;

namespace Fetchshelf.Data.Tests;

public class LinkFilePlacerTests
{
    [Fact]
    public void DestinationPath_SourceInDiscNumberedFolder_PlacesInDiscSubfolder()
    {
        var result = LinkFilePlacer.DestinationPath("/library/Author/Title", "Disc 2/track01.mp3");

        Assert.Equal(Path.Combine("/library/Author/Title", "Disc 2", "track01.mp3"), result);
    }

    [Fact]
    public void DestinationPath_SourceInNonDiscFolder_IsFlattened()
    {
        var result = LinkFilePlacer.DestinationPath("/library/Author/Title", "extras/misc/track01.mp3");

        Assert.Equal(Path.Combine("/library/Author/Title", "track01.mp3"), result);
    }

    [Fact]
    public void DestinationPath_SourceAtTopLevel_PlacesDirectlyInDestination()
    {
        var result = LinkFilePlacer.DestinationPath("/library/Author/Title", "track01.mp3");

        Assert.Equal(Path.Combine("/library/Author/Title", "track01.mp3"), result);
    }

    private const string Folder = "/library/Author/Title";

    private static IReadOnlyList<string> Place(params string[] sources) =>
        LinkFilePlacer.DestinationPaths(Folder, sources).Select(path => Path.GetRelativePath(Folder, path)).ToList();

    [Fact]
    public void DestinationPaths_NoClash_PlacesFilesAsOneAtATimeWould()
    {
        Assert.Equal(["01.mp3", "02.mp3", "Disc 2/03.mp3"], Place("Book/01.mp3", "Book/extras/02.mp3", "Book/CD2/03.mp3"));
    }

    [Fact]
    public void DestinationPaths_SameNameInTwoSubFolders_PutsTheSubFolderInFront()
    {
        Assert.Equal(["Part 1 - 01.mp3", "Part 2 - 01.mp3"], Place("Book/Part 1/01.mp3", "Book/Part 2/01.mp3"));
    }

    [Fact]
    public void DestinationPaths_WithoutACommonTopFolder_KeepsEveryFolderInTheName()
    {
        Assert.Equal(["Part 1 - 01.mp3", "Part 2 - 01.mp3"], Place("Part 1/01.mp3", "Part 2/01.mp3"));
    }

    [Fact]
    public void DestinationPaths_NestedSubFolders_AreJoinedInFront()
    {
        Assert.Equal(["Vol 1 - Part 1 - 01.mp3", "Vol 1 - Part 2 - 01.mp3"], Place("Book/Vol 1/Part 1/01.mp3", "Book/Vol 1/Part 2/01.mp3"));
    }

    [Fact]
    public void DestinationPaths_NamesDifferingOnlyInCase_CountAsAClash()
    {
        Assert.Equal(["A - Track.mp3", "B - track.MP3"], Place("Book/A/Track.mp3", "Book/B/track.MP3"));
    }

    [Fact]
    public void DestinationPaths_ClashInsideADiscFolder_KeepsTheDiscFolderAndPrefixesWithinIt()
    {
        Assert.Equal(
            ["Disc 1/Side A - 01.mp3", "Disc 1/Side B - 01.mp3", "Disc 2/01.mp3"],
            Place("Book/CD 1/Side A/01.mp3", "Book/CD 1/Side B/01.mp3", "Book/CD 2/01.mp3"));
    }

    [Fact]
    public void DestinationPaths_OnlyTheClashingFiles_GetAPrefix()
    {
        Assert.Equal(["Part 1 - 01.mp3", "Part 2 - 01.mp3", "cover.pdf"], Place("Book/Part 1/01.mp3", "Book/Part 2/01.mp3", "Book/Part 2/cover.pdf"));
    }

    [Fact]
    public void DestinationPaths_ClashLeftAfterPrefixing_IsNumbered()
    {
        Assert.Equal(["Part - 01.mp3", "part - 01 (2).mp3"], Place("Book/Part/01.mp3", "Book/part/01.mp3"));
    }

    [Fact]
    public void DestinationPaths_SingleFileTorrent_IsPlacedAsIs()
    {
        Assert.Equal(["Book.epub"], Place("Book.epub"));
    }
}
